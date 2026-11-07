using System;
using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace TotemMod
{
    public sealed partial class TotemPlugin
    {
        private BetaRoomSync betaSync;
        private BetaAppearance localBetaAppearance;
        private BetaAppearance localRenderedAppearance;
        private bool betaAppearanceDirty=true;
        private int betaGrantActor;
        private bool betaMenuBefore;
        private string betaLocalId;
        private bool betaLocalIdAllowed;
        private float betaIdentityAt;
        private Photon.Realtime.Room betaAuthorityRoom;
        private int betaAuthorityMaster;
        private string betaAuthorityValue;
        private readonly Dictionary<int, BetaAccountRecord> betaAccountCache=new Dictionary<int,BetaAccountRecord>();
        private sealed class BetaAccountRecord { internal Player Player; internal string Id; internal bool Allowed; }
        private bool HasBetaAccess { get { return betaSync!=null && betaSync.LocalAccess; } }

        private void InitializeBetaSync()
        {
            betaSync=new BetaRoomSync(() => isActiveAndEnabled && Allowed,() => IsDeveloper,BetaAuthority,
                SignDeveloperCommand,(data,signature) => DeveloperIdentity.Verify(data,signature),ReceiveBetaAppearance,RemoveBetaAppearance,
                access => { betaAppearanceDirty=true; HideHeld(); },LocalBetaAccount,RemoteBetaAccount);
        }
        private string BetaAuthority()
        {
            Photon.Realtime.Room room=PhotonNetwork.CurrentRoom;
            if (room==null) return null;
            if (room!=betaAuthorityRoom || room.MasterClientId!=betaAuthorityMaster)
            { betaAuthorityRoom=room; betaAuthorityMaster=room.MasterClientId; betaAuthorityValue="beta1:"+room.Name+":"+room.MasterClientId; }
            return betaAuthorityValue;
        }
        private bool LocalBetaAccount()
        {
            if (IsDeveloper) return true;
            if (Time.unscaledTime>=betaIdentityAt)
            {
                betaIdentityAt=Time.unscaledTime+1;
                string id=NetworkSystem.Instance!=null ? NetworkSystem.Instance.GetMyUserID() : null;
                if (id!=betaLocalId) { betaLocalId=id; betaLocalIdAllowed=BetaAccountAccess.Allows(id); }
            }
            return betaLocalIdAllowed;
        }
        private bool RemoteBetaAccount(int actor)
        {
            Player player=PhotonNetwork.CurrentRoom!=null ? PhotonNetwork.CurrentRoom.GetPlayer(actor) : null;
            if (player==null || player.IsInactive) return false;
            BetaAccountRecord record;
            string id=player.UserId;
            if (betaAccountCache.TryGetValue(actor,out record) && record.Player==player && record.Id==id) return record.Allowed;
            bool accepted=BetaAccountAccess.Allows(id);
            betaAccountCache[actor]=new BetaAccountRecord {Player=player,Id=id,Allowed=accepted}; return accepted;
        }
        private BetaAppearance LocalBetaState()
        {
            if (localBetaAppearance==null || betaAppearanceDirty)
            {
                Color tint=BetaColor(false); Vector3 tilt=BetaRotation();
                localBetaAppearance=new BetaAppearance(betaEnabled.Value && HasBetaAccess,betaStyle.Value,Math.Max(0,betaSourceActor.Value),
                    betaUsePlayerColor.Value,betaInPop.Value,tint.r,tint.g,tint.b,SafeFloat(betaScale.Value,1,0.5f,2),
                    SafeFloat(betaSpin.Value,0,-120,120),SafeFloat(betaBob.Value,0,0,0.025f),betaRainbow.Value,
                    SafeFloat(betaRainbowSpeed.Value,0.15f,0.05f,0.5f),tilt.x,tilt.y,tilt.z);
                betaAppearanceDirty=false;
            }
            return localBetaAppearance;
        }
        private void ReceiveBetaAppearance(int actor,BetaAppearance appearance)
        {
            FriendTotem friend;
            if (!friends.TryGetValue(actor,out friend))
            { friend=new FriendTotem(); friend.Supply.Configure(rules.Capacity,rules.Regeneration,Time.unscaledTime); friends.Add(actor,friend); }
            friend.Appearance=appearance;
        }
        private void RemoveBetaAppearance(int actor)
        {
            FriendTotem friend;
            if (friends.TryGetValue(actor,out friend)) { friend.Appearance=null; friend.RenderAppearance=null; if (friend.Held!=null) UnityEngine.Object.Destroy(friend.Held); friend.Held=null; }
        }
        private void ResetBetaNetwork()
        {
            if (betaSync!=null) betaSync.Reset(); betaAccountCache.Clear(); betaLocalId=null; betaLocalIdAllowed=false; betaIdentityAt=0;
            betaAppearanceDirty=true; localRenderedAppearance=null; betaGrantActor=0;
        }
        private BetaAppearance FriendBeta(int actor,FriendTotem friend)
        {
            BetaAppearance value=friend.Appearance;
            return value!=null && value.Enabled && friend.State.Snapshot!=null && value.Session==friend.State.Snapshot.Session && betaSync.AccessFor(actor) ? value : null;
        }
        private void CycleBetaGrantPlayer(int direction)
        {
            List<int> actors=new List<int>();
            if (PhotonNetwork.CurrentRoom!=null)
                foreach (Player p in PhotonNetwork.CurrentRoom.Players.Values) if (!p.IsLocal && !p.IsInactive) actors.Add(p.ActorNumber);
            actors.Sort(); if (actors.Count==0) { betaGrantActor=0; return; }
            int current=actors.IndexOf(betaGrantActor); if (current<0) betaGrantActor=actors[0];
            else betaGrantActor=actors[(current+direction+actors.Count)%actors.Count];
        }
        private string BetaGrantName()
        { Player p=PhotonNetwork.CurrentRoom!=null ? PhotonNetwork.CurrentRoom.GetPlayer(betaGrantActor) : null; return p==null ? "Select player" : p.NickName; }
        private void ChangeBetaGrant(bool grant)
        {
            if (!IsDeveloper || betaSync==null || betaGrantActor<=0) return;
            bool queued=grant ? betaSync.Grant(betaGrantActor) : betaSync.Revoke(betaGrantActor);
            ShowNotice(queued ? (grant ? "Beta room access granted" : "Beta room access revoked") : "Beta access change unavailable",false);
        }
        private static float BetaClock { get { return (float)(PhotonNetwork.Time%3600); } }
        private static Color AppearanceColor(BetaAppearance state)
        { return state.Rainbow ? Color.HSVToRGB(Mathf.Repeat(BetaClock*state.RainbowSpeed,1),0.8f,1) : new Color(state.TintR,state.TintG,state.TintB); }
        private static bool SameVisual(BetaAppearance a,BetaAppearance b)
        {
            if (a==b) return true;
            return a!=null && b!=null && a.Enabled==b.Enabled && a.Style==b.Style && a.SourceActor==b.SourceActor && (a.UsePlayerColor && !a.Rainbow)==(b.UsePlayerColor && !b.Rainbow);
        }
        private VRRig AppearanceSource(int owner,BetaAppearance state)
        {
            int actor=state.SourceActor==0 ? owner : state.SourceActor;
            Player p=PhotonNetwork.CurrentRoom!=null ? PhotonNetwork.CurrentRoom.GetPlayer(actor) : null;
            if (p==null || p.IsInactive) return null;
            if (p.IsLocal) return GorillaTagger.Instance!=null ? GorillaTagger.Instance.offlineVRRig : null;
            NetPlayer net=NetPlayer.Get(p);
            return net!=null && net.IsValid && GorillaGameManager.instance!=null ? GorillaGameManager.instance.FindPlayerVRRig(net) : null;
        }
    }
}
