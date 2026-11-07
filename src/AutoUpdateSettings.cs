using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using TotemMod.Updating;
using UnityEngine;

namespace TotemMod
{
    public sealed partial class TotemPlugin
    {
        internal const string ModVersion="1.7.1";
        private ConfigEntry<bool> automaticUpdates;
        private ConfigEntry<float> updateIntervalMinutes;
        private Task<UpdateResult> updateTask;
        private volatile bool updateCancelled;
        private bool updateTaskAutomatic,updaterDisposed,updateToast;
        private float updateCheckAt;
        private string updateStatus="Not checked",pendingUpdate;
        private string updaterRoot,updaterGameAssembly,updaterHelper;

        private sealed class UpdateResult
        {
            internal string Status,Version;
            internal bool ClearPending;
        }

        private void InitializeAutoUpdater()
        {
            automaticUpdates=Config.Bind("Updates","Automatic",true,"Check signed GitHub releases on startup and at the configured interval. Verified updates apply on the next game launch.");
            updateIntervalMinutes=SliderSetting("Updates","CheckIntervalMinutes",360,15,720,"Minutes between background update checks.");
            updaterRoot=Paths.BepInExRootPath;
            updaterGameAssembly=Path.Combine(Paths.ManagedPath,"Assembly-CSharp.dll");
            updaterHelper=Path.Combine(Paths.PatcherPluginPath,"TotemMod.Updater.dll");
            try
            {
                pendingUpdate=UpdateInstaller.PendingVersion(updaterRoot);
                if (pendingUpdate!=null && new Version(pendingUpdate).CompareTo(new Version(ModVersion))<=0)
                { UpdateInstaller.CancelPending(updaterRoot); pendingUpdate=null; }
            }
            catch (Exception error) { updateStatus="Pending update invalid"; Log.LogWarning("Pending Totem update was ignored: "+error.Message); }
            updateCheckAt=Time.unscaledTime+10;
            if (pendingUpdate!=null) updateStatus="Restart to install "+pendingUpdate;
        }

        private bool UpdateBusy() { return updateTask!=null; }

        private void CheckForUpdates(bool automatic=false)
        {
            if (updaterDisposed || updateTask!=null) return;
            updateCheckAt=Time.unscaledTime+SafeFloat(updateIntervalMinutes.Value,360,15,720)*60;
            if (!File.Exists(updaterHelper)) { updateStatus="Updater helper missing; install the release ZIP"; return; }
            updateCancelled=false; updateTaskAutomatic=automatic; updateStatus="Checking GitHub...";
            string root=updaterRoot,game=updaterGameAssembly;
            updateTask=Task.Run(() => {
                bool clearPending=false;
                try
                {
                    string gameHash;
                    using (SHA256 sha=SHA256.Create())
                    using (FileStream input=File.OpenRead(game)) gameHash=BitConverter.ToString(sha.ComputeHash(input)).Replace("-","");
                    UpdateManifest pending=UpdateInstaller.PendingManifest(root);
                    if (pending!=null && !pending.SupportedGame(gameHash))
                    { UpdateInstaller.CancelPending(root); pending=null; clearPending=true; }
                    Version baseline=new Version(ModVersion);
                    if (pending!=null && pending.ParsedVersion.CompareTo(baseline)>0) baseline=pending.ParsedVersion;
                    UpdateDownload download=new UpdateClient().Fetch(baseline,gameHash);
                    if (updateCancelled) return new UpdateResult {Status="Update check canceled",ClearPending=clearPending};
                    if (download==null) return new UpdateResult {Status=pending==null ? "Up to date" : "Ready: restart for "+pending.Version,ClearPending=clearPending};
                    UpdateInstaller.Stage(root,download);
                    return new UpdateResult {Status="Ready: restart for "+download.Manifest.Version,Version=download.Manifest.Version,ClearPending=clearPending};
                }
                catch (Exception error) { return new UpdateResult {Status="Check failed: "+error.Message,ClearPending=clearPending}; }
            });
        }

        private void TickAutoUpdater()
        {
            if (updaterDisposed) return;
            if (updateTask!=null && updateTaskAutomatic && !automaticUpdates.Value) updateCancelled=true;
            if (updateTask!=null && updateTask.IsCompleted)
            {
                UpdateResult result;
                try { result=updateTask.Result; }
                catch (Exception) { result=new UpdateResult {Status="Update check failed; installed version retained"}; }
                updateTask=null; updateStatus=result.Status;
                if (result.ClearPending) { pendingUpdate=null; updateToast=false; }
                if (result.Version!=null) { pendingUpdate=result.Version; updateToast=true; }
                Log.LogInfo("Totem updater: "+updateStatus);
            }
            if (updateToast && Allowed)
            { ShowNotice("Totem update "+pendingUpdate+" ready\nRestart Gorilla Tag to install",false); updateToast=false; }
            if (automaticUpdates.Value && updateTask==null && Time.unscaledTime>=updateCheckAt)
                CheckForUpdates(true);
        }

        private void CancelPendingUpdate()
        {
            if (UpdateBusy()) return;
            try
            {
                bool removed=UpdateInstaller.CancelPending(updaterRoot);
                pendingUpdate=null; updateToast=false;
                updateStatus=removed ? "Pending update canceled" : "No pending update";
            }
            catch (Exception error) { updateStatus="Cancel failed: "+error.Message; }
        }

        private void StopAutoUpdater() { updaterDisposed=true; updateCancelled=true; }
    }
}
