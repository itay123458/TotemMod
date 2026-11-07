using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace TotemMod
{
    public sealed partial class TotemPlugin
    {
        private readonly BetaTotemVisuals betaVisuals=new BetaTotemVisuals();
        private ConfigEntry<bool> betaEnabled,betaInPop,betaUsePlayerColor,betaRainbow;
        private ConfigEntry<int> betaStyle,betaSourceActor;
        private ConfigEntry<float> betaScale,betaSpin,betaBob,betaRainbowSpeed;
        private ConfigEntry<Vector3> betaTint,betaTilt;
        private Renderer[] betaHeldRenderers;
        private readonly MaterialPropertyBlock betaTintBlock=new MaterialPropertyBlock();
        private Material betaClassicMaterial;
        private bool betaWaitingForRig;
        private float betaRetryAt;
        private static readonly string[] BetaStyleNames={"Classic totem","Player model","Crystal","Block","Star"};

        private void InitializeBetaSettings()
        {
            betaEnabled=Config.Bind("Beta","Enabled",false,"Local experimental appearance. Friends keep their normal totem visuals.");
            betaInPop=Config.Bind("Beta","UseInPop",true,"Use the selected Beta model in your local pop animation.");
            betaUsePlayerColor=Config.Bind("Beta","PlayerColors",true,"Keep original avatar colors instead of tinting.");
            betaRainbow=Config.Bind("Beta","Rainbow",false,"Cycle local model tint; disables original player colors.");
            betaStyle=Config.Bind("Beta","Model",1,new ConfigDescription("0 Classic, 1 Player, 2 Crystal, 3 Block, 4 Star.",new AcceptableValueRange<int>(0,4)));
            betaSourceActor=Config.Bind("Beta","PlayerActor",0,"Avatar source in this room; zero means yourself. Resets on room change.");
            betaScale=SliderSetting("Beta","ModelScale",1,0.5f,2,"Extra local Beta model scale.");
            betaSpin=SliderSetting("Beta","Spin",0,-120,120,"Held model rotation in degrees per second.");
            betaBob=SliderSetting("Beta","Bob",0,0,0.025f,"Held model bob height in meters.");
            betaRainbowSpeed=SliderSetting("Beta","RainbowSpeed",0.15f,0.05f,0.5f,"Rainbow color cycles per second.");
            betaTint=Config.Bind("Beta","Tint",Vector3.one,"Red, green and blue tint channels, each 0 to 1.");
            betaTilt=Config.Bind("Beta","Tilt",Vector3.zero,"Extra local model pitch, yaw and roll.");
        }
        private void BetaChanged(bool refresh=false)
        {
            betaWaitingForRig=false;
            if (refresh) betaVisuals.ClearCache();
            HideHeld(); ChangedConfig();
        }
        private void SetBetaFloat(ConfigEntry<float> entry,float value,float min,float max)
        { entry.Value=Mathf.Clamp(value,min,max); BetaChanged(); }
        private void FlipBeta(ConfigEntry<bool> entry) { entry.Value=!entry.Value; BetaChanged(); }
        private Color BetaColor()
        {
            if (betaRainbow.Value) return Color.HSVToRGB(Mathf.Repeat(Time.unscaledTime*betaRainbowSpeed.Value,1),0.8f,1);
            Vector3 value=betaTint.Value;
            return new Color(SafeFloat(value.x,1,0,1),SafeFloat(value.y,1,0,1),SafeFloat(value.z,1,0,1));
        }
        private VRRig BetaSourceRig()
        {
            if (betaSourceActor.Value>0 && PhotonNetwork.CurrentRoom!=null && GorillaGameManager.instance!=null)
            {
                Player player=PhotonNetwork.CurrentRoom.GetPlayer(betaSourceActor.Value);
                if (player!=null && !player.IsInactive && !player.IsLocal)
                {
                    NetPlayer net=NetPlayer.Get(player);
                    if (net!=null && net.IsValid) return GorillaGameManager.instance.FindPlayerVRRig(net);
                }
            }
            return GorillaTagger.Instance!=null ? GorillaTagger.Instance.offlineVRRig : null;
        }
        private string BetaSourceName()
        {
            Player player=PhotonNetwork.CurrentRoom!=null ? PhotonNetwork.CurrentRoom.GetPlayer(betaSourceActor.Value) : null;
            return player!=null && !player.IsLocal && !player.IsInactive ? player.NickName : "My avatar";
        }
        private void CycleBetaSource(int direction)
        {
            List<int> actors=new List<int>(); actors.Add(0);
            if (PhotonNetwork.CurrentRoom!=null)
                foreach (Player player in PhotonNetwork.CurrentRoom.Players.Values)
                    if (!player.IsLocal && !player.IsInactive) actors.Add(player.ActorNumber);
            actors.Sort(); int current=Math.Max(0,actors.IndexOf(betaSourceActor.Value));
            betaSourceActor.Value=actors[(current+direction+actors.Count)%actors.Count]; BetaChanged(true);
        }
        private GameObject CreateLocalVisual(Transform parent,string name,bool pop)
        {
            GameObject visual=null;
            if (betaEnabled.Value && (!pop || betaInPop.Value) && betaStyle.Value!=0)
            {
                visual=betaVisuals.Create(parent,name,BetaSourceRig(),betaStyle.Value,BetaColor(),betaUsePlayerColor.Value && !betaRainbow.Value);
                if (betaStyle.Value==BetaTotemVisuals.PlayerModel)
                {
                    bool failed=visual==null;
                    if (failed) { betaRetryAt=Time.unscaledTime+3; if (!betaWaitingForRig) ShowNotice("Avatar not ready; using classic totem",false); }
                    betaWaitingForRig=failed;
                }
            }
            if (visual==null)
            {
                visual=assets.Create(parent,name);
                if (betaEnabled.Value && (!pop || betaInPop.Value) && betaStyle.Value==0)
                {
                    if (betaClassicMaterial==null)
                    {
                        Shader shader=Shader.Find("Sprites/Default");
                        if (shader!=null) { betaClassicMaterial=new Material(shader); betaClassicMaterial.mainTexture=assets.Material.mainTexture; }
                    }
                    if (betaClassicMaterial!=null) visual.GetComponent<Renderer>().sharedMaterial=betaClassicMaterial;
                    if (!betaUsePlayerColor.Value || betaRainbow.Value) ApplyBetaTint(visual.GetComponentsInChildren<Renderer>());
                }
            }
            int layer=PopEffectLayer(true);
            foreach (Transform part in visual.GetComponentsInChildren<Transform>(true)) part.gameObject.layer=layer;
            return visual;
        }
        private void ApplyBetaHeld(GameObject item)
        {
            if (!betaEnabled.Value) return;
            item.transform.localScale*=SafeFloat(betaScale.Value,1,0.5f,2);
            Vector3 tilt=BetaRotation();
            item.transform.localRotation*=Quaternion.Euler(tilt+new Vector3(0,Time.unscaledTime*SafeFloat(betaSpin.Value,0,-120,120),0));
            item.transform.localPosition+=Vector3.up*(Mathf.Sin(Time.unscaledTime*2)*SafeFloat(betaBob.Value,0,0,0.025f));
            if (betaRainbow.Value)
            {
                if (betaHeldRenderers==null) betaHeldRenderers=item.GetComponentsInChildren<Renderer>();
                ApplyBetaTint(betaHeldRenderers);
            }
        }
        private void ApplyBetaTint(Renderer[] renderers)
        {
            betaTintBlock.Clear(); Color tint=BetaColor(); betaTintBlock.SetColor("_Color",tint); betaTintBlock.SetColor("_BaseColor",tint);
            for (int i=0;i<renderers.Length;i++) if (renderers[i]!=null) renderers[i].SetPropertyBlock(betaTintBlock);
        }
        private Vector3 BetaRotation()
        {
            Vector3 value=betaTilt.Value;
            return new Vector3(SafeFloat(value.x,0,-180,180),SafeFloat(value.y,0,-180,180),SafeFloat(value.z,0,-180,180));
        }
        private void MaintainBetaSource()
        {
            if (betaWaitingForRig && betaEnabled.Value && state.Equipped && Time.unscaledTime>=betaRetryAt)
            { HideHeld(); betaRetryAt=Time.unscaledTime+3; }
            if (betaSourceActor.Value<=0) return;
            Player player=PhotonNetwork.CurrentRoom!=null ? PhotonNetwork.CurrentRoom.GetPlayer(betaSourceActor.Value) : null;
            if (player!=null && !player.IsInactive) return;
            betaSourceActor.Value=0; BetaChanged(true);
        }
        private void ResetBeta()
        {
            betaEnabled.Value=false; betaStyle.Value=1; betaSourceActor.Value=0; betaInPop.Value=true; betaUsePlayerColor.Value=true;
            betaRainbow.Value=false; betaScale.Value=1; betaSpin.Value=0; betaBob.Value=0; betaTint.Value=Vector3.one; betaTilt.Value=Vector3.zero;
            BetaChanged(true);
        }
        private void BetaPreset(int preset)
        {
            ResetBeta(); betaEnabled.Value=true;
            if (preset==0) { betaStyle.Value=1; betaSpin.Value=15; }
            if (preset==1) { betaStyle.Value=2; betaUsePlayerColor.Value=false; betaTint.Value=new Vector3(0.15f,1,0.35f); betaSpin.Value=35; betaBob.Value=0.01f; }
            if (preset==2) { betaStyle.Value=4; betaUsePlayerColor.Value=false; betaRainbow.Value=true; betaSpin.Value=50; }
            if (preset==3) { betaStyle.Value=3; betaUsePlayerColor.Value=false; betaTint.Value=new Vector3(1,0.75f,0.1f); betaSpin.Value=25; }
            BetaChanged();
        }
    }
}
