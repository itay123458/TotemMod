using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using TotemMod;

internal static class DeveloperIdentityTests
{
    private static int checks;
    private static void Check(bool value,string label) { checks++; if (!value) throw new Exception(label); }
    public static int Main()
    {
        string path = Path.Combine(Path.GetTempPath(),"totem-test-"+Guid.NewGuid().ToString("N")+".xml");
        try
        {
            using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider(2048))
            {
                rsa.PersistKeyInCsp=false; File.WriteAllText(path,rsa.ToXmlString(true));
                byte[] data=Encoding.UTF8.GetBytes("private-room/host/actor/session/nonce/start-scrim");
                Check(DeveloperIdentity.Sign(data,path,false)==null,"A nondeveloper cannot sign even with a key file.");
                byte[] signature=DeveloperIdentity.Sign(data,path,true);
                Check(signature!=null && signature.Length==256,"A developer can sign with a local private key.");
                Check(DeveloperIdentity.VerifyWithKey(data,signature,rsa.ToXmlString(false)),"Matching proof is valid.");
                Check(!DeveloperIdentity.Verify(data,signature),"A self-generated key cannot authorize the shipped developer identity.");
                data[0]^=1; Check(!DeveloperIdentity.VerifyWithKey(data,signature,rsa.ToXmlString(false)),"Changing room, actor or command invalidates proof.");
                data[0]^=1; signature[0]^=1;
                Check(!DeveloperIdentity.VerifyWithKey(data,signature,rsa.ToXmlString(false)),"Tampered signature is rejected.");
                Check(DeveloperIdentity.Sign(data,path+"missing",true)==null,"Missing key fails closed.");
                Check(DeveloperIdentity.Sign(new byte[4097],path,true)==null,"Oversized signing payload rejected.");
                Check(!DeveloperIdentity.VerifyWithKey(data,new byte[1],rsa.ToXmlString(false)),"Malformed signature rejected.");
                Check(!DeveloperIdentity.VerifyWithKey(null,signature,rsa.ToXmlString(false)),"Missing payload rejected.");
                Check(!DeveloperIdentity.VerifyWithKey(data,signature,"invalid"),"Malformed key rejected.");
                string ownerPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".totemmod","developer-private.xml");
                if (File.Exists(ownerPath))
                {
                    byte[] ownerProof=DeveloperIdentity.Sign(data,ownerPath,true);
                    Check(DeveloperIdentity.Verify(data,ownerProof),"Local developer key matches the shipped public identity.");
                }
            }
        }
        finally { if (File.Exists(path)) File.Delete(path); }
        Console.WriteLine("PASS "+checks+" developer signature and tamper checks"); return 0;
    }
}
