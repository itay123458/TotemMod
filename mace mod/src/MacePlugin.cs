using System;
using BepInEx;
using BepInEx.Configuration;
using MaceMod.Shared;
using Photon.Pun;
using UnityEngine;
using Utilla.Attributes;

namespace MaceMod
{
    [BepInPlugin("macemod.gtag.rhys","Minecraft Mace Mod","1.0.1")]
    [BepInProcess("Gorilla Tag.exe")]
    [BepInDependency("org.legoandmars.gorillatag.utilla","1.5.0")]
    [ModdedGamemode]
    public sealed class MacePlugin : BaseUnityPlugin
    {
        private MaceClient client;
        private ConfigEntry<bool> particles,sound,receiveHits;
        private bool menuOpen;
        private Photon.Realtime.Room moddedRoom;
        private Rect window=new Rect(24,24,370,255);

        private void Awake()
        {
            try
            {
                string repairStatus=TotemMod.Updating.UtillaRepairBootstrap.EnsureInstalled(Paths.BepInExRootPath);
                if(!string.IsNullOrEmpty(repairStatus)) Logger.LogWarning(repairStatus);
            }
            catch(Exception error) { Logger.LogWarning("Utilla startup repair unavailable: "+error.Message); }
            particles=Config.Bind("Effects","Particles",true,"Show authenticated Minecraft mace impact particles.");
            sound=Config.Bind("Effects","Sound",true,"Play authenticated Minecraft mace smash and wind-burst sounds.");
            receiveHits=Config.Bind("Mace","ReceiveHits",true,"Receive authenticated developer mace hits in a private modded room.");
            ConnectClient();
            Logger.LogInfo("Minecraft Mace receiver ready. F8 opens receiver settings. Weapon access is limited to Totem's Developer menu.");
        }

        private void ConnectClient()
        {
            if(client!=null || receiveHits==null) return;
            client=MaceClient.Connect("mace",Allowed,delegate { return false; },delegate(string message) { Logger.LogInfo(message); });
            Configure();
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
            if(client!=null) client.Configure(client.Equipped,0,0,0,1f,particles.Value,sound.Value,false,receiveHits.Value,1f);
        }
        private void Update()
        {
            if(client==null) return;
            if(KeyDown(KeyCode.F8)) menuOpen=!menuOpen;
            Configure(); client.Tick();
        }
        private static bool KeyDown(KeyCode key)
        {
            try { return Input.GetKeyDown(key); }
            catch(InvalidOperationException) { return false; }
        }
        private void OnGUI()
        {
            if(!menuOpen) return;
            window=GUI.Window(681904,window,DrawWindow,"Minecraft Mace Receiver Settings");
        }
        private void DrawWindow(int id)
        {
            GUILayout.Label(client==null?"Mace receiver unavailable":client.Status);
            GUILayout.Label("Private modded rooms | F8: close");
            receiveHits.Value=GUILayout.Toggle(receiveHits.Value,"Receive developer mace hits");
            particles.Value=GUILayout.Toggle(particles.Value,"Minecraft impact particles");
            sound.Value=GUILayout.Toggle(sound.Value,"Minecraft impact sound");
            GUILayout.Label("Weapon access and enchantments are available only in Totem Mod's authorized Developer menu.");
            if(GUILayout.Button("Close")) menuOpen=false;
            GUI.DragWindow(new Rect(0,0,window.width,24));
        }
        private void OnDestroy() { DisconnectClient(); }
    }
}
