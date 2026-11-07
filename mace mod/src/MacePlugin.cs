using System;
using BepInEx;
using BepInEx.Configuration;
using MaceMod.Shared;
using Photon.Pun;
using UnityEngine;
using UnityEngine.XR;
using Utilla.Attributes;

namespace MaceMod
{
    [BepInPlugin("macemod.gtag.rhys","Minecraft Mace Mod","1.0.0")]
    [BepInProcess("Gorilla Tag.exe")]
    [BepInDependency("org.legoandmars.gorillatag.utilla","1.5.0")]
    [ModdedGamemode]
    public sealed class MacePlugin : BaseUnityPlugin
    {
        private MaceClient client;
        private ConfigEntry<bool> particles,sound,glint,receiveHits,keyboard;
        private ConfigEntry<int> density,knockback,windBurst;
        private ConfigEntry<float> scale,power;
        private bool menuOpen,wasB;
        private Photon.Realtime.Room moddedRoom;
        private Rect window=new Rect(24,24,370,570);

        private void Awake()
        {
            try
            {
                string repairStatus=TotemMod.Updating.UtillaRepairBootstrap.EnsureInstalled(Paths.BepInExRootPath);
                if(!string.IsNullOrEmpty(repairStatus)) Logger.LogWarning(repairStatus);
            }
            catch(Exception error) { Logger.LogWarning("Utilla startup repair unavailable: "+error.Message); }
            density=Config.Bind("Enchantments","Density",0,new ConfigDescription("Smash bonus per block fallen.",new AcceptableValueRange<int>(0,5)));
            knockback=Config.Bind("Enchantments","Knockback",0,new ConfigDescription("Additional shove strength.",new AcceptableValueRange<int>(0,2)));
            windBurst=Config.Bind("Enchantments","WindBurst",0,new ConfigDescription("Upward rebound after a smash.",new AcceptableValueRange<int>(0,3)));
            scale=Config.Bind("Mace","Scale",1f,new ConfigDescription("Mace model size multiplier.",new AcceptableValueRange<float>(0.5f,1.5f)));
            power=Config.Bind("Mace","Power",1f,new ConfigDescription("Bounded local shove strength multiplier.",new AcceptableValueRange<float>(0.25f,2f)));
            particles=Config.Bind("Effects","Particles",true,"Real Minecraft critical-hit pixel sparks, bounded to 16 per impact.");
            sound=Config.Bind("Effects","Sound",true,"Real local Minecraft smash and wind-burst sounds.");
            glint=Config.Bind("Effects","Glint",true,"Opaque purple enchantment shimmer; preserves solid item depth.");
            receiveHits=Config.Bind("Mace","ReceiveHits",true,"Allow this client to receive mace hits from consenting mod users.");
            keyboard=Config.Bind("Controls","KeyboardM",true,"Press M to toggle the mace. F8 opens settings.");
            ConnectClient();
            Logger.LogInfo("Minecraft Mace Mod ready. B toggles mace; F8 opens settings. Private modded rooms only.");
        }

        private void ConnectClient()
        {
            if(client!=null || keyboard==null) return;
            client=MaceClient.Connect("mace",Allowed,delegate { return false; },delegate(string message) { Logger.LogInfo(message); });
            wasB=BDown();
            Configure(); client.Suspend(menuOpen);
        }
        private void DisconnectClient()
        {
            if(client!=null) { client.Dispose(); client=null; }
        }
        private void OnEnable() { ConnectClient(); }
        private void OnDisable() { menuOpen=false; DisconnectClient(); }
        private bool Allowed()
        {
            return isActiveAndEnabled && PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom!=null &&
                !PhotonNetwork.CurrentRoom.IsVisible && moddedRoom==PhotonNetwork.CurrentRoom;
        }
        [ModdedGamemodeJoin]
        public void OnModdedJoin(string gamemode) { moddedRoom=PhotonNetwork.CurrentRoom; }
        [ModdedGamemodeLeave]
        public void OnModdedLeave(string gamemode) { moddedRoom=null; }

        private void Configure()
        {
            if(client!=null) client.Configure(client.Equipped,density.Value,knockback.Value,windBurst.Value,scale.Value,
                particles.Value,sound.Value,glint.Value,receiveHits.Value,power.Value);
        }
        private void Update()
        {
            if(client==null) return;
            if(KeyDown(KeyCode.F8)) menuOpen=!menuOpen;
            bool b=BDown();
            if((b&&!wasB)||(keyboard.Value&&KeyDown(KeyCode.M))) Toggle();
            wasB=b;
            Configure(); client.Suspend(menuOpen); client.Tick();
        }
        private void Toggle()
        {
            if(client==null) return;
            client.Toggle();
        }
        private static bool BDown()
        {
            ControllerInputPoller poller=ControllerInputPoller.instance;
            if(poller!=null && poller.rightControllerSecondaryButton) return true;
            bool pressed; InputDevice device=InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            return device.isValid && device.TryGetFeatureValue(CommonUsages.secondaryButton,out pressed) && pressed;
        }
        private static bool KeyDown(KeyCode key)
        {
            try { return Input.GetKeyDown(key); }
            catch(InvalidOperationException) { return false; }
        }
        private void OnGUI()
        {
            if(!menuOpen) return;
            window=GUI.Window(681904,window,DrawWindow,"Minecraft Mace Settings");
        }
        private void DrawWindow(int id)
        {
            GUILayout.Label(client==null?"Mace unavailable":client.Status);
            GUILayout.Label("Private modded rooms | B / M: equip | F8: close");
            if(GUILayout.Button(client!=null && client.Equipped?"Unequip mace":"Equip mace")) Toggle();
            density.Value=IntSlider("Density",density.Value,0,5);
            knockback.Value=IntSlider("Knockback",knockback.Value,0,2);
            windBurst.Value=IntSlider("Wind Burst",windBurst.Value,0,3);
            scale.Value=FloatSlider("Size",scale.Value,0.5f,1.5f);
            power.Value=FloatSlider("Power",power.Value,0.25f,2f);
            receiveHits.Value=GUILayout.Toggle(receiveHits.Value,"Receive consenting players' mace hits");
            particles.Value=GUILayout.Toggle(particles.Value,"Minecraft impact particles");
            sound.Value=GUILayout.Toggle(sound.Value,"Minecraft impact sound");
            glint.Value=GUILayout.Toggle(glint.Value,"Opaque enchantment shimmer");
            keyboard.Value=GUILayout.Toggle(keyboard.Value,"Keyboard M toggle");
            GUILayout.Label("Totem Mod's Developer page exposes the same mace controls when both mods are installed.");
            if(GUILayout.Button("Close")) menuOpen=false;
            GUI.DragWindow(new Rect(0,0,window.width,24));
        }
        private static int IntSlider(string label,int value,int min,int max)
        {
            GUILayout.Label(label+": "+value); return Mathf.Clamp(Mathf.RoundToInt(GUILayout.HorizontalSlider(value,min,max)),min,max);
        }
        private static float FloatSlider(string label,float value,float min,float max)
        {
            GUILayout.Label(label+": "+value.ToString("0.00")+"x");
            return Mathf.Round(GUILayout.HorizontalSlider(value,min,max)*100f)/100f;
        }
        private void OnDestroy() { DisconnectClient(); }
    }
}
