using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using TotemMod.Updating;

internal static class UpdateClientTests
{
    private static int checks;
    private static string privateKey;
    private static readonly byte[] payload=Encoding.UTF8.GetBytes("offline DLL fixture; never loaded");
    private static void Check(bool value,string message) { checks++; if (!value) throw new Exception(message); }
    private static void Reject(Action action,string message)
    {
        checks++;
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new Exception(message);
    }
    private static UpdateManifest Fixture(int minimumUpdater)
    {
        string hash;
        using (SHA256 sha=SHA256.Create()) { hash=BitConverter.ToString(sha.ComputeHash(payload)).Replace("-",""); }
        UpdateManifest manifest=new UpdateManifest { Format=1,Version="1.2.3",AssetUrl="https://github.com/"+UpdateTrust.Repository+"/releases/download/v1.2.3/TotemMod.dll",Sha256=hash,Size=payload.Length,GameSha256=new string('a',64),MinimumUpdater=minimumUpdater };
        using (RSACryptoServiceProvider rsa=new RSACryptoServiceProvider())
        {
            rsa.PersistKeyInCsp=false;
            rsa.FromXmlString(privateKey);
            manifest.Signature=Convert.ToBase64String(rsa.SignData(manifest.CanonicalBytes(),CryptoConfig.MapNameToOID("SHA256")));
        }
        return manifest;
    }
    public static int Main(string[] args)
    {
        privateKey=File.ReadAllText(args.Length==0 ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".totemmod","update-signing-private.xml") : args[0]);
        int calls=0;
        UpdateManifest manifest=Fixture(1);
        byte[] json=manifest.ToJson();
        UpdateClient client=new UpdateClient(delegate(Uri uri,int maximum)
        {
            calls++;
            if (calls==1)
            {
                Check(uri.AbsoluteUri==UpdateTrust.ManifestUrl && maximum==UpdateTrust.MaximumManifestBytes,"Client must use the fixed manifest URL and bound.");
                return json;
            }
            Check(uri.AbsoluteUri==manifest.AssetUrl && maximum==payload.Length,"Payload must use signed URL and exact size bound.");
            return payload;
        });
        UpdateDownload result=client.Fetch(new Version(1,0,0),new string('A',64));
        Check(calls==2 && result.Manifest.Version=="1.2.3" && result.Payload==payload,"Valid signed update fetched.");
        Version[] installed={new Version(1,2,3),new Version(2,0,0)};
        foreach (Version version in installed)
        {
            calls=0;
            client=new UpdateClient(delegate(Uri uri,int maximum) { calls++; return json; });
            Check(client.Fetch(version,new string('a',64))==null && calls==1,"Equal/older versions must skip payload download.");
        }
        calls=0; client=new UpdateClient(delegate(Uri uri,int maximum) { calls++; return json; });
        Reject(delegate { client.Fetch(new Version(1,0,0),new string('b',64)); },"Game mismatch accepted.");
        Check(calls==1,"Game mismatch must reject before fetching payload.");
        calls=0; json=Fixture(2).ToJson();
        Reject(delegate { client.Fetch(new Version(1,0,0),new string('a',64)); },"Future updater requirement accepted.");
        Check(calls==1,"Updater mismatch must reject before fetching payload.");
        calls=0; manifest=Fixture(1); manifest.Signature=Convert.ToBase64String(new byte[256]); json=manifest.ToJson();
        Reject(delegate { client.Fetch(new Version(1,0,0),new string('a',64)); },"Untrusted signature accepted.");
        Check(calls==1,"Untrusted signature must reject before fetching payload.");
        calls=0; json=new byte[UpdateTrust.MaximumManifestBytes+1];
        Reject(delegate { client.Fetch(new Version(1,0,0),new string('a',64)); },"Oversize manifest returned by transport accepted.");
        Check(calls==1,"Oversize manifest must stop fetch.");
        json=Fixture(1).ToJson(); calls=0;
        client=new UpdateClient(delegate(Uri uri,int maximum) { calls++; return calls==1 ? json : new byte[maximum+1]; });
        Reject(delegate { client.Fetch(new Version(1,0,0),new string('a',64)); },"Oversize payload returned by transport accepted.");
        calls=0; client=new UpdateClient(delegate(Uri uri,int maximum) { calls++; return calls==1 ? json : new byte[payload.Length]; });
        Reject(delegate { client.Fetch(new Version(1,0,0),new string('a',64)); },"Payload with wrong hash accepted.");
        client=new UpdateClient(delegate(Uri uri,int maximum) { throw new WebException("offline test"); });
        bool networkError=false;
        try { client.Fetch(new Version(1,0,0),new string('a',64)); } catch (WebException) { networkError=true; }
        Check(networkError,"Network errors must remain visible to the caller.");
        string[] invalidAddresses={"http://github.com/a","https://evil.example/a","https://github.com.evil.example/a","https://user:pass@github.com/a","https://github.com:444/a","https://github.com/a#fragment","file:///C:/a"};
        foreach (string address in invalidAddresses) Reject(delegate { UpdateClient.ValidateAddress(new Uri(address)); },"Unsafe network destination accepted.");
        UpdateClient.ValidateAddress(new Uri("https://github.com/a"));
        UpdateClient.ValidateAddress(new Uri("https://release-assets.githubusercontent.com/a?token=fixture"));
        UpdateClient.ValidateAddress(new Uri("https://objects.githubusercontent.com/a"));
        Check(true,"Expected HTTPS redirect hosts are allowed.");
        Reject(delegate { UpdateClient.FetchBytes(new Uri("http://github.com/a"),1); },"HTTP request was not rejected before network access.");
        Console.WriteLine("PASS "+checks+" bounded update client/trust/version/compatibility checks");
        return 0;
    }
}
