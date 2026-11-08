using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using TotemMod.Updating;

internal static class UpdateManifestTests
{
    private static int checks;
    private static string privateKey;
    private static readonly byte[] payload = Encoding.UTF8.GetBytes("offline DLL fixture; never loaded");
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static void Reject(Action action, string message)
    {
        checks++;
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new Exception(message);
    }
    private static UpdateManifest Fixture(UpdateProduct product=UpdateProduct.Totem)
    {
        UpdateManifest manifest = new UpdateManifest {
            Product=product, Format=1, Version="1.2.3", AssetUrl="https://github.com/"+UpdateTrust.Repository+"/releases/download/v1.2.3/"+(product==UpdateProduct.Mace ? "MaceMod.dll" : "TotemMod.dll"),
            Sha256=Hash(payload), Size=payload.Length, GameSha256=new string('a',64), MinimumUpdater=1
        };
        Sign(manifest);
        return manifest;
    }
    private static string Hash(byte[] bytes)
    {
        using (SHA256 sha=SHA256.Create()) { return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-",""); }
    }
    private static void Sign(UpdateManifest manifest)
    {
        using (RSACryptoServiceProvider rsa=new RSACryptoServiceProvider())
        {
            rsa.PersistKeyInCsp=false;
            rsa.FromXmlString(privateKey);
            manifest.Signature=Convert.ToBase64String(rsa.SignData(manifest.CanonicalBytes(),CryptoConfig.MapNameToOID("SHA256")));
        }
    }
    public static int Main(string[] args)
    {
        privateKey=File.ReadAllText(args.Length==0 ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".totemmod","update-signing-private.xml") : args[0]);
        UpdateManifest manifest=Fixture();
        string canonical="TotemMod update v1\n1.2.3\n"+manifest.AssetUrl+"\n"+manifest.Sha256+"\n"+payload.Length+"\n"+new string('A',64)+"\n1\n";
        Check(Encoding.UTF8.GetString(manifest.CanonicalBytes())==canonical,"Canonical signing bytes must be exact.");
        UpdateManifest loaded=UpdateManifest.Read(manifest.ToJson());
        Check(loaded.ParsedVersion==new Version(1,2,3),"Signed manifest round trip.");
        Check(loaded.IsNewerThan(new Version(1,2,2)) && !loaded.IsNewerThan(new Version(1,2,3)) && !loaded.IsNewerThan(new Version(2,0,0)),"Equal and older releases cannot update.");
        Check(loaded.SupportedGame(new string('A',64)) && !loaded.SupportedGame(new string('b',64)) && !loaded.SupportedGame(null),"Game hash comparison validates shape and ignores hex case.");
        UpdateManifest.VerifyPayload(loaded,payload); Check(true,"Payload verified.");
        Reject(delegate { UpdateManifest.VerifyPayload(loaded,new byte[payload.Length]); },"Bad payload hash accepted.");
        Reject(delegate { UpdateManifest.VerifyPayload(loaded,new byte[1]); },"Bad payload size accepted.");
        manifest=Fixture(); manifest.Version="1.2.4"; manifest.AssetUrl=manifest.AssetUrl.Replace("1.2.3","1.2.4");
        Reject(delegate { manifest.Validate(); },"Tampered signed fields accepted.");
        manifest=Fixture(); manifest.Signature=new string('A',344);
        Reject(delegate { manifest.Validate(); },"Invalid signature accepted.");
        manifest=Fixture(); manifest.Signature=" "+manifest.Signature;
        Reject(delegate { manifest.Validate(); },"Whitespace signature accepted.");
        string[] badVersions={"1.2","1.2.3.4","01.2.3","1.2.3\n","-1.2.3","2147483648.2.3","1.2.3-beta"};
        foreach (string version in badVersions)
        {
            manifest=Fixture(); manifest.Version=version;
            Reject(delegate { manifest.Validate(); },"Invalid version accepted: "+version);
        }
        string[] badUrls={"http://github.com/"+UpdateTrust.Repository+"/releases/download/v1.2.3/TotemMod.dll","https://example.com/TotemMod.dll","https://github.com/other/repo/releases/download/v1.2.3/TotemMod.dll",Fixture().AssetUrl+"?x=1"};
        foreach (string url in badUrls)
        {
            manifest=Fixture(); manifest.AssetUrl=url;
            Reject(delegate { manifest.Validate(); },"Untrusted asset URL accepted.");
        }
        manifest=Fixture(); manifest.Format=2; Reject(delegate { manifest.Validate(); },"Unknown format accepted.");
        manifest=Fixture(); manifest.Size=0; Reject(delegate { manifest.Validate(); },"Empty DLL accepted.");
        manifest=Fixture(); manifest.Size=UpdateTrust.MaximumDllBytes+1L; Reject(delegate { manifest.Validate(); },"Oversize DLL accepted.");
        manifest=Fixture(); manifest.Sha256=new string('z',64); Reject(delegate { manifest.Validate(); },"Invalid hash accepted.");
        manifest=Fixture(); manifest.GameSha256=new string('a',63); Reject(delegate { manifest.Validate(); },"Invalid game hash accepted.");
        manifest=Fixture(); manifest.MinimumUpdater=0; Reject(delegate { manifest.Validate(); },"Invalid updater version accepted.");
        Reject(delegate { UpdateManifest.Read(new byte[UpdateTrust.MaximumManifestBytes+1]); },"Oversize manifest accepted.");
        string validJson=Encoding.UTF8.GetString(Fixture().ToJson());
        string[] invalidJson={"", "null", "{}", "{", "[]", validJson+"{}", validJson.Replace("\"Format\":1", "\"Format\":1,\"Format\":1"),validJson.Replace("\"Format\":1","\"Format\":\"1\""),validJson.Replace("\"Format\":1","\"Unknown\":0,\"Format\":1"),validJson.Replace("\"Format\":1","\"Format\":{}")};
        for (int i=0;i<invalidJson.Length;i++)
        {
            string json=invalidJson[i];
            Reject(delegate { UpdateManifest.Read(Encoding.UTF8.GetBytes(json)); },"Invalid JSON/schema accepted at fixture "+i+".");
        }
        Reject(delegate { UpdateManifest.Read(new byte[]{255,254,253}); },"Invalid UTF-8 accepted.");
        UpdateManifest mace=Fixture(UpdateProduct.Mace);
        UpdateManifest maceLoaded=UpdateManifest.Read(mace.ToJson(),UpdateProduct.Mace);
        Check(maceLoaded.Product==UpdateProduct.Mace && maceLoaded.ParsedVersion==new Version(1,2,3),"Mace product signed manifest round trip.");
        Check(Encoding.UTF8.GetString(mace.CanonicalBytes())==canonical.Replace("TotemMod update v1\n","MaceMod update v1\n").Replace("/TotemMod.dll","/MaceMod.dll"),"Mace canonical domain and asset must be exact.");
        UpdateManifest.VerifyPayload(maceLoaded,payload); Check(true,"Mace payload verifies with selected product.");
        Reject(delegate { UpdateManifest.Read(mace.ToJson()); },"Default Totem reader accepted Mace manifest.");
        Reject(delegate { UpdateManifest.Read(Fixture().ToJson(),UpdateProduct.Mace); },"Mace reader accepted Totem manifest.");
        UpdateManifest swapped=Fixture(UpdateProduct.Mace); swapped.Signature=Fixture().Signature;
        Reject(delegate { swapped.Validate(); },"Totem signature accepted on Mace manifest.");
        swapped=Fixture(); swapped.Signature=mace.Signature;
        Reject(delegate { swapped.Validate(); },"Mace signature accepted on Totem manifest.");
        // Keep the Mace asset URL and fields unchanged, but sign under Totem's
        // domain: product isolation must hold independently of URL validation.
        swapped=Fixture(UpdateProduct.Mace);
        using (RSACryptoServiceProvider rsa=new RSACryptoServiceProvider())
        {
            rsa.PersistKeyInCsp=false; rsa.FromXmlString(privateKey);
            byte[] wrongDomain=Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(swapped.CanonicalBytes()).Replace("MaceMod update v1\n","TotemMod update v1\n"));
            swapped.Signature=Convert.ToBase64String(rsa.SignData(wrongDomain,CryptoConfig.MapNameToOID("SHA256")));
        }
        Reject(delegate { swapped.Validate(); },"Mace accepted publisher signature for wrong product domain.");
        swapped=Fixture(UpdateProduct.Mace); swapped.AssetUrl=swapped.AssetUrl.Replace("MaceMod.dll","TotemMod.dll");
        Reject(delegate { swapped.Validate(); },"Mace accepted Totem asset URL.");
        swapped=Fixture(); swapped.AssetUrl=swapped.AssetUrl.Replace("TotemMod.dll","MaceMod.dll");
        Reject(delegate { swapped.Validate(); },"Totem accepted Mace asset URL.");
        string maceJson=Encoding.UTF8.GetString(mace.ToJson());
        Reject(delegate { UpdateManifest.Read(Encoding.UTF8.GetBytes(maceJson.Replace("\"Format\":1","\"Product\":1,\"Format\":1")),UpdateProduct.Mace); },"Serialized product discriminator bypassed strict eight-field schema.");
        Reject(delegate { UpdateManifest.Read(mace.ToJson(),(UpdateProduct)99); },"Unknown locally selected product accepted.");
        swapped=Fixture(UpdateProduct.Mace); swapped.Product=(UpdateProduct)99;
        Reject(delegate { swapped.Validate(); },"Unknown manifest product accepted.");
        Check(UpdateTrust.ProductGuidFor(UpdateProduct.Totem)=="totemmod.gtag.rhys" && UpdateTrust.ProductGuidFor(UpdateProduct.Mace)=="macemod.gtag.rhys","Installer product GUIDs are distinct.");
        Console.WriteLine("PASS "+checks+" update manifest/signature/hash/schema checks");
        return 0;
    }
}
