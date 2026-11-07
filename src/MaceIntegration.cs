using BepInEx.Configuration;
using MaceMod.Shared;
using Photon.Pun;
using UnityEngine;

namespace TotemMod
{
    public sealed partial class TotemPlugin
    {
        private MaceClient mace;
        private ConfigEntry<int> maceDensity,maceKnockback,maceWind;
        private ConfigEntry<float> maceScale,macePower;
        private ConfigEntry<bool> maceReceive,maceParticles,maceSound,maceGlint;
        private bool maceButtonBefore,maceForDeveloper;

        private void InitializeMace()
        {
            maceDensity=Config.Bind("Mace","Density",0,new ConfigDescription("Extra falling-hit power, level 0 to 5.",new AcceptableValueRange<int>(0,5)));
            maceKnockback=Config.Bind("Mace","Knockback",0,new ConfigDescription("Extra mace knockback, level 0 to 2.",new AcceptableValueRange<int>(0,2)));
            maceWind=Config.Bind("Mace","WindBurst",0,new ConfigDescription("Smash rebound, level 0 to 3.",new AcceptableValueRange<int>(0,3)));
            maceScale=SliderSetting("Mace","Scale",1,0.5f,1.5f,"Mace size and reach multiplier.");
            macePower=SliderSetting("Mace","Power",1,0.25f,2,"Mace knockback multiplier; final velocity is bounded.");
            maceReceive=Config.Bind("Mace","ReceiveHits",true,"Accept mace hits from updated mod peers in private modded Infection / Tag.");
            maceParticles=Config.Bind("Mace","Particles",true,"Minecraft critical-hit particles.");
            maceSound=Config.Bind("Mace","Sound",true,"Minecraft mace impact and smash sound.");
            maceGlint=Config.Bind("Mace","EnchantmentShimmer",true,"Opaque enchantment shimmer on your mace.");
            ConnectMace();
        }
        private void ConnectMace()
        {
            maceForDeveloper=IsDeveloper;
            mace=MaceClient.Connect(maceForDeveloper ? "totem-dev" : "totem",()=>isActiveAndEnabled && Allowed,CanMaceTotem,text=>ShowNotice(text,false),()=>PopFromTag());
        }
        private void StopMace() { if(mace!=null) { mace.Dispose(); mace=null; } }
        private bool CanMaceTotem()
        {
            if(!Allowed || PhotonNetwork.CurrentRoom==null || PhotonNetwork.CurrentRoom.IsVisible ||
                !RulesPermitProtection() || !state.Equipped || inventory.Count<=0) return false;
            return true;
        }
        private void TickMace()
        {
            if(mace==null) ConnectMace();
            else if(maceForDeveloper!=IsDeveloper) { StopMace(); ConnectMace(); }
            ControllerInputPoller poller=ControllerInputPoller.instance;
            bool button=poller!=null && poller.rightControllerSecondaryButton || KeyHeld(KeyCode.M);
            if(IsDeveloper && Allowed && button && !maceButtonBefore && (menu==null || !menu.IsOpen)) mace.Toggle();
            maceButtonBefore=button;
            mace.Configure(mace.Equipped,maceDensity.Value,maceKnockback.Value,maceWind.Value,maceScale.Value,
                maceParticles.Value,maceSound.Value,maceGlint.Value,maceReceive.Value,macePower.Value);
            mace.Suspend(menu!=null && menu.IsOpen);
            mace.Tick();
        }
        private MenuEntry MaceLevel(string title,ConfigEntry<int> setting,int max)
        {
            return new MenuEntry(()=>title+": "+setting.Value,()=>setting.Value,value=>{
                if(IsDeveloper) { setting.Value=Mathf.RoundToInt(value); ChangedConfig(); }
            },0,max,1,()=>IsDeveloper);
        }
        private void ResetMaceEnchantments()
        { if(!IsDeveloper) return; maceDensity.Value=0; maceKnockback.Value=0; maceWind.Value=0; maceScale.Value=1; macePower.Value=1; ChangedConfig(); }
    }
}
