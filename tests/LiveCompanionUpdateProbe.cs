using System;
using System.IO;
using System.Security.Cryptography;
using Mono.Cecil;
using TotemMod.Updating;

internal static class LiveCompanionUpdateProbe
{
    private static Version VersionOf(string path,string guid)
    {
        using(AssemblyDefinition assembly=AssemblyDefinition.ReadAssembly(path))
            foreach(TypeDefinition type in assembly.MainModule.Types)
            foreach(CustomAttribute attribute in type.CustomAttributes)
                if(attribute.AttributeType.FullName=="BepInEx.BepInPlugin" && attribute.ConstructorArguments.Count==3 && (string)attribute.ConstructorArguments[0].Value==guid)
                    return new Version((string)attribute.ConstructorArguments[2].Value);
        throw new InvalidDataException("Unexpected source plugin identity.");
    }
    private static byte[] Download(Uri address,int limit)
    {
        Console.WriteLine("Fetching "+address.Host+address.AbsolutePath);
        return UpdateClient.FetchBytes(address,limit);
    }
    public static int Main(string[] args)
    {
        try
        {
            if(args.Length!=4) throw new ArgumentException("Usage: LiveCompanionUpdateProbe InstalledTotem InstalledMace GameAssembly NewTempDirectory");
            string root=Path.GetFullPath(args[3]);
            string temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!root.StartsWith(temp,StringComparison.OrdinalIgnoreCase) || Directory.Exists(root)) throw new InvalidOperationException("A new temporary probe directory is required.");
            string gameHash;
            using(SHA256 sha=SHA256.Create()) using(FileStream source=File.OpenRead(args[2])) gameHash=BitConverter.ToString(sha.ComputeHash(source)).Replace("-","");
            UpdateDownload totem=new UpdateClient(Download).Fetch(VersionOf(args[0],UpdateTrust.PluginGuid),gameHash);
            UpdateDownload mace=new UpdateClient(Download,UpdateProduct.Mace).Fetch(VersionOf(args[1],UpdateTrust.MaceGuid),gameHash);
            if(totem==null || mace==null) throw new InvalidOperationException("Use previous-version DLLs to exercise both real update downloads.");
            string bepin=Path.Combine(root,"BepInEx"),plugins=Path.Combine(bepin,"plugins");
            Directory.CreateDirectory(plugins);
            string totemTarget=Path.Combine(plugins,"TotemMod.dll"),maceTarget=Path.Combine(plugins,"MaceMod.dll"),game=Path.Combine(root,"Assembly-CSharp.dll");
            File.Copy(args[0],totemTarget); File.Copy(args[1],maceTarget); File.Copy(args[2],game);
            UpdateInstaller.Stage(bepin,totem); MaceUpdateInstaller.Stage(bepin,mace);
            if(UpdateInstaller.PendingVersion(bepin)!=totem.Manifest.Version || MaceUpdateInstaller.PendingVersion(bepin)!=mace.Manifest.Version) throw new InvalidDataException("Pending product versions disagree.");
            Console.WriteLine(UpdateInstaller.ApplyPending(bepin,totemTarget,game));
            Console.WriteLine(MaceUpdateInstaller.ApplyPending(bepin,maceTarget,game));
            UpdateManifest.VerifyPayload(totem.Manifest,File.ReadAllBytes(totemTarget));
            UpdateManifest.VerifyPayload(mace.Manifest,File.ReadAllBytes(maceTarget));
            if(VersionOf(totemTarget,UpdateTrust.PluginGuid)!=totem.Manifest.ParsedVersion || VersionOf(maceTarget,UpdateTrust.MaceGuid)!=mace.Manifest.ParsedVersion) throw new InvalidDataException("Installed metadata does not match signed releases.");
            Console.WriteLine("Live GitHub signature/download/stage/restart-install verified for BOTH mods at "+totem.Manifest.Version+" / "+mace.Manifest.Version); return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}
