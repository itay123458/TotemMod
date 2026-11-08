using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using TotemMod.Updating;

internal static class MaceUpdateInstallerTests
{
    private static int checks;
    private static string privateKey;
    private static byte[] original, candidate, totemOriginal, totemCandidate;
    private static readonly byte[] game = Encoding.UTF8.GetBytes("Companion updater supported game fixture");
    private static Action<string,UpdateDownload> stage;
    private static Func<string,string> pendingVersion;
    private static Func<string,UpdateManifest> pendingManifest;
    private static Func<string,bool> cancel;
    private static Func<string,string,string,string> apply;
    private static Func<string,Version> installedVersion;

    private sealed class Fixture : IDisposable
    {
        internal readonly string Root, Bep, Plugin, Totem, Game, Cache;
        internal string Pending { get { return Path.Combine(Cache,"pending"); } }
        internal Fixture()
        {
            Root=Path.Combine(Path.GetTempPath(),"MaceUpdaterTests-"+Guid.NewGuid().ToString("N"));
            Bep=Path.Combine(Root,"BepInEx"); Plugin=Path.Combine(Bep,"plugins","MaceMod.dll");
            Totem=Path.Combine(Bep,"plugins","TotemMod.dll"); Game=Path.Combine(Root,"Assembly-CSharp.dll");
            Cache=Path.Combine(Bep,"cache","MaceModUpdater");
            Directory.CreateDirectory(Path.GetDirectoryName(Plugin));
            File.WriteAllBytes(Plugin,original); File.WriteAllBytes(Totem,totemOriginal); File.WriteAllBytes(Game,game);
        }
        public void Dispose() { DeleteFixture(Root,Root); }
    }

    private static void Check(bool condition,string label)
    { checks++; if(!condition) throw new Exception(label); }
    private static void Reject(Action action,string label)
    {
        bool rejected=false;
        try { action(); }
        catch(Exception error)
        {
            if(!(error is IOException) && !(error is InvalidDataException) && !(error is UnauthorizedAccessException) && !(error is CryptographicException) && !(error is BadImageFormatException)) throw;
            rejected=true;
        }
        Check(rejected,label);
    }
    private static string Hash(byte[] bytes)
    {
        using(SHA256 sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
    }
    private static byte[] Rewrite(byte[] dll,string version,string guid)
    {
        using(MemoryStream input=new MemoryStream(dll,false))
        using(AssemblyDefinition assembly=AssemblyDefinition.ReadAssembly(input))
        using(MemoryStream output=new MemoryStream())
        {
            CustomAttribute attribute=assembly.MainModule.Types.SelectMany(t=>t.CustomAttributes).Single(a=>a.AttributeType.FullName=="BepInEx.BepInPlugin");
            attribute.ConstructorArguments[0]=new CustomAttributeArgument(assembly.MainModule.TypeSystem.String,guid);
            attribute.ConstructorArguments[2]=new CustomAttributeArgument(assembly.MainModule.TypeSystem.String,version);
            assembly.Write(output); return output.ToArray();
        }
    }
    private static UpdateDownload Download(byte[] payload,string version,bool mace,string gameHash,int minimum)
    {
        UpdateManifest manifest=new UpdateManifest {
            Format=UpdateTrust.Protocol,Version=version,
            AssetUrl="https://github.com/"+UpdateTrust.Repository+"/releases/download/v"+version+"/"+(mace?"MaceMod.dll":"TotemMod.dll"),
            Sha256=Hash(payload),Size=payload.LongLength,GameSha256=gameHash,MinimumUpdater=minimum
        };
        // Product is deliberately set through metadata so this test can establish a
        // meaningful missing-installer RED before the product-aware API is present.
        FieldInfo product=typeof(UpdateManifest).GetField("Product",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public);
        Check(product!=null,"manifest carries an explicit update product");
        product.SetValue(manifest,Enum.Parse(product.FieldType,mace?"Mace":"Totem"));
        using(RSACryptoServiceProvider rsa=new RSACryptoServiceProvider())
        {
            rsa.PersistKeyInCsp=false; rsa.FromXmlString(privateKey);
            manifest.Signature=Convert.ToBase64String(rsa.SignData(manifest.CanonicalBytes(),CryptoConfig.MapNameToOID("SHA256")));
        }
        manifest.Validate(); return new UpdateDownload(manifest,payload);
    }
    private static UpdateDownload Good() { return Download(candidate,"2.0.0",true,Hash(game),UpdateTrust.UpdaterVersion); }
    private static bool Unchanged(Fixture f) { return File.ReadAllBytes(f.Plugin).SequenceEqual(original) && File.ReadAllBytes(f.Totem).SequenceEqual(totemOriginal); }
    private static void Apply(Fixture f) { apply(f.Bep,f.Plugin,f.Game); }

    private static void BindApi()
    {
        Type type=typeof(UpdateInstaller).Assembly.GetType("TotemMod.Updating.MaceUpdateInstaller");
        Check(type!=null,"Mace product has its own installer API");
        BindingFlags flags=BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public;
        stage=(Action<string,UpdateDownload>)Delegate.CreateDelegate(typeof(Action<string,UpdateDownload>),type.GetMethod("Stage",flags));
        pendingVersion=(Func<string,string>)Delegate.CreateDelegate(typeof(Func<string,string>),type.GetMethod("PendingVersion",flags));
        pendingManifest=(Func<string,UpdateManifest>)Delegate.CreateDelegate(typeof(Func<string,UpdateManifest>),type.GetMethod("PendingManifest",flags));
        cancel=(Func<string,bool>)Delegate.CreateDelegate(typeof(Func<string,bool>),type.GetMethod("CancelPending",flags));
        apply=(Func<string,string,string,string>)Delegate.CreateDelegate(typeof(Func<string,string,string,string>),type.GetMethod("ApplyPending",flags));
        installedVersion=(Func<string,Version>)Delegate.CreateDelegate(typeof(Func<string,Version>),type.GetMethod("InstalledVersion",flags));
    }

    private static void HappyAndIsolation()
    {
        using(Fixture f=new Fixture())
        {
            Check(apply(f.Bep,f.Plugin,f.Game)==null && pendingVersion(f.Bep)==null && !cancel(f.Bep),"no staged Mace update is a no-op");
            Check(installedVersion(f.Plugin)==new Version(1,0,0),"Mace baseline reads BepInPlugin metadata without loading game code");
            stage(f.Bep,Good());
            Check(Unchanged(f),"staging leaves both active plugin files unchanged");
            Check(pendingVersion(f.Bep)=="2.0.0" && pendingManifest(f.Bep).AssetUrl.EndsWith("/MaceMod.dll",StringComparison.Ordinal),"Mace pending metadata reads its own signed manifest");
            Check(UpdateInstaller.PendingVersion(f.Bep)==null,"Totem pending query cannot discover Mace pending state");
            UpdateInstaller.Stage(f.Bep,Download(totemCandidate,"2.0.0",false,Hash(game),UpdateTrust.UpdaterVersion));
            Check(pendingVersion(f.Bep)=="2.0.0" && UpdateInstaller.PendingVersion(f.Bep)=="2.0.0","both products stage independently");
            Check(cancel(f.Bep) && UpdateInstaller.PendingVersion(f.Bep)=="2.0.0","Mace cancellation preserves Totem pending stage");
            stage(f.Bep,Good());
            string status=apply(f.Bep,f.Plugin,f.Game);
            Check(status.Contains("2.0.0") && File.ReadAllBytes(f.Plugin).SequenceEqual(candidate),"Mace candidate installs atomically");
            Check(File.ReadAllBytes(f.Totem).SequenceEqual(totemOriginal),"Mace application never replaces Totem DLL");
            Check(UpdateInstaller.ApplyPending(f.Bep,f.Totem,f.Game).Contains("2.0.0") && File.ReadAllBytes(f.Totem).SequenceEqual(totemCandidate),"Totem pending update can install in the same startup");
            string[] backups=Directory.GetFiles(Path.Combine(f.Cache,"backups"));
            Check(backups.Length==1 && File.ReadAllBytes(backups[0]).SequenceEqual(original),"Mace backup preserves exact old DLL outside plugins");
            Check(!Directory.Exists(f.Pending) && Directory.GetFiles(Path.GetDirectoryName(f.Plugin)).Length==2,"successful install leaves no duplicate or temporary plugin DLL");
        }
        using(Fixture f=new Fixture())
        {
            File.Delete(f.Plugin);
            Check(apply(f.Bep,f.Plugin,f.Game)==null,"absent Mace without pending state stays untouched");
            stage(f.Bep,Good()); Reject(delegate { Apply(f); },"pending update cannot create a missing Mace installation");
            Check(!File.Exists(f.Plugin) && File.ReadAllBytes(f.Totem).SequenceEqual(totemOriginal),"absent Mace never modifies Totem installation");
        }
    }

    private static void Rejections(byte[] maceFixture)
    {
        using(Fixture f=new Fixture())
        {
            UpdateDownload bad=Good(); bad.Manifest.Signature=Convert.ToBase64String(new byte[256]);
            Reject(delegate { stage(f.Bep,bad); },"invalid publisher signature rejected");
            Check(!Directory.Exists(f.Cache) && Unchanged(f),"signature rejection writes no stage");
            Reject(delegate { stage(f.Bep,Download(totemCandidate,"2.0.0",false,Hash(game),1)); },"Totem download cannot enter Mace stage");
            Reject(delegate { UpdateInstaller.Stage(f.Bep,Good()); },"Mace download cannot enter Totem stage");
            Reject(delegate { stage(f.Bep,Download(Rewrite(maceFixture,"2.0.0","other.plugin"),"2.0.0",true,Hash(game),1)); },"candidate BepInPlugin GUID must be Mace");
            Reject(delegate { stage(f.Bep,Download(candidate,"3.0.0",true,Hash(game),1)); },"candidate metadata version matches signed manifest");
            Reject(delegate { stage(f.Bep,Download(Encoding.UTF8.GetBytes("not a dll"),"2.0.0",true,Hash(game),1)); },"signed non-assembly payload rejected");
            Reject(delegate { stage(f.Bep,Download(candidate,"2.0.0",true,Hash(game),UpdateTrust.UpdaterVersion+1)); },"newer updater requirement rejected");
            byte[] tampered=(byte[])candidate.Clone(); tampered[tampered.Length-1]^=1;
            Reject(delegate { stage(f.Bep,new UpdateDownload(Good().Manifest,tampered)); },"candidate payload hash rechecked before staging");
            Reject(delegate { installedVersion(f.Totem); },"baseline metadata reader rejects Totem GUID");
            Reject(delegate { installedVersion("MaceMod.dll"); },"baseline metadata reader requires an absolute path");
        }
        using(Fixture f=new Fixture())
        {
            stage(f.Bep,Good());
            UpdateDownload totem=Download(totemCandidate,"2.0.0",false,Hash(game),1);
            File.WriteAllBytes(Path.Combine(f.Pending,"mace-update.json"),totem.Manifest.ToJson());
            File.WriteAllBytes(Path.Combine(f.Pending,"MaceMod.dll"),totem.Payload);
            Reject(delegate { pendingManifest(f.Bep); },"Mace pending reader rejects a Totem-signed manifest placed in its cache");
            Reject(delegate { Apply(f); },"Mace apply cannot accept renamed Totem pending files");
            Check(Unchanged(f),"cross-product pending replacement preserves both plugins");
        }
        using(Fixture f=new Fixture())
        {
            using(FileStream oversized=new FileStream(f.Plugin,FileMode.Create,FileAccess.Write)) oversized.SetLength(UpdateTrust.MaximumDllBytes+1L);
            Reject(delegate { installedVersion(f.Plugin); },"baseline metadata inspection rejects oversized DLL before parsing");
        }
        using(Fixture f=new Fixture())
        {
            stage(f.Bep,Good()); File.AppendAllText(Path.Combine(f.Pending,"mace-update.json"),"tampered");
            Reject(delegate { Apply(f); },"pending manifest tamper rejected"); Check(Unchanged(f),"manifest tamper preserves both plugins");
            cancel(f.Bep); stage(f.Bep,Good());
            byte[] bytes=File.ReadAllBytes(Path.Combine(f.Pending,"MaceMod.dll")); bytes[bytes.Length-1]^=1; File.WriteAllBytes(Path.Combine(f.Pending,"MaceMod.dll"),bytes);
            Reject(delegate { Apply(f); },"pending payload tamper rejected"); Check(Unchanged(f),"payload tamper preserves both plugins");
        }
        using(Fixture f=new Fixture())
        {
            stage(f.Bep,Good()); File.WriteAllText(f.Game,"other build");
            Reject(delegate { Apply(f); },"unsupported game build rejected"); Check(Unchanged(f),"game mismatch preserves installed plugin");
            File.Delete(f.Game); Reject(delegate { Apply(f); },"missing game assembly rejected");
        }
        using(Fixture f=new Fixture())
        {
            stage(f.Bep,Download(original,"1.0.0",true,Hash(game),1)); Reject(delegate { Apply(f); },"same version rejected");
            stage(f.Bep,Download(Rewrite(maceFixture,"0.9.0","macemod.gtag.rhys"),"0.9.0",true,Hash(game),1));
            Reject(delegate { Apply(f); },"Mace downgrade rejected"); Check(Unchanged(f),"version rejection preserves plugins");
            stage(f.Bep,Good()); Reject(delegate { apply(f.Bep,f.Totem,f.Game); },"Mace target cannot be redirected to Totem");
            Reject(delegate { apply(f.Bep,Path.Combine(f.Root,"MaceMod.dll"),f.Game); },"Mace target cannot escape fixed plugins path");
            File.WriteAllBytes(f.Plugin,totemOriginal); Reject(delegate { Apply(f); },"installed Mace path with wrong plugin GUID rejected");
        }
        using(Fixture f=new Fixture())
        {
            stage(f.Bep,Good()); File.WriteAllText(Path.Combine(f.Pending,"keep.txt"),"keep");
            Reject(delegate { Apply(f); },"unexpected pending content blocks apply");
            Reject(delegate { cancel(f.Bep); },"unexpected pending content blocks cancellation");
            Reject(delegate { stage(f.Bep,Good()); },"unexpected pending content blocks restaging");
            Check(File.ReadAllText(Path.Combine(f.Pending,"keep.txt"))=="keep" && Unchanged(f),"conflict preserves foreign content and plugins");
        }
        using(Fixture f=new Fixture())
        {
            stage(f.Bep,Good());
            using(FileStream hold=new FileStream(Path.Combine(f.Cache,"update.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))
            {
                Reject(delegate { stage(f.Bep,Good()); },"Mace stage respects exclusive cache lock");
                Reject(delegate { Apply(f); },"Mace apply respects exclusive cache lock");
                Reject(delegate { cancel(f.Bep); },"Mace cancel respects exclusive cache lock");
                UpdateInstaller.Stage(f.Bep,Download(totemCandidate,"2.0.0",false,Hash(game),1));
                Check(UpdateInstaller.PendingVersion(f.Bep)=="2.0.0","Mace cache lock does not block Totem cache");
            }
            using(FileStream hold=new FileStream(f.Plugin,FileMode.Open,FileAccess.Read,FileShare.None))
                Reject(delegate { Apply(f); },"locked installed Mace fails safely");
            Check(Unchanged(f) && Directory.Exists(f.Pending),"locked apply retains old Mace and retryable stage");
            stage(f.Bep,Good());
            Check(!Directory.GetDirectories(f.Cache).Any(p=>Path.GetFileName(p).StartsWith("stage-",StringComparison.Ordinal)||Path.GetFileName(p).StartsWith("previous-",StringComparison.Ordinal)),"restaging cleans only owned scratch directories");
            Check(cancel(f.Bep) && !Directory.Exists(f.Pending) && Unchanged(f),"cancel leaves current plugins unchanged");
        }
        using(Fixture f=new Fixture())
        {
            stage(f.Bep,Good()); File.Delete(Path.Combine(f.Pending,"ready"));
            Reject(delegate { Apply(f); },"incomplete Mace stage rejected"); Check(cancel(f.Bep),"incomplete owned stage may be canceled");
        }
    }

    private static void Junctions()
    {
        using(Fixture f=new Fixture())
        {
            Directory.CreateDirectory(f.Cache); string outside=Path.Combine(f.Root,"outside"); Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside,"keep.txt"),"keep"); CreateJunction(f.Pending,outside);
            try
            {
                Reject(delegate { stage(f.Bep,Good()); },"Mace stage rejects pending junction");
                Reject(delegate { Apply(f); },"Mace apply rejects pending junction");
                Reject(delegate { cancel(f.Bep); },"Mace cancel rejects pending junction");
                Check(File.ReadAllText(Path.Combine(outside,"keep.txt"))=="keep" && Unchanged(f),"junction rejection preserves external data");
            }
            finally { Directory.Delete(f.Pending,false); }
        }
        using(Fixture f=new Fixture())
        {
            stage(f.Bep,Good()); string plugins=Path.GetDirectoryName(f.Plugin),outside=Path.Combine(f.Root,"other-plugins");
            Directory.Move(plugins,outside); CreateJunction(plugins,outside);
            try
            {
                Reject(delegate { Apply(f); },"Mace apply rejects plugins junction");
                Reject(delegate { installedVersion(f.Plugin); },"Mace baseline reader rejects reparse ancestry");
                Check(File.ReadAllBytes(Path.Combine(outside,"MaceMod.dll")).SequenceEqual(original),"plugins junction rejection preserves Mace");
            }
            finally { Directory.Delete(plugins,false); Directory.Move(outside,plugins); }
        }
    }

    private static void CreateJunction(string link,string target)
    {
        ProcessStartInfo start=new ProcessStartInfo("cmd.exe","/d /c mklink /J \""+link+"\" \""+target+"\"");
        start.UseShellExecute=false; start.CreateNoWindow=true; start.WindowStyle=ProcessWindowStyle.Hidden;
        start.RedirectStandardOutput=true; start.RedirectStandardError=true;
        using(Process process=Process.Start(start))
        {
            string output=process.StandardOutput.ReadToEnd()+process.StandardError.ReadToEnd(); process.WaitForExit();
            if(process.ExitCode!=0) throw new IOException("Could not create fixture junction: "+output);
        }
    }
    private static void DeleteFixture(string root,string path)
    {
        string full=Path.GetFullPath(path),prefix=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        if(full!=prefix && !full.StartsWith(prefix+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Cleanup escaped fixture root.");
        if((File.GetAttributes(full)&FileAttributes.ReparsePoint)!=0) throw new InvalidOperationException("Cleanup will not traverse reparse points.");
        foreach(string file in Directory.GetFiles(full)) File.Delete(file);
        foreach(string directory in Directory.GetDirectories(full)) DeleteFixture(prefix,directory);
        Directory.Delete(full,false);
    }

    private static void CompanionHelper(string path)
    {
        using(AssemblyDefinition assembly=AssemblyDefinition.ReadAssembly(path))
        {
            Check(assembly.Name.Name=="TotemMod.CompanionUpdater","companion helper has a distinct assembly identity");
            TypeDefinition patcher=assembly.MainModule.GetType("TotemMod.Updating.CompanionPreloader.Patcher");
            Check(patcher!=null,"companion helper exposes a dedicated preloader Patcher");
            MethodDefinition initialize=patcher.Methods.Single(m=>m.Name=="Initialize");
            MethodDefinition targets=patcher.Methods.Single(m=>m.Name=="get_TargetDLLs");
            Check(targets.Body.Instructions.Count(i=>i.OpCode.Code==Code.Newarr)==1 &&
                targets.Body.Instructions.Any(i=>i.OpCode.Code==Code.Ldc_I4_0),"companion preloader has no patch targets");
            string[] owners={"TotemMod.Updating.UpdateInstaller","TotemMod.Updating.MaceUpdateInstaller"};
            ExceptionHandler previous=null;
            foreach(string owner in owners)
            {
                Instruction[] calls=initialize.Body.Instructions.Where(i=>i.Operand is MethodReference &&
                    ((MethodReference)i.Operand).DeclaringType.FullName==owner && ((MethodReference)i.Operand).Name=="ApplyPending").ToArray();
                Check(calls.Length==1,"companion applies each updater exactly once: "+owner);
                int call=initialize.Body.Instructions.IndexOf(calls[0]);
                ExceptionHandler handler=initialize.Body.ExceptionHandlers.SingleOrDefault(h=>h.HandlerType==ExceptionHandlerType.Catch &&
                    h.CatchType.FullName=="System.Exception" && initialize.Body.Instructions.IndexOf(h.TryStart)<=call &&
                    initialize.Body.Instructions.IndexOf(h.TryEnd)>call);
                Check(handler!=null && handler!=previous,"each product apply has an independent guarded try/catch");
                if(previous!=null)
                    Check(initialize.Body.Instructions.IndexOf(previous.HandlerEnd)<=initialize.Body.Instructions.IndexOf(handler.TryStart),
                        "Totem error handler completes before independent Mace attempt");
                bool logs=false,fixedName=false;
                string dll=owner.EndsWith("MaceUpdateInstaller",StringComparison.Ordinal)?"MaceMod.dll":"TotemMod.dll";
                for(int i=initialize.Body.Instructions.IndexOf(handler.HandlerStart);i<initialize.Body.Instructions.IndexOf(handler.HandlerEnd);i++)
                {
                    MethodReference method=initialize.Body.Instructions[i].Operand as MethodReference;
                    if(method!=null && method.Name=="LogError") logs=true;
                }
                for(int i=initialize.Body.Instructions.IndexOf(handler.TryStart);i<call;i++)
                    if(initialize.Body.Instructions[i].OpCode.Code==Code.Ldstr && (string)initialize.Body.Instructions[i].Operand==dll) fixedName=true;
                Check(logs && fixedName,"each guarded apply logs failure and uses its fixed plugin DLL path");
                previous=handler;
            }
        }
    }

    public static int Main(string[] args)
    {
        try
        {
            Check(args.Length==3 || args.Length==4,"Usage: MaceUpdateInstallerTests MaceMod.dll TotemMod.dll signing-key.xml [CompanionUpdater.dll]"); BindApi();
            privateKey=File.ReadAllText(args[2]); byte[] maceFixture=File.ReadAllBytes(args[0]),totemFixture=File.ReadAllBytes(args[1]);
            original=Rewrite(maceFixture,"1.0.0","macemod.gtag.rhys"); candidate=Rewrite(maceFixture,"2.0.0","macemod.gtag.rhys");
            totemOriginal=Rewrite(totemFixture,"1.0.0",UpdateTrust.PluginGuid); totemCandidate=Rewrite(totemFixture,"2.0.0",UpdateTrust.PluginGuid);
            HappyAndIsolation(); Rejections(maceFixture); Junctions();
            if(args.Length==4) CompanionHelper(args[3]);
            Console.WriteLine("PASS MaceUpdateInstallerTests ("+checks+" checks)"); return 0;
        }
        catch(Exception error) { Console.Error.WriteLine("FAIL MaceUpdateInstallerTests: "+error); return 1; }
        finally { privateKey=null; }
    }
}
