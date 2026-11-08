using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;

namespace TotemMod
{
    public sealed partial class TotemPlugin
    {
        private bool legacyMaceRetired;
        private float maceRetirementAt;

        private void RetireLegacyMace()
        {
            if(legacyMaceRetired || Time.unscaledTime<maceRetirementAt) return;
            maceRetirementAt=Time.unscaledTime+1;
            PluginInfo plugin;
            if(!Chainloader.PluginInfos.TryGetValue("macemod.gtag.rhys",out plugin) || plugin==null || plugin.Instance==null) return;
            if(plugin.Metadata.Version.CompareTo(new System.Version(1,9,8))<0)
            {
                plugin.Instance.enabled=false;
                Log.LogInfo("Disabled the removed legacy mace companion. Its inactive retirement update will apply through the updater.");
            }
            legacyMaceRetired=true;
        }
    }
}
