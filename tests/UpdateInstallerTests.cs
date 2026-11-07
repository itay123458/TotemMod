using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;
using TotemMod.Updating;

internal static class UpdateInstallerTests
{
    private static int checks;
    private static string privateKey;
    private static byte[] original;
    private static byte[] candidate;
    private static byte[] game = Encoding.UTF8.GetBytes("Totem Mod supported game test fixture");

    private sealed class Fixture : IDisposable
    {
        internal readonly string Root;
        internal readonly string Bep;
        internal readonly string Plugin;
        internal readonly string Game;
        internal readonly string Cache;
        internal string Pending { get { return Path.Combine(Cache, "pending"); } }
        internal Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "TotemModUpdaterTests-" + Guid.NewGuid().ToString("N"));
            Bep = Path.Combine(Root, "BepInEx");
            Plugin = Path.Combine(Bep, "plugins", "TotemMod.dll");
            Game = Path.Combine(Root, "Assembly-CSharp.dll");
            Cache = Path.Combine(Bep, "cache", "TotemModUpdater");
            Directory.CreateDirectory(Path.GetDirectoryName(Plugin));
            File.WriteAllBytes(Plugin, original);
            File.WriteAllBytes(Game, game);
        }
        public void Dispose() { DeleteFixture(Root, Root); }
    }

    private static void Check(bool value, string label)
    {
        if (!value) throw new Exception("FAIL " + label);
        checks++;
        Console.WriteLine("PASS " + label);
    }

    private static void Reject(Action action, string label)
    {
        bool rejected = false;
        try { action(); }
        catch (Exception error)
        {
            if (!(error is IOException) && !(error is InvalidDataException) && !(error is UnauthorizedAccessException) && !(error is CryptographicException) && !(error is BadImageFormatException)) throw;
            rejected = true;
        }
        Check(rejected, label);
    }

    private static string Hash(byte[] bytes)
    {
        using (SHA256 sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }

    private static UpdateDownload Download(byte[] payload, string version, string gameHash, int minimum)
    {
        UpdateManifest manifest = new UpdateManifest {
            Format = UpdateTrust.Protocol,
            Version = version,
            AssetUrl = "https://github.com/" + UpdateTrust.Repository + "/releases/download/v" + version + "/TotemMod.dll",
            Sha256 = Hash(payload), Size = payload.LongLength, GameSha256 = gameHash,
            MinimumUpdater = minimum
        };
        using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider())
        {
            rsa.PersistKeyInCsp = false;
            rsa.FromXmlString(privateKey);
            manifest.Signature = Convert.ToBase64String(rsa.SignData(manifest.CanonicalBytes(), CryptoConfig.MapNameToOID("SHA256")));
        }
        manifest.Validate();
        return new UpdateDownload(manifest, payload);
    }

    private static UpdateDownload Good()
    {
        return Download(candidate, "2.0.0", Hash(game), UpdateTrust.UpdaterVersion);
    }

    private static byte[] Rewrite(byte[] dll, string version, string guid)
    {
        using (MemoryStream input = new MemoryStream(dll, false))
        using (AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(input))
        using (MemoryStream output = new MemoryStream())
        {
            CustomAttribute attribute = assembly.MainModule.Types.SelectMany(t => t.CustomAttributes)
                .Single(a => a.AttributeType.FullName == "BepInEx.BepInPlugin");
            attribute.ConstructorArguments[0] = new CustomAttributeArgument(assembly.MainModule.TypeSystem.String, guid);
            attribute.ConstructorArguments[2] = new CustomAttributeArgument(assembly.MainModule.TypeSystem.String, version);
            assembly.Write(output);
            return output.ToArray();
        }
    }

    private static bool Unchanged(Fixture fixture)
    {
        return File.ReadAllBytes(fixture.Plugin).SequenceEqual(original);
    }

    private static void Apply(Fixture fixture)
    {
        UpdateInstaller.ApplyPending(fixture.Bep, fixture.Plugin, fixture.Game);
    }

    public static int Main(string[] args)
    {
        if (args.Length != 2) { Console.Error.WriteLine("Usage: UpdateInstallerTests fixture.dll signing-key.xml"); return 2; }
        // The private key remains in the user's profile and never enters an artifact or the repository.
        privateKey = File.ReadAllText(args[1]);
        byte[] fixtureDll = File.ReadAllBytes(args[0]);
        original = Rewrite(fixtureDll, "1.0.0", UpdateTrust.PluginGuid);
        candidate = Rewrite(fixtureDll, "2.0.0", UpdateTrust.PluginGuid);
        try
        {
            using (Fixture f = new Fixture())
            {
                Check(UpdateInstaller.ApplyPending(f.Bep, f.Plugin, f.Game) == null, "no pending update is a no-op");
                Check(UpdateInstaller.PendingVersion(f.Bep) == null && !UpdateInstaller.CancelPending(f.Bep), "empty pending query and cancel are no-ops");
                UpdateInstaller.Stage(f.Bep, Good());
                Check(Unchanged(f), "staging never changes the running plugin");
                Check(UpdateInstaller.PendingVersion(f.Bep) == "2.0.0", "signed pending version is readable");
                Check(new FileInfo(Path.Combine(f.Pending, "ready")).Length == 0, "complete stage publishes the ready marker");
                string status = UpdateInstaller.ApplyPending(f.Bep, f.Plugin, f.Game);
                Check(status.Contains("2.0.0") && File.ReadAllBytes(f.Plugin).SequenceEqual(candidate), "preloader installs the verified candidate");
                string[] backups = Directory.GetFiles(Path.Combine(f.Cache, "backups"));
                Check(backups.Length == 1 && File.ReadAllBytes(backups[0]).SequenceEqual(original), "atomic replacement preserves exact old DLL outside plugins");
                Check(!Directory.Exists(f.Pending), "successful apply removes only owned pending files");
                Check(Directory.GetFiles(Path.GetDirectoryName(f.Plugin)).Length == 1, "plugins contains no duplicate DLL or update temporary file");
            }
            return RunRejections(fixtureDll);
        }
        catch (Exception error) { Console.Error.WriteLine(error.ToString()); return 1; }
        finally { privateKey = null; }
    }

    private static int RunRejections(byte[] fixtureDll)
    {
        using (Fixture f = new Fixture())
        {
            UpdateDownload bad = Good(); bad.Manifest.Signature = Convert.ToBase64String(new byte[256]);
            Reject(delegate { UpdateInstaller.Stage(f.Bep, bad); }, "stage rejects an invalid publisher signature");
            Check(Unchanged(f) && !Directory.Exists(f.Cache), "invalid signature writes nothing");
            byte[] tampered = (byte[])candidate.Clone(); tampered[tampered.Length - 1] ^= 1;
            Reject(delegate { UpdateInstaller.Stage(f.Bep, new UpdateDownload(Good().Manifest, tampered)); }, "stage rechecks payload hash");
            Reject(delegate { UpdateInstaller.Stage(f.Bep, Download(candidate, "3.0.0", Hash(game), 1)); }, "stage requires DLL version to match signed manifest");
            Reject(delegate { UpdateInstaller.Stage(f.Bep, Download(Rewrite(fixtureDll, "2.0.0", "other.plugin"), "2.0.0", Hash(game), 1)); }, "stage rejects a different BepInPlugin GUID");
            Reject(delegate { UpdateInstaller.Stage(f.Bep, Download(Encoding.UTF8.GetBytes("not a DLL"), "2.0.0", Hash(game), 1)); }, "stage rejects a signed non-assembly payload");
            Reject(delegate { UpdateInstaller.Stage(f.Bep, Download(candidate, "2.0.0", Hash(game), UpdateTrust.UpdaterVersion + 1)); }, "stage rejects a newer required updater protocol");
        }
        using (Fixture f = new Fixture())
        {
            UpdateInstaller.Stage(f.Bep, Good());
            byte[] bytes = File.ReadAllBytes(Path.Combine(f.Pending, "TotemMod.dll")); bytes[bytes.Length - 1] ^= 1;
            File.WriteAllBytes(Path.Combine(f.Pending, "TotemMod.dll"), bytes);
            Reject(delegate { Apply(f); }, "apply rechecks pending DLL hash");
            Check(Unchanged(f), "payload tampering preserves installed plugin");
        }
        using (Fixture f = new Fixture())
        {
            UpdateInstaller.Stage(f.Bep, Good());
            File.AppendAllText(Path.Combine(f.Pending, "totem-update.json"), "tampering");
            Reject(delegate { Apply(f); }, "apply rejects tampered manifest");
            Check(Unchanged(f), "manifest tampering preserves installed plugin");
        }
        using (Fixture f = new Fixture())
        {
            UpdateInstaller.Stage(f.Bep, Good());
            File.WriteAllText(f.Game, "different game build");
            Reject(delegate { Apply(f); }, "apply rejects a game build hash mismatch");
            Check(Unchanged(f), "game mismatch preserves installed plugin");
            File.Delete(f.Game);
            Reject(delegate { Apply(f); }, "apply rejects a missing game assembly");
        }
        using (Fixture f = new Fixture())
        {
            UpdateInstaller.Stage(f.Bep, Download(original, "1.0.0", Hash(game), 1));
            Reject(delegate { Apply(f); }, "apply rejects the same version");
            UpdateInstaller.Stage(f.Bep, Download(Rewrite(fixtureDll, "0.9.0", UpdateTrust.PluginGuid), "0.9.0", Hash(game), 1));
            Reject(delegate { Apply(f); }, "apply rejects a downgrade");
            Check(Unchanged(f), "downgrade preserves installed plugin");
        }
        using (Fixture f = new Fixture())
        {
            UpdateInstaller.Stage(f.Bep, Good());
            File.WriteAllBytes(f.Plugin, Rewrite(fixtureDll, "1.0.0", "another.plugin"));
            Reject(delegate { Apply(f); }, "apply refuses to replace an unrelated installed plugin");
            File.Delete(f.Plugin);
            Reject(delegate { Apply(f); }, "apply rejects a missing installed plugin");
        }
        using (Fixture f = new Fixture())
        {
            UpdateInstaller.Stage(f.Bep, Good());
            Reject(delegate { UpdateInstaller.ApplyPending(f.Bep, Path.Combine(f.Root, "TotemMod.dll"), f.Game); }, "apply rejects a target outside the plugin directory");
            File.Delete(Path.Combine(f.Pending, "ready"));
            Reject(delegate { Apply(f); }, "apply rejects an incomplete stage");
            Check(UpdateInstaller.CancelPending(f.Bep) && Unchanged(f), "cancel safely clears an incomplete owned stage");
        }
        using (Fixture f = new Fixture())
        {
            UpdateInstaller.Stage(f.Bep, Good());
            string unexpected = Path.Combine(f.Pending, "unrelated.txt"); File.WriteAllText(unexpected, "keep");
            Reject(delegate { Apply(f); }, "apply rejects unexpected pending files");
            Reject(delegate { UpdateInstaller.CancelPending(f.Bep); }, "cancel fails closed on unexpected pending files");
            Reject(delegate { UpdateInstaller.Stage(f.Bep, Good()); }, "restaging fails closed on unexpected pending files");
            Check(File.ReadAllText(unexpected) == "keep" && File.Exists(Path.Combine(f.Pending, "TotemMod.dll")) && Unchanged(f), "safe cleanup preserves unrelated content and old stage");
            File.Delete(unexpected);
            Directory.CreateDirectory(Path.Combine(f.Pending, "unrelated-directory"));
            Reject(delegate { UpdateInstaller.CancelPending(f.Bep); }, "cancel never recurses into unexpected directories");
        }
        using (Fixture f = new Fixture())
        {
            UpdateInstaller.Stage(f.Bep, Good());
            using (FileStream hold = new FileStream(Path.Combine(f.Cache, "update.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Reject(delegate { UpdateInstaller.Stage(f.Bep, Good()); }, "exclusive cache lock prevents concurrent stage");
                Reject(delegate { Apply(f); }, "exclusive cache lock prevents concurrent apply");
                Reject(delegate { UpdateInstaller.CancelPending(f.Bep); }, "exclusive cache lock prevents concurrent cancel");
            }
            UpdateInstaller.Stage(f.Bep, Good());
            Check(UpdateInstaller.PendingVersion(f.Bep) == "2.0.0" && !Directory.GetDirectories(f.Cache).Any(p => Path.GetFileName(p).StartsWith("stage-") || Path.GetFileName(p).StartsWith("previous-")), "restaging atomically replaces pending and clears its own scratch directories");
            Check(UpdateInstaller.CancelPending(f.Bep) && !Directory.Exists(f.Pending) && Unchanged(f), "cancel preserves current plugin");
        }
        using (Fixture f = new Fixture())
        {
            UpdateInstaller.Stage(f.Bep, Good());
            using (FileStream hold = new FileStream(f.Plugin, FileMode.Open, FileAccess.Read, FileShare.None))
                Reject(delegate { Apply(f); }, "a locked installed DLL fails safely");
            Check(Unchanged(f) && Directory.Exists(f.Pending), "failed application retains old DLL and retryable pending update");
        }
        using (Fixture f = new Fixture())
        {
            Directory.CreateDirectory(f.Cache);
            string outside = Path.Combine(f.Root, "unrelated-owner"); Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "keep.txt"), "keep");
            CreateJunction(f.Pending, outside);
            try
            {
                Reject(delegate { UpdateInstaller.Stage(f.Bep, Good()); }, "stage rejects a pending directory junction");
                Reject(delegate { Apply(f); }, "apply rejects a pending directory junction");
                Reject(delegate { UpdateInstaller.CancelPending(f.Bep); }, "cancel rejects a pending directory junction");
                Check(File.ReadAllText(Path.Combine(outside, "keep.txt")) == "keep" && Unchanged(f), "reparse rejection preserves external files and installed DLL");
            }
            finally { Directory.Delete(f.Pending, false); }
        }
        using (Fixture f = new Fixture())
        {
            UpdateInstaller.Stage(f.Bep, Good());
            string plugins = Path.GetDirectoryName(f.Plugin);
            string outside = Path.Combine(f.Root, "other-plugins");
            Directory.Move(plugins, outside);
            CreateJunction(plugins, outside);
            try
            {
                Reject(delegate { Apply(f); }, "apply rejects a plugins directory junction");
                Check(File.ReadAllBytes(Path.Combine(outside, "TotemMod.dll")).SequenceEqual(original), "plugin junction rejection preserves original DLL");
            }
            finally { Directory.Delete(plugins, false); Directory.Move(outside, plugins); }
        }
        Console.WriteLine("Verified " + checks + " installer checks.");
        return 0;
    }

    private static void CreateJunction(string link, string target)
    {
        ProcessStartInfo start = new ProcessStartInfo("cmd.exe", "/d /c mklink /J \"" + link + "\" \"" + target + "\"");
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.WindowStyle = ProcessWindowStyle.Hidden;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        using (Process process = Process.Start(start))
        {
            string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0) throw new IOException("Could not create test junction: " + output);
        }
    }

    private static void DeleteFixture(string root, string path)
    {
        string full = Path.GetFullPath(path);
        string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        if (full != prefix && !full.StartsWith(prefix + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Test cleanup escaped its fixture root.");
        if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Test cleanup will not traverse reparse points.");
        foreach (string file in Directory.GetFiles(full)) File.Delete(file);
        foreach (string directory in Directory.GetDirectories(full)) DeleteFixture(prefix, directory);
        Directory.Delete(full, false);
    }
}
