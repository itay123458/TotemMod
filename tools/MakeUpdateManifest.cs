using System;
using System.IO;
using System.Security.Cryptography;
using Mono.Cecil;
using TotemMod.Updating;

internal static class MakeUpdateManifest
{
    private static string Hash(string path)
    { using (SHA256 sha=SHA256.Create()) using (FileStream input=File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(input)).Replace("-",""); }
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length!=4 && args.Length!=5) throw new ArgumentException("Usage: MakeUpdateManifest DLL GameAssembly SigningKey OutputJson [mace]");
            UpdateProduct product=UpdateProduct.Totem;
            if (args.Length==5)
            {
                if (!string.Equals(args[4],"mace",StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("The optional product must be mace.");
                product=UpdateProduct.Mace;
            }
            string version=null;
            using (AssemblyDefinition assembly=AssemblyDefinition.ReadAssembly(args[0]))
                foreach (TypeDefinition type in assembly.MainModule.Types)
                    foreach (CustomAttribute attribute in type.CustomAttributes)
                        if (attribute.AttributeType.FullName=="BepInEx.BepInPlugin" && attribute.ConstructorArguments.Count==3 && (string)attribute.ConstructorArguments[0].Value==UpdateTrust.ProductGuidFor(product))
                            version=(string)attribute.ConstructorArguments[2].Value;
            if (version==null) throw new InvalidDataException("The release DLL does not match the selected update product.");
            UpdateManifest manifest=new UpdateManifest {
                Product=product,Format=UpdateTrust.Protocol,Version=version,AssetUrl="https://github.com/"+UpdateTrust.Repository+"/releases/download/v"+version+"/"+UpdateTrust.AssetNameFor(product),
                Sha256=Hash(args[0]),Size=new FileInfo(args[0]).Length,GameSha256=Hash(args[1]),MinimumUpdater=UpdateTrust.UpdaterVersion
            };
            using (RSACryptoServiceProvider rsa=new RSACryptoServiceProvider())
            {
                rsa.PersistKeyInCsp=false; rsa.FromXmlString(File.ReadAllText(args[2]));
                manifest.Signature=Convert.ToBase64String(rsa.SignData(manifest.CanonicalBytes(),CryptoConfig.MapNameToOID("SHA256")));
            }
            manifest.Validate();
            File.WriteAllBytes(args[3],manifest.ToJson());
            Console.WriteLine("Signed "+(product==UpdateProduct.Mace ? "Mace Mod" : "Totem Mod")+" "+version+" release manifest."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}
