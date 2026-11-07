using System;
using BepInEx.Configuration;
using UnityEngine;

namespace TotemMod
{
    public sealed partial class TotemPlugin
    {
        private ConfigEntry<float> menuDistance,menuClickVolume,pointerWidth,pointerSmoothing,menuHoldSeconds;
        private ConfigEntry<float> popDistance,popHeight,popPitch,particleBrightness,particleSpin,friendRenderScale;
        private ConfigEntry<int> popMotion,particleShape;
        private ConfigEntry<bool> popSound,friendPopSound,hudShowTimer,hudPersistentStock,notifyEquip,notifyRegen,notifyPop;
        private ConfigEntry<Vector3> savedPalmPosition,savedPalmRotation;
        private ConfigEntry<float> savedPalmScale;

        private ConfigEntry<float> SliderSetting(string section,string key,float initial,float minimum,float maximum,string description)
        { return Config.Bind(section,key,initial,new ConfigDescription(description,new AcceptableValueRange<float>(minimum,maximum))); }

        private void InitializeExpandedSettings()
        {
            menuDistance=SliderSetting("Menu","Distance",1.1f,0.65f,1.8f,"Distance when opening or recentering the menu.");
            menuClickVolume=SliderSetting("Menu","ClickVolume",0.3f,0,1,"Minecraft menu click volume.");
            pointerWidth=SliderSetting("Menu","PointerWidth",1,0.5f,2,"Controller laser width multiplier.");
            pointerSmoothing=SliderSetting("Menu","PointerSmoothing",1,0,1,"Reticle smoothing; zero uses raw aim.");
            menuHoldSeconds=SliderSetting("Controls","MenuHoldSeconds",0.6f,0.3f,1.5f,"Hold X duration to toggle the menu.");
            popDistance=SliderSetting("Effects","PopDistance",0.65f,0.4f,1.2f,"Local pop distance in front of your eyes.");
            popHeight=SliderSetting("Effects","PopHeight",-0.07f,-0.2f,0.2f,"Local pop vertical offset.");
            popPitch=SliderSetting("Effects","PopPitch",1,0.5f,1.5f,"Minecraft pop sound pitch.");
            particleBrightness=SliderSetting("Effects","ParticleBrightness",1,0.5f,1.5f,"Pixel spark brightness multiplier.");
            particleSpin=SliderSetting("Effects","ParticleSpin",1,0,2,"Pixel spark rotation multiplier.");
            friendRenderScale=SliderSetting("Effects","FriendRenderScale",1,0.5f,1.5f,"Local size multiplier for friends' totems.");
            popMotion=Config.Bind("Effects","PopMotion",0,new ConfigDescription("0 Minecraft sway, 1 Spin, 2 Still.",new AcceptableValueRange<int>(0,2)));
            particleShape=Config.Bind("Effects","ParticleShape",0,new ConfigDescription("0 Mixed, 1 Sparkles, 2 Flecks.",new AcceptableValueRange<int>(0,2)));
            popSound=Config.Bind("Effects","PopSound",true,"Play the Minecraft totem sound.");
            friendPopSound=Config.Bind("Effects","FriendPopSound",true,"Hear nearby friends' pop sounds.");
            hudShowTimer=Config.Bind("Notifications","ShowTimer",true,"Show the running shared timer on the HUD.");
            hudPersistentStock=Config.Bind("Notifications","PersistentStock",false,"Keep remaining stock visible.");
            notifyEquip=Config.Bind("Notifications","EquipNotices",true,"Show equip/unequip notices.");
            notifyRegen=Config.Bind("Notifications","RegenerationNotices",true,"Show regeneration notices.");
            notifyPop=Config.Bind("Notifications","PopNotices",true,"Show pop notices; empty stock still warns.");
            savedPalmPosition=Config.Bind("Hand","SavedPalmPosition",new Vector3(0,0.005f,0),"Saved custom grip slot.");
            savedPalmRotation=Config.Bind("Hand","SavedPalmRotation",new Vector3(-90,-90,0),"Saved custom grip rotation.");
            savedPalmScale=SliderSetting("Hand","SavedPalmScale",0.75f,0.25f,1.5f,"Saved custom grip scale.");
            InitializeBetaSettings();
        }

        private void ApplyMenuSettings()
        {
            menu.Scale=menuScale.Value; menu.MenuDistance=menuDistance.Value; menu.ClickVolume=menuClickVolume.Value;
            menu.PointerWidth=pointerWidth.Value; menu.PointerSmoothing=pointerSmoothing.Value;
            menu.PointerColor=PointerColors[Math.Max(0,Math.Min(PointerColors.Length-1,pointerStyle.Value))];
        }
        private void SaveGrip()
        {
            savedPalmPosition.Value=handPosition.Value; savedPalmRotation.Value=handRotation.Value; savedPalmScale.Value=heldScale.Value;
            ChangedConfig(); ShowNotice("Custom grip saved",false);
        }
        private void LoadGrip()
        {
            handPosition.Value=savedPalmPosition.Value; handRotation.Value=savedPalmRotation.Value; heldScale.Value=savedPalmScale.Value; ChangedConfig();
        }
        private void SetGrip(int preset)
        {
            ResetPose();
            if (preset==1) handRotation.Value=new Vector3(0,-90,0);
            if (preset==2) handRotation.Value=new Vector3(-90,0,0);
            if (preset==3) heldScale.Value=0.45f;
            ChangedConfig();
        }
        private void ResetMenuSettings()
        {
            menuScale.Value=1; menuDistance.Value=1.1f; menuClickVolume.Value=0.3f;
            pointerWidth.Value=1; pointerSmoothing.Value=1; pointerStyle.Value=0; menuHoldSeconds.Value=0.6f; ChangedConfig();
        }
        private void ResetHudSettings()
        {
            hudEnabled.Value=true; hudCountdown.Value=true; hudShowTimer.Value=true; hudPersistentStock.Value=false;
            hudOffset.Value=new Vector3(-0.28f,-0.22f,0.85f); hudScale.Value=1; hudDuration.Value=5;
            notifyEquip.Value=true; notifyRegen.Value=true; notifyPop.Value=true; ChangedConfig();
        }
        private void SetEffectPreset(int preset)
        {
            fx.Value=true; particleCount.Value=48; particleSize.Value=1; particleSpread.Value=1; particlePalette.Value=0;
            particleMotion.Value=0; particleShape.Value=0; particleBrightness.Value=1; particleSpin.Value=1;
            popSpeed.Value=1; popMotion.Value=0; popSound.Value=true; popPitch.Value=1; volume.Value=0.9f; haptics.Value=true;
            popDistance.Value=0.65f; popHeight.Value=-0.07f;
            if (preset==1) { volume.Value=0; popSound.Value=false; haptics.Value=false; particleCount.Value=16; }
            if (preset==2) { particleCount.Value=64; particleSize.Value=1.5f; particleMotion.Value=1; popSpeed.Value=0.75f; }
            if (preset==3) { particlePalette.Value=3; particleShape.Value=1; particleSize.Value=1.25f; }
            if (preset==4) { particlePalette.Value=2; particleBrightness.Value=1.4f; particleMotion.Value=1; }
            if (preset==5) { particleCount.Value=12; particleSize.Value=0.75f; haptics.Value=false; }
            ChangedConfig();
        }
        private void PreviewPop()
        {
            if (!Allowed || Time.unscaledTime<demoReadyAt || !EnsureAssets()) return;
            if (!fx.Value) { ShowNotice("Enable Pop effects to preview",false); return; }
            demoReadyAt=Time.unscaledTime+3.5f; Transform head=GetHead();
            if (head!=null) StartCoroutine(PopAnimation(head,true,SafeSize()));
        }
    }
}
