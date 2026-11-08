using System;
using System.IO;
using System.Security.Cryptography;
using Mono.Cecil;
using TotemMod.Updating;

internal static class StageLocalRelease
{
    private static string Hash(string path)
    { using(SHA256 sha=SHA256.Create()) using(FileStream input=File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(input)).Replace("-",""); }
    private static Version VersionOf(string path,string guid)
    {
        using(AssemblyDefinition assembly=AssemblyDefinition.ReadAssembly(path))
            foreach(TypeDefinition type in assembly.MainModule.Types)
            foreach(CustomAttribute attribute in type.CustomAttributes)
                if(attribute.AttributeType.FullName=="BepInEx.BepInPlugin" && attribute.ConstructorArguments.Count==3 && (string)attribute.ConstructorArguments[0].Value==guid)
                    return new Version((string)attribute.ConstructorArguments[2].Value);
        throw new InvalidDataException("Unexpected installed plugin identity.");
    }
    public static int Main(string[] args)
    {
        try
        {
            if(args.Length!=4) throw new ArgumentException("Usage: StageLocalRelease BepInExRoot GameAssembly ReleaseArtifacts MaceCandidateDll");
            string root=Path.GetFullPath(args[0]),gameHash=Hash(args[1]);
            string totem=Path.Combine(root,"plugins","TotemMod.dll"),mace=Path.Combine(root,"plugins","MaceMod.dll");
            string totemBefore=Hash(totem),maceBefore=File.Exists(mace)?Hash(mace):null;
            UpdateManifest totemManifest=UpdateManifest.Read(File.ReadAllBytes(Path.Combine(args[2],"totem-update.json")));
            UpdateManifest maceManifest=UpdateManifest.Read(File.ReadAllBytes(Path.Combine(args[2],"mace-update.json")),UpdateProduct.Mace);
            if(!totemManifest.SupportedGame(gameHash) || !maceManifest.SupportedGame(gameHash)) throw new InvalidDataException("Release does not match this game build.");
            if(totemManifest.IsNewerThan(VersionOf(totem,UpdateTrust.PluginGuid)))
            {
                UpdateInstaller.Stage(root,new UpdateDownload(totemManifest,File.ReadAllBytes(Path.Combine(args[2],"TotemMod.dll"))));
                Console.WriteLine("Totem "+UpdateInstaller.PendingVersion(root)+" queued for next launch.");
            }
            if(maceBefore!=null && maceManifest.IsNewerThan(VersionOf(mace,UpdateTrust.MaceGuid)))
            {
                MaceUpdateInstaller.Stage(root,new UpdateDownload(maceManifest,File.ReadAllBytes(args[3])));
                Console.WriteLine("Mace "+MaceUpdateInstaller.PendingVersion(root)+" queued for next launch.");
            }
            if(Hash(totem)!=totemBefore || maceBefore!=null && Hash(mace)!=maceBefore) throw new IOException("Installed DLL changed during staging.");
            Console.WriteLine("Running DLLs retained unchanged. Restart Gorilla Tag to apply queued updates."); return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}
