using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Mono.Cecil;
using TotemMod.Updating.UtillaFix;
using TotemMod.Updating;

internal static class UtillaRepairTests
{
    private static int checks;
    private static byte[] original,payload;
    private static string managed;
    private static void Check(bool value,string message) { checks++; if(!value) throw new Exception(message); }
    private static string Hash(byte[] bytes) { using(SHA256 sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-",""); }
    private sealed class Fixture : IDisposable
    {
        internal readonly string Root=Path.Combine(Path.GetTempPath(),"TotemUtillaTests-"+Guid.NewGuid().ToString("N"));
        internal string Bep { get { return Path.Combine(Root,"BepInEx"); } }
        internal string Plugins { get { return Path.Combine(Bep,"plugins"); } }
        internal string Target { get { return Path.Combine(Plugins,"Utilla.dll"); } }
        internal string Backups { get { return Path.Combine(Bep,"cache","TotemModUtilla","backups"); } }
        internal Fixture() { Directory.CreateDirectory(Plugins); File.WriteAllBytes(Target,original); }
        public void Dispose() { Delete(Root); }
        private static void Delete(string path)
        {
            foreach(string child in Directory.GetDirectories(path))
                if((File.GetAttributes(child)&FileAttributes.ReparsePoint)!=0) Directory.Delete(child); else Delete(child);
            foreach(string file in Directory.GetFiles(path)) File.Delete(file);
            Directory.Delete(path);
        }
    }
    private static bool Patched(string path)
    {
        using(AssemblyDefinition assembly=AssemblyDefinition.ReadAssembly(path))
            return assembly.MainModule.GetType("Utilla.Compat.InfectionCompatibility")!=null;
    }
    private static string Apply(Fixture f) { return UtillaRepair.Apply(f.Bep,managed,payload); }
    private static void Refuses(Action action,string label)
    {
        bool refused=false;
        try { action(); } catch(IOException) { refused=true; } catch(InvalidDataException) { refused=true; } catch(BadImageFormatException) { refused=true; }
        Check(refused,label);
    }
    private static void Bootstrap(byte[] helper)
    {
        UtillaRepairBootstrap.Validate(helper,Hash(helper));
        using(Fixture f=new Fixture())
        {
            string target=Path.Combine(f.Bep,"patchers","TotemMod.UtillaFix.dll");
            Check(UtillaRepairBootstrap.EnsureInstalled(f.Bep)=="Utilla repair ready; restart Gorilla Tag","Missing helper is staged for the next launch.");
            Check(Hash(File.ReadAllBytes(target))==Hash(helper),"Bootstrap extracts exactly the verified embedded helper.");
            Check(UtillaRepairBootstrap.EnsureInstalled(f.Bep)==null,"Matching existing helper is a no-op.");
            byte[] other={1,2,3}; File.WriteAllBytes(target,other);
            Check(UtillaRepairBootstrap.EnsureInstalled(f.Bep).IndexOf("retained",StringComparison.OrdinalIgnoreCase)>=0,"Different existing helper is retained.");
            Check(Hash(File.ReadAllBytes(target))==Hash(other),"Bootstrap never overwrites an existing different helper.");
        }
        byte[] corrupt=(byte[])helper.Clone(); corrupt[corrupt.Length-1]^=1;
        Refuses(delegate { UtillaRepairBootstrap.Validate(corrupt,Hash(helper)); },"Tampered helper fails embedded digest verification.");
        Refuses(delegate { UtillaRepairBootstrap.Validate(new byte[] {1,2,3},Hash(new byte[] {1,2,3})); },"Malformed helper fails assembly verification even with a matching digest.");
        Refuses(delegate { UtillaRepairBootstrap.Validate(helper,"incomplete"); },"Incomplete digest is rejected.");
        using(Fixture f=new Fixture())
        {
            string outside=Path.Combine(f.Root,"external-patchers"),link=Path.Combine(f.Bep,"patchers");
            Directory.CreateDirectory(outside); Junction(link,outside);
            Refuses(delegate { UtillaRepairBootstrap.EnsureInstalled(f.Bep); },"Bootstrap refuses a redirected patchers directory.");
            Check(Directory.GetFiles(outside).Length==0,"Redirected patchers destination stays untouched.");
        }
    }
    private static void Junction(string link,string destination)
    {
        ProcessStartInfo start=new ProcessStartInfo("cmd.exe","/c mklink /J \""+link+"\" \""+destination+"\"");
        start.UseShellExecute=false; start.CreateNoWindow=true; start.RedirectStandardOutput=true; start.RedirectStandardError=true;
        using(Process process=Process.Start(start)) { process.WaitForExit(); Check(process.ExitCode==0,"Junction fixture is available."); }
    }
    private static void Retained(Fixture f,Action action,string label)
    {
        string before=Hash(File.ReadAllBytes(f.Target));
        try { action(); } catch(IOException) { } catch(UnauthorizedAccessException) { }
        Check(Hash(File.ReadAllBytes(f.Target))==before,label);
    }
    public static int Main(string[] args)
    {
        try
        {
            original=File.ReadAllBytes(typeof(UtillaRepairTests).Assembly.Location); payload=File.ReadAllBytes(args[1]); managed=args[2];
            Bootstrap(File.ReadAllBytes(args[3]));
            using(Fixture f=new Fixture())
                Retained(f,delegate { Apply(f); },"A DLL without the Utilla plugin identity is untouched.");
            if(args.Length>4 && File.Exists(args[4]))
            {
                using(Fixture f=new Fixture())
                {
                    byte[] fixedVersion=File.ReadAllBytes(args[4]);
                    if(Patched(args[4]))
                    {
                        File.WriteAllBytes(f.Target,fixedVersion);
                        Check(Apply(f).IndexOf("already",StringComparison.OrdinalIgnoreCase)>=0,"The actual installed compatible Utilla is recognized in a temporary fixture.");
                        Check(Hash(File.ReadAllBytes(f.Target))==Hash(fixedVersion),"Actual compatible Utilla bytes remain unchanged.");
                    }
                }
            }
            if(!File.Exists(args[0]))
            {
                Console.WriteLine("SKIP original-Utilla patch integration: local original fixture is unavailable.");
                Console.WriteLine("PASS "+checks+" Utilla bootstrap, identity and installed-version checks");
                return 0;
            }
            original=File.ReadAllBytes(args[0]);
            using(Fixture f=new Fixture())
            {
                string settings=Path.Combine(f.Bep,"config"); Directory.CreateDirectory(settings);
                File.WriteAllText(Path.Combine(settings,"Utilla.cfg"),"keep settings");
                string status=Apply(f);
                Check(Patched(f.Target),"Known original must receive the compatibility payload.");
                string[] backups=Directory.GetFiles(f.Backups);
                Check(backups.Length==1 && Hash(File.ReadAllBytes(backups[0]))==Hash(original),"Atomic replacement preserves an exact original outside plugins.");
                Check(File.ReadAllText(Path.Combine(settings,"Utilla.cfg"))=="keep settings","Repair preserves settings.");
                string repairedHash=Hash(File.ReadAllBytes(f.Target));
                Check(Apply(f).IndexOf("already",StringComparison.OrdinalIgnoreCase)>=0,"Already compatible installation is recognized.");
                Check(Hash(File.ReadAllBytes(f.Target))==repairedHash && Directory.GetFiles(f.Backups).Length==1,"Repair is idempotent.");
                Check(Directory.GetFiles(f.Plugins,"*",SearchOption.AllDirectories).Length==1,"No temporary or backup DLL enters plugin discovery.");
                Console.WriteLine(status);
            }
            using(Fixture f=new Fixture())
            {
                string nested=Path.Combine(f.Plugins,"Utilla","nested"); Directory.CreateDirectory(nested);
                File.Move(f.Target,Path.Combine(nested,"renamed.dll"));
                Apply(f); Check(Patched(Path.Combine(nested,"renamed.dll")),"One nested, renamed Utilla identity is found.");
            }
            using(Fixture f=new Fixture())
            {
                Directory.CreateDirectory(Path.Combine(f.Plugins,"nested"));
                File.WriteAllBytes(Path.Combine(f.Plugins,"nested","copy.dll"),original);
                Retained(f,delegate { Apply(f); },"Duplicate Utilla identities remain untouched.");
                Check(!Patched(Path.Combine(f.Plugins,"nested","copy.dll")),"Duplicate copy also remains untouched.");
            }
            using(Fixture f=new Fixture())
            {
                byte[] modified=(byte[])original.Clone(); modified[modified.Length-1]^=1; File.WriteAllBytes(f.Target,modified);
                Retained(f,delegate { Apply(f); },"Modified Utilla digest remains untouched.");
            }
            using(Fixture f=new Fixture())
            {
                string wrong=Path.Combine(f.Root,"managed"); Directory.CreateDirectory(wrong);
                File.WriteAllBytes(Path.Combine(wrong,"Assembly-CSharp.dll"),new byte[] {1,2,3});
                Retained(f,delegate { UtillaRepair.Apply(f.Bep,wrong,payload); },"Unsupported game digest remains untouched.");
                using(FileStream locked=new FileStream(f.Target,FileMode.Open,FileAccess.Read,FileShare.None))
                {
                    bool refused=false;
                    try { Apply(f); } catch(IOException) { refused=true; }
                    Check(refused,"A locked candidate is rejected before changing it.");
                }
                Check(Hash(File.ReadAllBytes(f.Target))==Hash(original),"Locked installation retains original bytes.");
            }
            using(Fixture f=new Fixture())
            {
                string outside=Path.Combine(f.Root,"outside"),link=Path.Combine(f.Plugins,"linked");
                Directory.CreateDirectory(outside); File.WriteAllBytes(Path.Combine(outside,"copy.dll"),original);
                Junction(link,outside);
                Apply(f);
                Check(Patched(f.Target) && Hash(File.ReadAllBytes(Path.Combine(outside,"copy.dll")))==Hash(original),"Nested reparse directories are never traversed or modified.");
            }
            using(Fixture f=new Fixture())
            {
                string cache=Path.Combine(f.Bep,"cache","TotemModUtilla"); Directory.CreateDirectory(cache);
                using(FileStream locked=new FileStream(Path.Combine(cache,"repair.lock"),FileMode.Create,FileAccess.ReadWrite,FileShare.None))
                {
                    bool refused=false; try { Apply(f); } catch(IOException) { refused=true; }
                    Check(refused,"Concurrent repair lock is respected.");
                }
                Check(Hash(File.ReadAllBytes(f.Target))==Hash(original),"Concurrent repair leaves original bytes untouched.");
            }
            Console.WriteLine("PASS "+checks+" Utilla repair identity, backup, idempotence, nested paths, unsupported versions, locks and junction checks");
            return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
