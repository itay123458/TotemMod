using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using Mono.Cecil;

internal static class CompanionBootstrapTests
{
    private static int checks;
    private static Type bootstrap;
    private static void Check(bool value,string message) { checks++; if(!value) throw new Exception(message); }
    private static string Hash(byte[] data) { using(SHA256 sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(data)).Replace("-",""); }
    private static object Call(string method,params object[] args) { return bootstrap.GetMethod(method,BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,args); }
    private static void Reject(Action action,string message)
    { bool rejected=false; try { action(); } catch(TargetInvocationException error) { rejected=error.InnerException is InvalidDataException || error.InnerException is IOException || error.InnerException is BadImageFormatException; } Check(rejected,message); }
    public static int Main(string[] args)
    {
        try
        {
            bootstrap=Assembly.GetExecutingAssembly().GetType("TotemMod.Updating.CompanionBootstrap");
            Check(bootstrap!=null,"Both products need a bootstrap for the shared restart updater.");
            byte[] helper=File.ReadAllBytes(args[0]); string hash=Hash(helper);
            using(AssemblyDefinition assembly=AssemblyDefinition.ReadAssembly(args[0]))
            {
                TypeDefinition patcher=assembly.MainModule.GetType("TotemMod.Updating.CompanionPreloader.Patcher");
                Check(patcher!=null,"Expected BepInEx companion patcher exists.");
                bool totem=false,mace=false;
                foreach(MethodDefinition method in patcher.Methods) if(method.Name=="Initialize")
                    foreach(var instruction in method.Body.Instructions)
                    {
                        MethodReference called=instruction.Operand as MethodReference;
                        if(called==null || called.Name!="ApplyPending") continue;
                        totem|=called.DeclaringType.Name=="UpdateInstaller";
                        mace|=called.DeclaringType.Name=="MaceUpdateInstaller";
                    }
                Check(totem && mace,"Startup helper must process both independent signed queues.");
            }
            string root=Path.Combine(Path.GetTempPath(),"MaceCompanionBootstrap-"+Guid.NewGuid().ToString("N"));
            string target=Path.Combine(root,"patchers","TotemMod.CompanionUpdater.dll");
            string status=(string)Call("Install",root,helper,hash);
            Check(status!=null && File.Exists(target),"Missing helper installs for the next launch.");
            Check(Hash(File.ReadAllBytes(target))==hash,"Installed helper exactly matches embedded bytes.");
            Check(Call("Install",root,helper,hash)==null,"Matching helper is left unchanged.");
            File.WriteAllBytes(target,new byte[] {1,2,3});
            Check(Call("Install",root,helper,hash)!=null,"Different existing helper is reported for review.");
            Check(File.ReadAllBytes(target).Length==3,"Bootstrap never overwrites a currently loaded different helper.");
            byte[] corrupt=(byte[])helper.Clone(); corrupt[corrupt.Length-1]^=1;
            Reject(()=>Call("Install",root,corrupt,hash),"Corruption fails before any replacement.");
            Reject(()=>Call("Install",root,new byte[] {1,2,3},Hash(new byte[] {1,2,3})),"Malformed assemblies are rejected even with matching hashes.");
            Check(File.ReadAllBytes(target).Length==3,"Rejected input retains the existing file.");
            Console.WriteLine("PASS "+checks+" shared updater bootstrap and preloader checks"); return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}
