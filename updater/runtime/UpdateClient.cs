using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;

namespace TotemMod.Updating
{
    internal sealed class UpdateClient
    {
        private readonly Func<Uri, int, byte[]> fetch;

        internal UpdateClient() : this(FetchBytes) { }

        internal UpdateClient(Func<Uri, int, byte[]> fetchBytes)
        {
            if (fetchBytes == null) throw new ArgumentNullException("fetchBytes");
            fetch = fetchBytes;
        }

        internal UpdateDownload Fetch(Version installed, string gameHash)
        {
            if (installed == null) throw new ArgumentNullException("installed");
            UpdateManifest manifest = UpdateManifest.Read(fetch(new Uri(UpdateTrust.ManifestUrl), UpdateTrust.MaximumManifestBytes));
            if (manifest.MinimumUpdater > UpdateTrust.UpdaterVersion)
                throw new InvalidDataException("This release requires a newer TotemMod updater.");
            if (!manifest.SupportedGame(gameHash))
                throw new InvalidDataException("This release targets a different game build.");
            if (!manifest.IsNewerThan(installed)) return null;
            byte[] payload = fetch(new Uri(manifest.AssetUrl), checked((int)manifest.Size));
            VerifyPayload(manifest, payload);
            return new UpdateDownload(manifest, payload);
        }

        internal static void VerifyPayload(UpdateManifest manifest, byte[] payload)
        {
            UpdateManifest.VerifyPayload(manifest, payload);
        }

        internal static byte[] FetchBytes(Uri address, int maximumBytes)
        {
            if (maximumBytes < 1 || maximumBytes > UpdateTrust.MaximumDllBytes)
                throw new ArgumentOutOfRangeException("maximumBytes");
            ValidateAddress(address);
            // Preserve OS defaults; older Framework/Mono defaults need TLS 1.2 enabled.
            SecurityProtocolType protocols = ServicePointManager.SecurityProtocol;
            if (protocols != 0)
                ServicePointManager.SecurityProtocol = (protocols & ~(SecurityProtocolType)48) | (SecurityProtocolType)3072;
            Uri current = address;
            int totalTimeout=maximumBytes>UpdateTrust.MaximumManifestBytes ? 60000 : 15000;
            Stopwatch elapsed=Stopwatch.StartNew();
            for (int redirects = 0; redirects <= 5; redirects++)
            {
                ValidateAddress(current);
                int remaining=totalTimeout-(int)Math.Min(elapsed.ElapsedMilliseconds,(long)totalTimeout);
                if (remaining<=0) throw new WebException("The update download timed out.",WebExceptionStatus.Timeout);
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(current);
                request.Method = "GET";
                request.AllowAutoRedirect = false;
                // Update checks are infrequent; release each redirect connection.
                request.KeepAlive = false;
                request.Timeout = remaining;
                request.ReadWriteTimeout = remaining;
                request.AutomaticDecompression = DecompressionMethods.None;
                request.UseDefaultCredentials = false;
                request.Credentials = null;
                request.Proxy = null;
                request.UserAgent = "TotemMod-Updater/1";
#if UPDATER_TRACE
                Console.WriteLine("GET "+current.DnsSafeHost+current.AbsolutePath);
#endif
                using (Timer deadline=new Timer(delegate(object ignored) { request.Abort(); },null,remaining,Timeout.Infinite))
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    int status = (int)response.StatusCode;
#if UPDATER_TRACE
                    Console.WriteLine("HTTP "+status+" length "+response.ContentLength);
#endif
                    if (status == 301 || status == 302 || status == 303 || status == 307 || status == 308)
                    {
                        string location = response.Headers[HttpResponseHeader.Location];
                        Uri next;
                        if (redirects == 5 || String.IsNullOrEmpty(location) || !Uri.TryCreate(current, location, out next))
                            throw new InvalidDataException("The update download has too many or invalid redirects.");
                        ValidateAddress(next);
                        current = next;
                        continue;
                    }
                    if (response.StatusCode != HttpStatusCode.OK)
                        throw new InvalidDataException("The update server did not return a complete download.");
                    if (response.ContentLength > maximumBytes)
                        throw new InvalidDataException("The update download exceeds its permitted size.");
                    using (Stream source = response.GetResponseStream())
                    using (MemoryStream output = new MemoryStream())
                    {
                        if (source == null) throw new InvalidDataException("The update response has no content.");
                        byte[] buffer = new byte[8192];
                        int count;
                        while ((count = source.Read(buffer, 0, Math.Min(buffer.Length, maximumBytes - checked((int)output.Length) + 1))) != 0)
                        {
                            if (output.Length + count > maximumBytes)
                                throw new InvalidDataException("The update download exceeds its permitted size.");
                            output.Write(buffer, 0, count);
#if UPDATER_TRACE
                            if (output.Length==count || output.Length==response.ContentLength) Console.WriteLine("Read "+output.Length+" bytes");
#endif
                        }
                        if (response.ContentLength >= 0 && output.Length != response.ContentLength)
                            throw new InvalidDataException("The update download ended before its declared length.");
                        return output.ToArray();
                    }
                }
            }
            throw new InvalidDataException("The update redirect limit was exceeded.");
        }

        internal static void ValidateAddress(Uri address)
        {
            if (address == null || !address.IsAbsoluteUri || address.Scheme != Uri.UriSchemeHttps || address.Port != 443 || address.UserInfo.Length != 0 || address.Fragment.Length != 0)
                throw new InvalidDataException("Update downloads require HTTPS on its standard port.");
            string host = address.DnsSafeHost;
            if (!String.Equals(host, "github.com", StringComparison.OrdinalIgnoreCase) &&
                !String.Equals(host, "release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase) &&
                !String.Equals(host, "objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The update download redirected outside trusted GitHub hosts.");
        }
    }
}
