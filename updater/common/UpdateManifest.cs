using System;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace TotemMod.Updating
{
    [DataContract]
    internal sealed class UpdateManifest
    {
        // Selected by trusted local code, never accepted from the JSON envelope.
        internal UpdateProduct Product=UpdateProduct.Totem;
        [DataMember(IsRequired=true, Order=0)] public int Format=0;
        [DataMember(IsRequired=true, Order=1)] public string Version=null;
        [DataMember(IsRequired=true, Order=2)] public string AssetUrl=null;
        [DataMember(IsRequired=true, Order=3)] public string Sha256=null;
        [DataMember(IsRequired=true, Order=4)] public long Size=0;
        [DataMember(IsRequired=true, Order=5)] public string GameSha256=null;
        [DataMember(IsRequired=true, Order=6)] public int MinimumUpdater=0;
        [DataMember(IsRequired=true, Order=7)] public string Signature=null;

        internal System.Version ParsedVersion
        {
            get
            {
                if (Version == null || !Regex.IsMatch(Version, @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z", RegexOptions.CultureInvariant))
                    throw new InvalidDataException("The update version must contain three decimal components.");
                System.Version parsed;
                if (!System.Version.TryParse(Version, out parsed))
                    throw new InvalidDataException("The update version is out of range.");
                return parsed;
            }
        }

        internal static UpdateManifest Read(byte[] json, UpdateProduct product=UpdateProduct.Totem)
        {
            if (product!=UpdateProduct.Totem && product!=UpdateProduct.Mace) throw new InvalidDataException("Unknown update product.");
            if (json == null || json.Length == 0 || json.Length > UpdateTrust.MaximumManifestBytes)
                throw new InvalidDataException("The update manifest exceeds its permitted size or is empty.");
            try
            {
                // Reject invalid UTF-8 rather than silently replacing bytes before parsing.
                ValidateJsonEnvelope(new UTF8Encoding(false, true).GetString(json));
                XmlDictionaryReaderQuotas quotas = new XmlDictionaryReaderQuotas();
                quotas.MaxDepth = 8;
                quotas.MaxStringContentLength = UpdateTrust.MaximumManifestBytes;
                ValidateSchema(json, quotas);
                using (XmlDictionaryReader reader = JsonReaderWriterFactory.CreateJsonReader(json, quotas))
                {
                    UpdateManifest manifest = (UpdateManifest)new DataContractJsonSerializer(typeof(UpdateManifest)).ReadObject(reader);
                    if (reader.MoveToContent() != XmlNodeType.None)
                        throw new InvalidDataException("Unexpected content follows the update manifest.");
                    if (manifest == null) throw new InvalidDataException("The update manifest is null.");
                    manifest.Product=product;
                    manifest.Validate();
                    return manifest;
                }
            }
            catch (InvalidDataException) { throw; }
            catch (Exception ex)
            {
                if (!(ex is SerializationException) && !(ex is XmlException) && !(ex is DecoderFallbackException) && !(ex is ArgumentException) && !(ex is CryptographicException) && !(ex is FormatException)) throw;
                throw new InvalidDataException("The update manifest is malformed or its signature is invalid.", ex);
            }
        }

        private static void ValidateJsonEnvelope(string json)
        {
            // Framework's JSON reader can stop after the first root object and
            // ignore trailing bytes. Require exactly one complete object.
            int start=0;
            while (start<json.Length && IsJsonSpace(json[start])) start++;
            if (start==json.Length || json[start]!='{') throw new InvalidDataException("The manifest must be a JSON object.");
            int depth=0;
            bool quoted=false, escaped=false;
            for (int i=start;i<json.Length;i++)
            {
                char ch=json[i];
                if (quoted)
                {
                    if (escaped) escaped=false;
                    else if (ch=='\\') escaped=true;
                    else if (ch=='\"') quoted=false;
                    continue;
                }
                if (ch=='\"') quoted=true;
                else if (ch=='{' || ch=='[') depth++;
                else if (ch=='}' || ch==']')
                {
                    depth--;
                    if (depth==0)
                    {
                        for (int j=i+1;j<json.Length;j++)
                            if (!IsJsonSpace(json[j])) throw new InvalidDataException("Unexpected content follows the update manifest.");
                        return;
                    }
                }
            }
            throw new InvalidDataException("The update manifest is incomplete.");
        }

        private static bool IsJsonSpace(char ch) { return ch==' ' || ch=='\t' || ch=='\r' || ch=='\n'; }

        private static void ValidateSchema(byte[] json, XmlDictionaryReaderQuotas quotas)
        {
            string[] names={"Format","Version","AssetUrl","Sha256","Size","GameSha256","MinimumUpdater","Signature"};
            bool[] seen=new bool[names.Length];
            using (XmlDictionaryReader reader=JsonReaderWriterFactory.CreateJsonReader(json,quotas))
            {
                if (reader.MoveToContent()!=XmlNodeType.Element || reader.GetAttribute("type")!="object")
                    throw new InvalidDataException("The manifest must be a JSON object.");
                while (reader.Read())
                {
                    if (reader.NodeType!=XmlNodeType.Element) continue;
                    if (reader.Depth!=1) throw new InvalidDataException("Manifest fields must be scalar values.");
                    int index=Array.IndexOf(names,reader.LocalName);
                    if (index<0 || seen[index]) throw new InvalidDataException("The manifest contains an unknown or duplicate field.");
                    seen[index]=true;
                    string expected=(index==0 || index==4 || index==6) ? "number" : "string";
                    if (reader.GetAttribute("type")!=expected) throw new InvalidDataException("A manifest field has the wrong JSON type.");
                }
            }
            for (int i=0;i<seen.Length;i++)
                if (!seen[i]) throw new InvalidDataException("A required manifest field is missing.");
        }

        internal byte[] ToJson()
        {
            using (MemoryStream stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(UpdateManifest)).WriteObject(stream, this);
                if (stream.Length > UpdateTrust.MaximumManifestBytes) throw new InvalidDataException("The update manifest exceeds its permitted size.");
                return stream.ToArray();
            }
        }

        internal byte[] CanonicalBytes()
        {
            ValidateShape();
            string domain=Product==UpdateProduct.Mace ? "MaceMod update v1\n" : "TotemMod update v1\n";
            return Encoding.UTF8.GetBytes(domain + Version + "\n" + AssetUrl + "\n" + Sha256.ToUpperInvariant() + "\n" + Size.ToString(CultureInfo.InvariantCulture) + "\n" + GameSha256.ToUpperInvariant() + "\n" + MinimumUpdater.ToString(CultureInfo.InvariantCulture) + "\n");
        }

        internal void Validate()
        {
            byte[] canonical = CanonicalBytes();
            byte[] signature;
            try { signature = Convert.FromBase64String(Signature ?? ""); }
            catch (FormatException ex) { throw new InvalidDataException("The update signature is not valid Base64.", ex); }
            if (signature.Length != 256 || Convert.ToBase64String(signature) != Signature)
                throw new InvalidDataException("The update signature has an invalid encoding or length.");
            using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider())
            {
                rsa.PersistKeyInCsp = false;
                rsa.FromXmlString(UpdateTrust.PublicKey);
                if (!rsa.VerifyData(canonical, CryptoConfig.MapNameToOID("SHA256"), signature))
                    throw new InvalidDataException("The update signature does not match the trusted publisher.");
            }
        }

        private void ValidateShape()
        {
            if (Product!=UpdateProduct.Totem && Product!=UpdateProduct.Mace) throw new InvalidDataException("Unknown update product.");
            if (Format != UpdateTrust.Protocol) throw new InvalidDataException("Unsupported update manifest format.");
            System.Version parsed = ParsedVersion;
            string expected = "https://github.com/" + UpdateTrust.Repository + "/releases/download/v" + parsed.ToString(3) + "/" + UpdateTrust.AssetNameFor(Product);
            if (!String.Equals(AssetUrl, expected, StringComparison.Ordinal))
                throw new InvalidDataException("The update asset is outside the trusted release location.");
            if (!IsHash(Sha256) || !IsHash(GameSha256)) throw new InvalidDataException("An update hash is invalid.");
            if (Size < 1 || Size > UpdateTrust.MaximumDllBytes) throw new InvalidDataException("The update DLL size is outside the permitted range.");
            if (MinimumUpdater < 1) throw new InvalidDataException("The minimum updater version is invalid.");
        }

        internal bool IsNewerThan(System.Version current)
        {
            if (current == null) throw new ArgumentNullException("current");
            return ParsedVersion.CompareTo(current) > 0;
        }

        internal bool SupportedGame(string hash)
        {
            return IsHash(hash) && String.Equals(GameSha256, hash, StringComparison.OrdinalIgnoreCase);
        }

        internal static void VerifyPayload(UpdateManifest manifest, byte[] bytes)
        {
            if (manifest == null) throw new ArgumentNullException("manifest");
            manifest.Validate();
            if (bytes == null || bytes.LongLength != manifest.Size || bytes.Length > UpdateTrust.MaximumDllBytes)
                throw new InvalidDataException("The downloaded DLL has the wrong size.");
            string actual;
            using (SHA256 sha = SHA256.Create()) { actual = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""); }
            if (!String.Equals(actual, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The downloaded DLL failed its SHA-256 check.");
        }

        private static bool IsHash(string value)
        {
            return value != null && Regex.IsMatch(value, @"\A[0-9a-fA-F]{64}\z", RegexOptions.CultureInvariant);
        }
    }

    internal sealed class UpdateDownload
    {
        internal readonly UpdateManifest Manifest;
        internal readonly UpdateProduct Product;
        internal readonly byte[] Payload;
        internal UpdateDownload(UpdateManifest manifest, byte[] payload)
        {
            if (manifest == null) throw new ArgumentNullException("manifest");
            if (payload == null) throw new ArgumentNullException("payload");
            Manifest = manifest;
            Product = manifest.Product;
            Payload = payload;
        }
    }
}
