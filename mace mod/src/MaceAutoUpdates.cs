using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using TotemMod.Updating;
using UnityEngine;

namespace MaceMod
{
    public sealed partial class MacePlugin
    {
        internal const string MaceVersion="1.9.6";
        private const float MaceUpdateInterval=6f*60f*60f;
        private static readonly Version MaceUpdaterVersion=new Version(MaceVersion);
        private static readonly Version TotemMaceManagerVersion=new Version(1,9,2);
        private ConfigEntry<bool> maceAutomaticUpdates;
        private Task<MaceUpdateResult> maceUpdateTask;
        private volatile bool maceUpdateCancelled;
        private bool maceUpdaterDisposed,maceTaskAutomatic,maceUpdatesManaged,maceWasAutomatic,maceUpdaterReady;
        private float maceUpdateCheckAt;
        private string maceUpdateStatus="Not checked",macePendingUpdate;
        private string maceUpdateRoot,maceGameAssembly,maceInstalledAssembly,maceUpdaterHelper;
        private string maceUpdaterUnavailable="Updater helper unavailable; reinstall the release ZIP";

        private sealed class MaceUpdateResult
        {
            internal string Status,Version;
            internal bool ClearPending,Failed;
        }

        private void InitializeMaceUpdates()
        {
            maceAutomaticUpdates=Config.Bind("Updates","Automatic",true,"Check signed Mace releases on startup and every six hours. Verified updates apply on the next game launch. PrimalCraft 1.9.2 or newer manages both products when installed.");
            maceWasAutomatic=maceAutomaticUpdates.Value;
            maceUpdateRoot=Paths.BepInExRootPath;
            maceGameAssembly=Path.Combine(Paths.ManagedPath,"Assembly-CSharp.dll");
            maceInstalledAssembly=Info.Location;
            maceUpdaterHelper=Path.Combine(Paths.PatcherPluginPath,"TotemMod.CompanionUpdater.dll");
            bool flatInstall=false;
            try
            {
                string target=Path.GetFullPath(Path.Combine(maceUpdateRoot,"plugins","MaceMod.dll"));
                flatInstall=string.Equals(Path.GetFullPath(maceInstalledAssembly),target,StringComparison.OrdinalIgnoreCase);
            }
            catch(Exception error) { Logger.LogWarning("Mace update install path: "+error.Message); }
            if(!flatInstall) maceUpdaterUnavailable="Automatic updates require BepInEx/plugins/MaceMod.dll";
            try
            {
                string bootstrapStatus=CompanionBootstrap.EnsureInstalled(maceUpdateRoot);
                if(!string.IsNullOrEmpty(bootstrapStatus)) Logger.LogInfo(bootstrapStatus);
                maceUpdaterReady=flatInstall && File.Exists(maceUpdaterHelper) && (bootstrapStatus==null || bootstrapStatus=="Companion updater ready; restart Gorilla Tag");
            }
            catch(Exception error) { Logger.LogWarning("Mace updater setup: "+error.Message); }
            try
            {
                macePendingUpdate=MaceUpdateInstaller.PendingVersion(maceUpdateRoot);
                if(macePendingUpdate!=null && new Version(macePendingUpdate).CompareTo(MaceUpdaterVersion)<=0)
                { MaceUpdateInstaller.CancelPending(maceUpdateRoot); macePendingUpdate=null; }
            }
            catch(Exception error)
            {
                maceUpdateStatus="Pending update invalid";
                Logger.LogWarning("Pending Mace update was ignored: "+error.Message);
            }
            maceUpdateCheckAt=Time.unscaledTime+10f;
            maceUpdatesManaged=MaceManagedByTotem();
            if(maceUpdatesManaged) maceUpdateStatus="Managed by PrimalCraft";
            else if(macePendingUpdate!=null) maceUpdateStatus="Restart to install "+macePendingUpdate;
            else if(!maceUpdaterReady) maceUpdateStatus=maceUpdaterUnavailable;
        }

        private static bool MaceManagedByTotem()
        {
            PluginInfo plugin;
            return Chainloader.PluginInfos.TryGetValue("totemmod.gtag.rhys",out plugin) && plugin!=null &&
                plugin.Metadata!=null && plugin.Metadata.Version!=null && plugin.Metadata.Version.CompareTo(TotemMaceManagerVersion)>=0 &&
                plugin.Instance!=null && plugin.Instance.isActiveAndEnabled;
        }

        private void CheckMaceUpdates(bool automatic=false)
        {
            if(maceUpdaterDisposed || maceAutomaticUpdates==null) return;
            maceUpdatesManaged=MaceManagedByTotem();
            if(maceUpdatesManaged)
            {
                maceUpdateCancelled=true;
                maceUpdateStatus="Managed by PrimalCraft";
                return;
            }
            if(maceUpdateTask!=null) return;
            maceUpdateCheckAt=Time.unscaledTime+MaceUpdateInterval;
            if(!maceUpdaterReady || !File.Exists(maceUpdaterHelper))
            {
                maceUpdateStatus=maceUpdaterUnavailable;
                maceUpdateCheckAt=Time.unscaledTime+300f;
                return;
            }
            maceUpdateCancelled=false;
            maceTaskAutomatic=automatic;
            maceUpdateStatus="Checking GitHub...";
            // All Unity/BepInEx state is captured here on the main thread. The
            // worker performs only BCL hashing, signed downloads and staging.
            string root=maceUpdateRoot,game=maceGameAssembly,installed=maceInstalledAssembly;
            Version current=MaceUpdaterVersion;
            maceUpdateTask=Task.Run(() => {
                bool clearPending=false;
                try
                {
                    if(maceUpdateCancelled) return new MaceUpdateResult {Status="Update check canceled"};
                    string gameHash;
                    using(SHA256 sha=SHA256.Create())
                    using(FileStream input=File.OpenRead(game)) gameHash=BitConverter.ToString(sha.ComputeHash(input)).Replace("-","");
                    UpdateManifest pending=MaceUpdateInstaller.PendingManifest(root);
                    if(pending!=null && !pending.SupportedGame(gameHash))
                    { MaceUpdateInstaller.CancelPending(root); pending=null; clearPending=true; }
                    Version baseline=current;
                    Version disk=MaceUpdateInstaller.InstalledVersion(installed);
                    if(disk!=null && disk.CompareTo(baseline)>0) baseline=disk;
                    if(pending!=null && pending.ParsedVersion.CompareTo(baseline)>0) baseline=pending.ParsedVersion;
                    if(maceUpdateCancelled) return new MaceUpdateResult {Status="Update check canceled",ClearPending=clearPending};
                    UpdateDownload download=new UpdateClient(UpdateProduct.Mace).Fetch(baseline,gameHash);
                    if(maceUpdateCancelled) return new MaceUpdateResult {Status="Update check canceled",ClearPending=clearPending};
                    if(download==null) return new MaceUpdateResult {
                        Status=pending==null?"Up to date":"Ready: restart for "+pending.Version,
                        Version=pending==null?null:pending.Version,ClearPending=clearPending};
                    MaceUpdateInstaller.Stage(root,download);
                    return new MaceUpdateResult {Status="Ready: restart for "+download.Manifest.Version,Version=download.Manifest.Version,ClearPending=clearPending};
                }
                catch(Exception error) { return new MaceUpdateResult {Status="Check failed: "+error.Message,ClearPending=clearPending,Failed=true}; }
            });
        }

        private void TickMaceUpdates()
        {
            if(maceUpdaterDisposed || maceAutomaticUpdates==null) return;
            bool managed=MaceManagedByTotem();
            if(managed!=maceUpdatesManaged)
            {
                maceUpdatesManaged=managed;
                maceUpdateCheckAt=Time.unscaledTime+10f;
                maceUpdateStatus=managed?"Managed by PrimalCraft":macePendingUpdate!=null?"Restart to install "+macePendingUpdate:!maceUpdaterReady?maceUpdaterUnavailable:"Not checked";
            }
            if(managed || (maceTaskAutomatic && !maceAutomaticUpdates.Value)) maceUpdateCancelled=true;
            if(maceUpdateTask!=null && maceUpdateTask.IsCompleted)
            {
                MaceUpdateResult result;
                try { result=maceUpdateTask.Result; }
                catch(Exception) { result=new MaceUpdateResult {Status="Update check failed; installed version retained",Failed=true}; }
                maceUpdateTask=null;
                if(result.ClearPending) macePendingUpdate=null;
                if(result.Version!=null) macePendingUpdate=result.Version;
                if(!managed)
                {
                    maceUpdateStatus=result.Status;
                    if(result.Failed) maceUpdateCheckAt=Time.unscaledTime+300f;
                    Logger.LogInfo("Mace updater: "+maceUpdateStatus);
                }
            }
            bool automatic=maceAutomaticUpdates.Value;
            if(automatic && !maceWasAutomatic) maceUpdateCheckAt=Time.unscaledTime+10f;
            maceWasAutomatic=automatic;
            if(managed) { maceUpdateStatus="Managed by PrimalCraft"; return; }
            if(automatic && maceUpdateTask==null && Time.unscaledTime>=maceUpdateCheckAt) CheckMaceUpdates(true);
        }

        private void DisposeMaceUpdates()
        {
            maceUpdaterDisposed=true;
            maceUpdateCancelled=true;
        }

        private void DrawMaceUpdates()
        {
            GUILayout.Space(8f);
            if(maceUpdatesManaged)
            {
                GUILayout.Label("Updates managed by PrimalCraft");
                GUILayout.Label("Use PrimalCraft Settings for automatic updates and checks.");
                return;
            }
            if(maceAutomaticUpdates==null) return;
            maceAutomaticUpdates.Value=GUILayout.Toggle(maceAutomaticUpdates.Value,"Automatic Mace updates");
            GUILayout.Label("Updates: "+maceUpdateStatus);
            bool enabled=GUI.enabled;
            GUI.enabled=enabled && maceUpdateTask==null;
            if(GUILayout.Button(maceUpdateTask==null?"Check for updates now":"Checking...")) CheckMaceUpdates();
            GUI.enabled=enabled;
        }
    }
}
