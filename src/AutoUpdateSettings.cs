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
        internal const string ModVersion="1.9.7";
        private ConfigEntry<bool> automaticUpdates;
        private ConfigEntry<float> updateIntervalMinutes;
        private Task<UpdateResult> updateTask;
        private volatile bool updateCancelled;
        private bool updateTaskAutomatic,updaterDisposed,updateToast;
        private float updateCheckAt;
        private string updateStatus="Not checked",pendingUpdate,pendingMaceUpdate;
        private string updaterRoot,updaterGameAssembly,updaterHelper,companionHelper;
        private bool utillaRepairToast,companionReadyToast,companionAvailable;

        private sealed class UpdateResult
        {
            internal string Status,Version,MaceVersion;
            internal bool ClearPending,ClearMacePending,Retry;
        }

        private void InitializeAutoUpdater()
        {
            automaticUpdates=Config.Bind("Updates","Automatic",true,"Check signed PrimalCraft and installed Mace releases on startup and at the configured interval. Verified updates apply on the next game launch.");
            updateIntervalMinutes=SliderSetting("Updates","CheckIntervalMinutes",360,15,720,"Minutes between background update checks.");
            updaterRoot=Paths.BepInExRootPath;
            updaterGameAssembly=Path.Combine(Paths.ManagedPath,"Assembly-CSharp.dll");
            updaterHelper=Path.Combine(Paths.PatcherPluginPath,"TotemMod.Updater.dll");
            companionHelper=Path.Combine(Paths.PatcherPluginPath,"TotemMod.CompanionUpdater.dll");
            try
            {
                string helperStatus=CompanionBootstrap.EnsureInstalled(updaterRoot);
                if(helperStatus!=null) { Log.LogInfo(helperStatus); companionReadyToast=helperStatus=="Companion updater ready; restart Gorilla Tag"; }
                companionAvailable=File.Exists(companionHelper) && (helperStatus==null || companionReadyToast);
            }
            catch(Exception error) { Log.LogWarning("Companion updater setup: "+error.Message); }
            try
            {
                string repairStatus=UtillaRepairBootstrap.EnsureInstalled(updaterRoot);
                if (repairStatus!=null)
                {
                    Log.LogInfo(repairStatus);
                    utillaRepairToast=repairStatus=="Utilla repair ready; restart Gorilla Tag";
                }
            }
            catch (Exception error) { Log.LogWarning("Utilla repair setup: "+error.Message); }
            try
            {
                pendingUpdate=UpdateInstaller.PendingVersion(updaterRoot);
                if (pendingUpdate!=null && new Version(pendingUpdate).CompareTo(new Version(ModVersion))<=0)
                { UpdateInstaller.CancelPending(updaterRoot); pendingUpdate=null; }
                pendingMaceUpdate=MaceUpdateInstaller.PendingVersion(updaterRoot);
                string macePath=Path.Combine(updaterRoot,"plugins","MaceMod.dll");
                if(pendingMaceUpdate!=null && File.Exists(macePath) && new Version(pendingMaceUpdate).CompareTo(MaceUpdateInstaller.InstalledVersion(macePath))<=0)
                { MaceUpdateInstaller.CancelPending(updaterRoot); pendingMaceUpdate=null; }
            }
            catch (Exception error) { updateStatus="Pending update invalid"; Log.LogWarning("Pending PrimalCraft update was ignored: "+error.Message); }
            updateCheckAt=Time.unscaledTime+10;
            if (pendingUpdate!=null || pendingMaceUpdate!=null) updateStatus=PendingUpdatesText();
        }

        private bool UpdateBusy() { return updateTask!=null; }

        private void CheckForUpdates(bool automatic=false)
        {
            if (updaterDisposed || updateTask!=null) return;
            updateCheckAt=Time.unscaledTime+SafeFloat(updateIntervalMinutes.Value,360,15,720)*60;
            if (!File.Exists(updaterHelper) && !File.Exists(companionHelper)) { updateStatus="Updater helper missing; install the release ZIP"; return; }
            updateCancelled=false; updateTaskAutomatic=automatic; updateStatus="Checking GitHub...";
            string root=updaterRoot,game=updaterGameAssembly;
            bool maceUpdatesReady=companionAvailable;
            updateTask=Task.Run(() => {
                UpdateResult result=new UpdateResult();
                string totemStatus="PrimalCraft up to date",maceStatus=null;
                string gameHash;
                try
                {
                    using (SHA256 sha=SHA256.Create())
                    using (FileStream input=File.OpenRead(game)) gameHash=BitConverter.ToString(sha.ComputeHash(input)).Replace("-","");
                }
                catch(Exception error) { result.Status="Check failed: "+error.Message; result.Retry=true; return result; }
                try
                {
                    UpdateManifest pending=UpdateInstaller.PendingManifest(root);
                    if (pending!=null && !pending.SupportedGame(gameHash))
                    { UpdateInstaller.CancelPending(root); pending=null; result.ClearPending=true; }
                    Version baseline=new Version(ModVersion);
                    if (pending!=null && pending.ParsedVersion.CompareTo(baseline)>0) baseline=pending.ParsedVersion;
                    UpdateDownload download=new UpdateClient().Fetch(baseline,gameHash);
                    if (updateCancelled) { result.Status="Update check canceled"; return result; }
                    if(download!=null) { UpdateInstaller.Stage(root,download); result.Version=download.Manifest.Version; totemStatus="PrimalCraft "+result.Version+" ready"; }
                    else if(pending!=null) { result.Version=pending.Version; totemStatus="PrimalCraft "+pending.Version+" ready"; }
                }
                catch(Exception error) { result.Retry=true; totemStatus="PrimalCraft check failed: "+error.Message; }
                if(updateCancelled) { result.Status=totemStatus; return result; }
                try
                {
                    string macePath=Path.Combine(root,"plugins","MaceMod.dll");
                    if(File.Exists(macePath))
                    {
                        if(!maceUpdatesReady || !File.Exists(Path.Combine(root,"patchers","TotemMod.CompanionUpdater.dll"))) throw new InvalidDataException("Mace updater helper is unavailable.");
                        Version baseline=MaceUpdateInstaller.InstalledVersion(macePath);
                        UpdateManifest pending=MaceUpdateInstaller.PendingManifest(root);
                        if(pending!=null && !pending.SupportedGame(gameHash)) { MaceUpdateInstaller.CancelPending(root); pending=null; result.ClearMacePending=true; }
                        if(pending!=null && pending.ParsedVersion.CompareTo(baseline)>0) baseline=pending.ParsedVersion;
                        UpdateDownload download=new UpdateClient(UpdateProduct.Mace).Fetch(baseline,gameHash);
                        if(updateCancelled) { result.Status=totemStatus; return result; }
                        if(download!=null) { MaceUpdateInstaller.Stage(root,download); result.MaceVersion=download.Manifest.Version; maceStatus="Mace "+result.MaceVersion+" ready"; }
                        else if(pending!=null) { result.MaceVersion=pending.Version; maceStatus="Mace "+pending.Version+" ready"; }
                        else maceStatus="Mace up to date";
                    }
                }
                catch(Exception error) { result.Retry=true; maceStatus="Mace check failed: "+error.Message; }
                result.Status=totemStatus+(maceStatus!=null ? " | "+maceStatus : ""); return result;
            });
        }

        private void TickAutoUpdater()
        {
            if (updaterDisposed) return;
            if (utillaRepairToast && Allowed)
            { ShowNotice("Utilla fix ready\nRestart Gorilla Tag to apply",false); utillaRepairToast=false; }
            if(companionReadyToast && Allowed)
            { ShowNotice("Auto updater ready\nRestart Gorilla Tag to activate",false); companionReadyToast=false; }
            if (updateTask!=null && updateTaskAutomatic && !automaticUpdates.Value) updateCancelled=true;
            if (updateTask!=null && updateTask.IsCompleted)
            {
                UpdateResult result;
                try { result=updateTask.Result; }
                catch (Exception) { result=new UpdateResult {Status="Update check failed; installed version retained"}; }
                updateTask=null; updateStatus=result.Status;
                if (result.Retry) updateCheckAt=Time.unscaledTime+300;
                if (result.ClearPending) { pendingUpdate=null; updateToast=false; }
                if (result.ClearMacePending) pendingMaceUpdate=null;
                if (result.Version!=null) { pendingUpdate=result.Version; updateToast=true; }
                if (result.MaceVersion!=null) { pendingMaceUpdate=result.MaceVersion; updateToast=true; }
                Log.LogInfo("PrimalCraft updater: "+updateStatus);
            }
            if (updateToast && Allowed)
            { ShowNotice("Mod update ready\nRestart Gorilla Tag to install",false); updateToast=false; }
            if (automaticUpdates.Value && updateTask==null && Time.unscaledTime>=updateCheckAt)
                CheckForUpdates(true);
        }

        private void CancelPendingUpdate()
        {
            if (UpdateBusy()) return;
            try
            {
                bool removed=UpdateInstaller.CancelPending(updaterRoot);
                removed=MaceUpdateInstaller.CancelPending(updaterRoot)||removed;
                pendingUpdate=pendingMaceUpdate=null; updateToast=false;
                updateStatus=removed ? "Pending update canceled" : "No pending update";
            }
            catch (Exception error) { updateStatus="Cancel failed: "+error.Message; }
        }

        private void StopAutoUpdater() { updaterDisposed=true; updateCancelled=true; }
        private string PendingUpdatesText()
        { return pendingUpdate==null && pendingMaceUpdate==null ? "No update waiting" : "Next launch: "+(pendingUpdate!=null ? "PrimalCraft "+pendingUpdate : "")+(pendingUpdate!=null && pendingMaceUpdate!=null ? " + " : "")+(pendingMaceUpdate!=null ? "Mace "+pendingMaceUpdate : ""); }
    }
}
