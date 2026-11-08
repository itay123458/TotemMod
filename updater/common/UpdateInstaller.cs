using System;
using System.IO;
using System.Security.Cryptography;
using Mono.Cecil;

namespace TotemMod.Updating
{
    // Shared by the running plugin (staging) and the preloader (installation).
    // Nothing in this class loads the candidate assembly into the CLR.
    internal static class UpdateInstaller
    {
        private static readonly UpdateInstallerCore Core = new UpdateInstallerCore(UpdateProduct.Totem);
        internal static void Stage(string bepinRoot, UpdateDownload download) { Core.Stage(bepinRoot, download); }
        internal static string PendingVersion(string bepinRoot) { return Core.PendingVersion(bepinRoot); }
        internal static UpdateManifest PendingManifest(string bepinRoot) { return Core.PendingManifest(bepinRoot); }
        internal static bool CancelPending(string bepinRoot) { return Core.CancelPending(bepinRoot); }
        internal static string ApplyPending(string bepinRoot, string pluginPath, string gameAssemblyPath) { return Core.ApplyPending(bepinRoot, pluginPath, gameAssemblyPath); }
    }

    internal static class MaceUpdateInstaller
    {
        private static readonly UpdateInstallerCore Core = new UpdateInstallerCore(UpdateProduct.Mace);
        internal static void Stage(string bepinRoot, UpdateDownload download) { Core.Stage(bepinRoot, download); }
        internal static string PendingVersion(string bepinRoot) { return Core.PendingVersion(bepinRoot); }
        internal static UpdateManifest PendingManifest(string bepinRoot) { return Core.PendingManifest(bepinRoot); }
        internal static bool CancelPending(string bepinRoot) { return Core.CancelPending(bepinRoot); }
        internal static string ApplyPending(string bepinRoot, string pluginPath, string gameAssemblyPath) { return Core.ApplyPending(bepinRoot, pluginPath, gameAssemblyPath); }
        internal static Version InstalledVersion(string fullPath) { return Core.InstalledVersion(fullPath); }
    }

    // Product identity is fixed at construction. The two public surfaces share
    // safety rules without sharing cache locks, pending state, or mutable options.
    internal sealed class UpdateInstallerCore
    {
        private readonly UpdateProduct product;
        private readonly string DllName, ManifestName, CacheName, PluginGuid, DisplayName, Stem;
        private const string ReadyName = "ready";

        internal UpdateInstallerCore(UpdateProduct product)
        {
            this.product = product;
            DllName = UpdateTrust.AssetNameFor(product);
            PluginGuid = UpdateTrust.ProductGuidFor(product);
            bool mace = product == UpdateProduct.Mace;
            ManifestName = mace ? "mace-update.json" : "totem-update.json";
            CacheName = mace ? "MaceModUpdater" : "TotemModUpdater";
            DisplayName = mace ? "Mace Mod" : "Totem Mod";
            Stem = Path.GetFileNameWithoutExtension(DllName);
        }

        internal void Stage(string bepinRoot, UpdateDownload download)
        {
            if (download == null) throw new ArgumentNullException("download");
            if (download.Manifest == null || download.Manifest.Product != product)
                throw new InvalidDataException("The download belongs to a different updater product.");
            download = new UpdateDownload(UpdateManifest.Read(download.Manifest.ToJson(), product), (byte[])download.Payload.Clone());
            UpdateManifest.VerifyPayload(download.Manifest, download.Payload);
            RequireSupportedUpdater(download.Manifest);
            RequireVersion(download.Payload, download.Manifest.ParsedVersion);
            string cache = Cache(bepinRoot, true);
            using (Acquire(cache))
            {
                string pending = Child(cache, "pending");
                if (Directory.Exists(pending)) CheckPending(pending, false);
                else RejectFile(pending);
                string stage = Child(cache, "stage-" + Guid.NewGuid().ToString("N"));
                string previous = Child(cache, "previous-" + Guid.NewGuid().ToString("N"));
                EnsureDirectory(stage);
                bool movedPrevious = false;
                try
                {
                    WriteNew(Child(stage, DllName), download.Payload);
                    WriteNew(Child(stage, ManifestName), download.Manifest.ToJson());
                    // A complete candidate becomes visible only after all files are flushed.
                    WriteNew(Child(stage, ReadyName), new byte[0]);
                    if (Directory.Exists(pending))
                    {
                        Directory.Move(pending, previous);
                        movedPrevious = true;
                    }
                    try { Directory.Move(stage, pending); }
                    catch
                    {
                        if (movedPrevious) Directory.Move(previous, pending);
                        movedPrevious = false;
                        throw;
                    }
                    if (movedPrevious) RemovePending(previous);
                }
                finally
                {
                    if (Directory.Exists(stage)) RemovePending(stage);
                }
            }
        }

        internal string PendingVersion(string bepinRoot)
        {
            UpdateManifest manifest=PendingManifest(bepinRoot);
            return manifest==null ? null : manifest.Version;
        }

        internal UpdateManifest PendingManifest(string bepinRoot)
        {
            string cache = Cache(bepinRoot, false);
            if (!Directory.Exists(cache)) return null;
            using (Acquire(cache))
            {
                string pending = Child(cache, "pending");
                if (!Directory.Exists(pending)) { RejectFile(pending); return null; }
                return ReadPending(pending).Manifest;
            }
        }

        internal bool CancelPending(string bepinRoot)
        {
            string cache = Cache(bepinRoot, false);
            if (!Directory.Exists(cache)) return false;
            using (Acquire(cache))
            {
                string pending = Child(cache, "pending");
                if (!Directory.Exists(pending)) { RejectFile(pending); return false; }
                RemovePending(pending);
                return true;
            }
        }

        // Only the preloader calls this, before Chainloader discovers any plugins.
        // Errors propagate to its logging boundary; the existing plugin stays in place.
        internal string ApplyPending(string bepinRoot, string pluginPath, string gameAssemblyPath)
        {
            string cache = Cache(bepinRoot, false);
            if (!Directory.Exists(cache)) return null;
            using (Acquire(cache))
            {
                string pending = Child(cache, "pending");
                if (!Directory.Exists(pending)) { RejectFile(pending); return null; }
                UpdateDownload download = ReadPending(pending);
                string pluginDirectory = Path.Combine(Path.GetFullPath(bepinRoot), "plugins");
                EnsureSafePath(pluginDirectory);
                string target = Child(pluginDirectory, DllName);
                if (!String.Equals(Path.GetFullPath(pluginPath), target, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The update target must be BepInEx/plugins/" + DllName + ".");
                EnsureSafePath(target);
                EnsureSafePath(gameAssemblyPath);
                if (!File.Exists(target)) throw new InvalidDataException("The installed " + DisplayName + " is missing.");
                if (!File.Exists(gameAssemblyPath)) throw new InvalidDataException("The game assembly is missing.");
                if (!String.Equals(HashFile(gameAssemblyPath), download.Manifest.GameSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("This update does not support the installed game build.");
                string backups = Child(cache, "backups");
                EnsureDirectory(backups);
                string temporary = Child(pluginDirectory, Stem + ".update-" + Guid.NewGuid().ToString("N") + ".tmp");
                // Deny writes to the inspected target until the atomic replacement completes.
                using (FileStream installed = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                {
                    Version installedVersion = ReadPluginVersion(ReadBounded(installed, UpdateTrust.MaximumDllBytes));
                    if (!download.Manifest.IsNewerThan(installedVersion))
                        throw new InvalidDataException("The pending update is not newer than the installed " + DisplayName + ".");
                    string backup = Child(backups, Stem + "-" + installedVersion.ToString(3) + "-" + Guid.NewGuid().ToString("N") + ".bak");
                    try
                    {
                        WriteNew(temporary, download.Payload);
                        EnsureSafePath(target);
                        EnsureSafePath(backup);
                        // Same-volume atomic replacement. No delete/move fallback can leave a missing plugin.
                        File.Replace(temporary, target, backup);
                    }
                    finally
                    {
                        if (File.Exists(temporary)) { EnsureSafePath(temporary); File.Delete(temporary); }
                    }
                }
                try { RemovePending(pending); }
                catch (Exception error)
                {
                    return "Installed " + DisplayName + " " + download.Manifest.Version + "; pending cleanup failed: " + error.Message;
                }
                return "Installed " + DisplayName + " " + download.Manifest.Version + ". Previous version saved in " + backups;
            }
        }

        private UpdateDownload ReadPending(string pending)
        {
            CheckPending(pending, true);
            UpdateManifest manifest = UpdateManifest.Read(ReadBoundedFile(Child(pending, ManifestName), UpdateTrust.MaximumManifestBytes), product);
            byte[] payload = ReadBoundedFile(Child(pending, DllName), UpdateTrust.MaximumDllBytes);
            UpdateManifest.VerifyPayload(manifest, payload);
            RequireSupportedUpdater(manifest);
            RequireVersion(payload, manifest.ParsedVersion);
            return new UpdateDownload(manifest, payload);
        }

        private void RequireVersion(byte[] payload, Version expected)
        {
            if (ReadPluginVersion(payload) != expected)
                throw new InvalidDataException("The DLL plugin version does not match the signed update manifest.");
        }

        private static void RequireSupportedUpdater(UpdateManifest manifest)
        {
            if (manifest.MinimumUpdater > UpdateTrust.UpdaterVersion)
                throw new InvalidDataException("The update requires a newer Totem Mod updater helper.");
        }

        internal Version InstalledVersion(string fullPath)
        {
            if (String.IsNullOrEmpty(fullPath) || !Path.IsPathRooted(fullPath))
                throw new InvalidDataException("An installed plugin baseline requires an absolute path.");
            return ReadPluginVersion(ReadBoundedFile(Path.GetFullPath(fullPath), UpdateTrust.MaximumDllBytes));
        }

        private Version ReadPluginVersion(byte[] payload)
        {
            using (MemoryStream stream = new MemoryStream(payload, false))
            using (AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(stream))
            {
                Version found = null;
                foreach (TypeDefinition type in assembly.MainModule.Types)
                {
                    foreach (CustomAttribute attribute in type.CustomAttributes)
                    {
                        if (attribute.AttributeType.FullName != "BepInEx.BepInPlugin") continue;
                        if (attribute.ConstructorArguments.Count != 3 ||
                            (string)attribute.ConstructorArguments[0].Value != PluginGuid || found != null)
                            throw new InvalidDataException("The DLL does not identify only the expected " + DisplayName + " plugin.");
                        string version = attribute.ConstructorArguments[2].Value as string;
                        Version parsed;
                        if (!Version.TryParse(version, out parsed) || parsed.Build < 0 || parsed.Revision >= 0 || parsed.ToString(3) != version)
                            throw new InvalidDataException("The DLL plugin version must have three numeric components.");
                        found = parsed;
                    }
                }
                if (found == null) throw new InvalidDataException("The DLL is not the expected " + DisplayName + " plugin.");
                return found;
            }
        }

        private string Cache(string bepinRoot, bool create)
        {
            string root = Path.GetFullPath(bepinRoot);
            EnsureSafePath(root);
            string cacheDirectory = Child(root, "cache");
            string cache = Child(cacheDirectory, CacheName);
            if (create) { EnsureDirectory(cacheDirectory); EnsureDirectory(cache); }
            else { EnsureSafePath(cacheDirectory); EnsureSafePath(cache); RejectFile(cache); }
            return cache;
        }

        private static FileStream Acquire(string cache)
        {
            string path = Child(cache, "update.lock");
            EnsureSafePath(path);
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }

        private static string Child(string directory, string name)
        {
            string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string path = Path.GetFullPath(Path.Combine(root, name));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Update path escaped its owned directory.");
            EnsureSafePath(path);
            return path;
        }

        private static void EnsureSafePath(string path)
        {
            string current = Path.GetFullPath(path);
            while (!String.IsNullOrEmpty(current))
            {
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("Update paths cannot contain symbolic links or reparse points.");
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                string parent = Path.GetDirectoryName(current);
                if (parent == current) break;
                current = parent;
            }
        }

        private static void EnsureDirectory(string path)
        {
            EnsureSafePath(path);
            RejectFile(path);
            Directory.CreateDirectory(path);
            EnsureSafePath(path);
        }

        private static void RejectFile(string path)
        {
            if (File.Exists(path)) throw new InvalidDataException("A file occupies an expected update directory.");
        }

        private void CheckPending(string pending, bool requireReady)
        {
            EnsureSafePath(pending);
            foreach (string path in Directory.GetFileSystemEntries(pending))
            {
                EnsureSafePath(path);
                string name = Path.GetFileName(path);
                if (Directory.Exists(path) || (name != DllName && name != ManifestName && name != ReadyName))
                    throw new InvalidDataException("The pending update contains unexpected files; nothing was removed.");
            }
            if (requireReady && (!File.Exists(Child(pending, ReadyName)) || new FileInfo(Child(pending, ReadyName)).Length != 0))
                throw new InvalidDataException("The pending update is incomplete.");
        }

        private void RemovePending(string pending)
        {
            CheckPending(pending, false);
            foreach (string name in new[] { ReadyName, ManifestName, DllName })
            {
                string path = Child(pending, name);
                if (File.Exists(path)) File.Delete(path);
            }
            Directory.Delete(pending, false);
        }

        private static void WriteNew(string path, byte[] bytes)
        {
            EnsureSafePath(path);
            using (FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        private static byte[] ReadBoundedFile(string path, int maximum)
        {
            EnsureSafePath(path);
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                return ReadBounded(stream, maximum);
        }

        private static byte[] ReadBounded(Stream stream, int maximum)
        {
            if (stream.Length <= 0 || stream.Length > maximum) throw new InvalidDataException("An update file has an invalid size.");
            byte[] bytes = new byte[(int)stream.Length];
            int offset = 0;
            while (offset < bytes.Length)
            {
                int count = stream.Read(bytes, offset, bytes.Length - offset);
                if (count == 0) throw new EndOfStreamException();
                offset += count;
            }
            return bytes;
        }

        private static string HashFile(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (SHA256 hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
