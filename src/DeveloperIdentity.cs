using System;
using System.IO;
using System.Security.Cryptography;

namespace TotemMod
{
    internal static class DeveloperIdentity
    {
        private const string PublicKey = "<RSAKeyValue><Modulus>zEraz+gwl0YhI8/DXrGF6oB5FaBvBPU0ANSWiQK2oQhARB88nh+4kxGGJBuJP2Qn3JExCGnr9XRwa4GJtJaidGPSV6XghYbP4AyHgvtP0o1wIO9HFTV4YAmrpDTwQJatSA30RARnJ9W7b8M6k/yr73bealn3D+WUWhslHsprGCZpgOhbvNwz/+fKPxQMKsCxYStgg2IF5dB9RZk7oGxADU/nAqiEfzbL9BFfmsTmZXBigtkTRbyaVIpv0D1leqTlXulj5j9ZFE7bhiP+Yn4c9fn9H2zNjUzPBCixPq1N+ETS9VdG55H0V6yewMOXrU4QjvdBUFffVX97GGKh3GipQQ==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

        internal static byte[] Sign(byte[] data,string keyPath,bool developer)
        {
            if (!developer || data == null || data.Length == 0 || data.Length > 4096) return null;
            try
            {
                using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider())
                {
                    rsa.PersistKeyInCsp = false;
                    rsa.FromXmlString(File.ReadAllText(keyPath));
                    return rsa.SignData(data,"SHA256");
                }
            }
            catch (Exception) { return null; }
        }

        internal static bool Verify(byte[] data,byte[] signature) { return VerifyWithKey(data,signature,PublicKey); }
        internal static bool VerifyWithKey(byte[] data,byte[] signature,string publicKey)
        {
            if (data == null || data.Length == 0 || data.Length > 4096 || signature == null || signature.Length != 256) return false;
            try
            {
                using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider())
                {
                    rsa.PersistKeyInCsp = false;
                    rsa.FromXmlString(publicKey);
                    return rsa.VerifyData(data,"SHA256",signature);
                }
            }
            catch (Exception) { return false; }
        }
    }
}
