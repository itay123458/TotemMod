using System;
using System.IO;
using Mono.Cecil;
using TotemMod.Updating;

internal static class LiveUpdateProbe
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length!=3) throw new ArgumentException("Usage: LiveUpdateProbe InstalledDll GameAssembly EmptyTestDirectory");
            string root=Path.GetFullPath(args[2]);
            if (!root.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase) || Directory.Exists(root))
                throw new InvalidOperationException("Live verification requires a new temporary directory.");
            Version installed=null;
            using (AssemblyDefinition assembly=AssemblyDefinition.ReadAssembly(args[0]))
                foreach (TypeDefinition type in assembly.MainModule.Types)
                    foreach (CustomAttribute a in type.CustomAttributes)
                        if (a.AttributeType.FullName=="BepInEx.BepInPlugin" && (string)a.ConstructorArguments[0].Value==UpdateTrust.PluginGuid)
                            installed=new Version((string)a.ConstructorArguments[2].Value);
            if (installed==null) throw new InvalidOperationException("Installed DLL is not Totem Mod.");
            string hash;
            using (System.Security.Cryptography.SHA256 sha=System.Security.Cryptography.SHA256.Create())
            using (FileStream input=File.OpenRead(args[1])) hash=BitConverter.ToString(sha.ComputeHash(input)).Replace("-","");
            UpdateDownload update=new UpdateClient().Fetch(installed,hash);
            if (update==null) { Console.WriteLine("Live feed verified: installed version is already current."); return 0; }
            string bepin=Path.Combine(root,"BepInEx"),plugins=Path.Combine(bepin,"plugins");
            Directory.CreateDirectory(plugins);
            string target=Path.Combine(plugins,"TotemMod.dll"),game=Path.Combine(root,"Assembly-CSharp.dll");
            File.Copy(args[0],target); File.Copy(args[1],game);
            UpdateInstaller.Stage(bepin,update);
            if (UpdateInstaller.PendingVersion(bepin)!=update.Manifest.Version) throw new InvalidOperationException("Pending version mismatch.");
            string result=UpdateInstaller.ApplyPending(bepin,target,game);
            UpdateManifest.VerifyPayload(update.Manifest,File.ReadAllBytes(target));
            Console.WriteLine("Live GitHub download/signature/stage/next-launch apply verified: "+update.Manifest.Version);
            Console.WriteLine(result); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}
