using System;
using System.Collections.Generic;
using GorillaLocomotion;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace MaceMod.Shared
{
    internal sealed class MaceRuntime
    {
        private sealed class Client
        {
            internal string Owner;
            internal Func<bool> Allowed,Save;
            internal Action Commit;
            internal Action<string> Notice;
            internal int Density,Knockback,Wind;
            internal float Scale=1,Power=1;
            internal bool Particles=true,Sound=true,Glint=true,Receive=true,Suspended;
        }
        private sealed class Peer
        {
            internal MaceState State;
            internal readonly MaceFall Fall=new MaceFall();
            internal VRRig Rig;
            internal float NextRig;
            internal GameObject Item;
            internal readonly Vector3[] Tips=new Vector3[12];
            internal readonly float[] Times=new float[12];
            internal int Cursor,Count;
            internal float LastHeight,LastSample,StillAt,LastReceive=float.NegativeInfinity;
            internal MaceHit Hit;
            internal int Resolved;
        }
        internal readonly IDictionary<string,object> Bus=new Dictionary<string,object>();
        private readonly Dictionary<string,Client> clients=new Dictionary<string,Client>();
        private readonly Dictionary<int,Peer> peers=new Dictionary<int,Peer>();
        private readonly MaceTransport transport;
        private readonly MaceVisuals visuals=new MaceVisuals();
        private readonly MaceFall fall=new MaceFall();
        private Room room;
        private string session;
        private MaceState published;
        private MaceHit outbound;
        private bool equipped,loaded,disposed;
        private GameObject held;
        private int frame=-1,toggleFrame=-1,sequence,windSequence,masterActor;
        private Vector3 lastTip;
        private bool hadTip;
        private float lastTipAt,nextSwing,nextLoad;
        private string result="Ready";

        internal MaceRuntime()
        {
            transport=new MaceTransport(Allowed,ReceiveState,ReceiveHit,ReceiveReceipt,RemovePeer);
            Bus["attach"]=new Func<string,Func<bool>,Func<bool>,Action<string>,Action,IDictionary<string,object>>(Attach);
        }
        private IDictionary<string,object> Attach(string owner,Func<bool> allowed,Func<bool> save,Action<string> notice,Action commit)
        {
            string token=Guid.NewGuid().ToString("N");
            Client client=new Client {Owner=owner,Allowed=allowed,Save=save,Notice=notice,Commit=commit}; clients.Add(token,client);
            return new Dictionary<string,object> {
                {"configure",new Action<bool,int,int,int,float,bool,bool,bool,bool,float>((e,d,k,w,s,p,a,g,r,f)=>Configure(client,e,d,k,w,s,p,a,g,r,f))},
                {"tick",new Action(Tick)},{"toggle",new Action(Toggle)},
                {"equipped",new Func<bool>(()=>equipped)}, {"status",new Func<string>(Status)},
                {"suspend",new Action<bool>(value=>client.Suspended=value)},
                {"detach",new Action(()=>Detach(token))}
            };
        }
        private static float Safe(float value,float fallback,float min,float max)
        { return float.IsNaN(value)||float.IsInfinity(value) ? fallback : Mathf.Clamp(value,min,max); }
        private void Configure(Client client,bool equip,int density,int knockback,int wind,float scale,bool particles,bool sound,bool glint,bool receive,float power)
        {
            equipped=equip;
            client.Density=Math.Max(0,Math.Min(5,density)); client.Knockback=Math.Max(0,Math.Min(2,knockback)); client.Wind=Math.Max(0,Math.Min(3,wind));
            client.Scale=Safe(scale,1,0.5f,1.5f); client.Power=Safe(power,1,0.25f,2);
            client.Particles=particles; client.Sound=sound; client.Glint=glint; client.Receive=receive;
        }
        private bool Active(Client client)
        { try { return client.Allowed!=null && client.Allowed(); } catch { return false; } }
        private Client Profile()
        {
            Client selected=null;
            foreach(Client client in clients.Values)
                if(Active(client) && (selected==null || Priority(client.Owner)>Priority(selected.Owner))) selected=client;
            return selected;
        }
        private static int Priority(string owner) { return owner=="totem-dev" ? 10 : owner=="mace" ? 5 : 0; }
        private bool Allowed()
        {
            return !disposed && Profile()!=null && PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode && PhotonNetwork.CurrentRoom!=null &&
                !PhotonNetwork.CurrentRoom.IsVisible && PhotonNetwork.LocalPlayer!=null &&
                GorillaGameManager.instance!=null && GorillaGameManager.instance.GetType()==typeof(GorillaTagManager);
        }
        private bool Suspended()
        { foreach(Client client in clients.Values) if(Active(client) && client.Suspended) return true; return false; }
        private bool Receiving()
        { Client profile=Profile(); return profile!=null && profile.Receive; }
        private string Status()
        { return !Allowed() ? "Private modded Infection / Tag required" : (equipped ? "Equipped | "+fall.Blocks.ToString("0.0")+" blocks | " : "Unequipped | ")+result; }
        private void Say(string message)
        {
            result=message;
            Client client=Profile(); if(client!=null && client.Notice!=null) client.Notice(message);
        }
        private void Toggle()
        {
            if(!Allowed() || toggleFrame==Time.frameCount) return;
            toggleFrame=Time.frameCount; equipped=!equipped;
            Say(equipped ? "Mace equipped" : "Mace unequipped");
        }
        private bool Load()
        {
            if(loaded) return true;
            if(Time.unscaledTime<nextLoad) return false; nextLoad=Time.unscaledTime+10;
            try { visuals.Load(); loaded=true; return true; }
            catch(Exception error) { Say("Mace assets: "+error.Message); return false; }
        }
        private void Tick()
        {
            if(disposed || frame==Time.frameCount) return;
            frame=Time.frameCount;
            if(!Allowed()) { Reset(); transport.Reset(); return; }
            if(room!=PhotonNetwork.CurrentRoom || masterActor!=PhotonNetwork.CurrentRoom.MasterClientId)
            { Reset(); transport.Reset(); room=PhotonNetwork.CurrentRoom; masterActor=room.MasterClientId; session=Guid.NewGuid().ToString("N"); }
            Client profile=Profile();
            bool visible=equipped && !Suspended();
            bool receive=Receiving();
            if(published==null || published.Equipped!=visible || published.Density!=profile.Density || published.Knockback!=profile.Knockback ||
                published.WindBurst!=profile.Wind || published.Scale!=profile.Scale || published.Receive!=receive || published.Power!=profile.Power)
                published=new MaceState(session,true,visible,profile.Density,profile.Knockback,profile.Wind,profile.Scale,receive,profile.Power);
            transport.Tick(published,Time.unscaledTime);
            GTPlayer player=GTPlayer.Instance;
            VRRig local=GorillaTagger.Instance!=null ? GorillaTagger.Instance.offlineVRRig : null;
            if(player==null || local==null || local.rightHandTransform==null) { Hide(); hadTip=false; return; }
            float size=Safe(player.scale,1,0.2f,4);
            Vector3 position=player.headCollider!=null ? player.headCollider.transform.position : local.transform.position;
            bool grounded=player.IsHandTouching(true)||player.IsHandTouching(false);
            if(!grounded && player.bodyCollider!=null)
            {
                Bounds bounds=player.bodyCollider.bounds;
                grounded=Physics.Raycast(bounds.center,Vector3.down,bounds.extents.y+0.035f*size,player.locomotionEnabledLayers,QueryTriggerInteraction.Ignore);
            }
            fall.Update(position.y,Time.unscaledTime,grounded,size);
            if(Load())
            {
                visuals.ParticlesEnabled=profile.Particles; visuals.SoundEnabled=profile.Sound; visuals.Tick(Time.unscaledTime);
                if(visible)
                {
                    if(held==null) held=visuals.Create(local.rightHandTransform,"Mace (held)");
                    visuals.Pose(held,local.rightHandTransform,profile.Scale,profile.Glint && profile.Density+profile.Knockback+profile.Wind>0,Time.unscaledTime);
                }
                else Hide();
            }
            foreach(KeyValuePair<int,Peer> entry in peers) SamplePeer(entry.Key,entry.Value,position,size);
            Vector3 tip=MaceVisuals.HeadPosition(local.rightHandTransform,profile.Scale);
            float now=Time.unscaledTime,dt=now-lastTipAt;
            if(visible && hadTip && dt>0.001f && dt<0.15f && now>=nextSwing && (tip-lastTip).magnitude/dt>=1.1f*size)
            {
                foreach(KeyValuePair<int,Peer> entry in peers)
                {
                    Peer peer=entry.Value;
                    if(peer.State==null || !peer.State.Enabled || !peer.State.Receive || peer.Rig==null) continue;
                    float targetSize=RigScale(peer.Rig);
                    Vector3 body=Body(peer.Rig,targetSize);
                    if((position-body).sqrMagnitude>Mathf.Pow(Mathf.Min(4.5f,3.2f*size),2)) continue;
                    float radius=0.55f*targetSize;
                    if(Distance(lastTip,tip,body)>radius*radius || Blocked(tip,body,player)) continue;
                    if(sequence==int.MaxValue) break;
                    Vector3 travel=tip-lastTip;
                    Vector3 contact=lastTip+travel*Mathf.Clamp01(travel.sqrMagnitude>0.000001f ? Vector3.Dot(body-lastTip,travel)/travel.sqrMagnitude : 0);
                    MaceHit hit=new MaceHit(session,entry.Key,peer.State.Session,sequence+1,PhotonNetwork.Time,fall.Blocks,contact.x,contact.y,contact.z);
                    outbound=hit;
                    if(transport.SendHit(hit,now)) { sequence++; nextSwing=now+0.8f; fall.Consume(); }
                    else outbound=null;
                    break;
                }
            }
            lastTip=tip; lastTipAt=now; hadTip=visible;
        }
        private static float RigScale(VRRig rig) { return Safe(Mathf.Abs(rig.transform.lossyScale.x),1,0.2f,4); }
        private static Vector3 Body(VRRig rig,float size) { return rig.transform.position-Vector3.up*(0.2f*size); }
        private static float Distance(Vector3 a,Vector3 b,Vector3 point)
        { return MaceMechanics.SegmentDistanceSquared(a.x,a.y,a.z,b.x,b.y,b.z,point.x,point.y,point.z); }
        private static bool Blocked(Vector3 tip,Vector3 body,GTPlayer player)
        { return player!=null && Physics.Linecast(tip,body,player.locomotionEnabledLayers,QueryTriggerInteraction.Ignore); }
        private void SamplePeer(int actor,Peer peer,Vector3 localPosition,float localScale)
        {
            Player player=room.GetPlayer(actor);
            if(player==null || player.IsInactive) return;
            float now=Time.unscaledTime;
            if(peer.Rig==null || now>=peer.NextRig)
            {
                peer.NextRig=now+0.5f; NetPlayer net=NetPlayer.Get(player);
                peer.Rig=net!=null && net.IsValid ? GorillaGameManager.instance.FindPlayerVRRig(net) : null;
            }
            if(peer.Rig==null) return;
            float size=RigScale(peer.Rig),height=peer.Rig.transform.position.y;
            bool steady=peer.LastSample>0 && Mathf.Abs(height-peer.LastHeight)<0.008f*size;
            if(!steady) peer.StillAt=now;
            peer.Fall.Update(height,now,steady && now-peer.StillAt>0.13f,size);
            peer.LastHeight=height; peer.LastSample=now;
            if(peer.State!=null && peer.Rig.rightHandTransform!=null)
            {
                Vector3 tip=MaceVisuals.HeadPosition(peer.Rig.rightHandTransform,peer.State.Scale);
                int latest=(peer.Cursor+peer.Tips.Length-1)%peer.Tips.Length;
                if(peer.Count==0 || now-peer.Times[latest]>=0.033f)
                { peer.Tips[peer.Cursor]=tip; peer.Times[peer.Cursor]=now; peer.Cursor=(peer.Cursor+1)%peer.Tips.Length; peer.Count=Math.Min(peer.Tips.Length,peer.Count+1); }
                bool visible=loaded && peer.State.Enabled && peer.State.Equipped && (localPosition-peer.Rig.transform.position).sqrMagnitude<225*localScale*localScale;
                if(visible)
                {
                    if(peer.Item==null) peer.Item=visuals.Create(peer.Rig.rightHandTransform,"Friend mace");
                    visuals.Pose(peer.Item,peer.Rig.rightHandTransform,peer.State.Scale,peer.State.Density+peer.State.Knockback+peer.State.WindBurst>0,now);
                }
                else if(peer.Item!=null) { UnityEngine.Object.Destroy(peer.Item); peer.Item=null; }
            }
        }
        private void ReceiveState(int actor,MaceState state)
        {
            if(PhotonNetwork.LocalPlayer==null || actor==PhotonNetwork.LocalPlayer.ActorNumber) return;
            Peer peer;
            if(!peers.TryGetValue(actor,out peer)) { peer=new Peer(); peers.Add(actor,peer); }
            if(peer.State!=null && peer.State.Session!=state.Session) { peer.Fall.Reset(); peer.Hit=null; peer.Resolved=0; peer.Count=0; }
            peer.State=state;
        }
        private void RemovePeer(int actor)
        { Peer peer; if(peers.TryGetValue(actor,out peer)) { if(peer.Item!=null) UnityEngine.Object.Destroy(peer.Item); peers.Remove(actor); } }
        private bool Validate(int sender,MaceHit hit,out Peer source,out float blocks)
        {
            source=null; blocks=0;
            if(!Allowed() || room==null || hit==null || hit.TargetActor==sender || GorillaTagger.Instance==null || GorillaTagger.Instance.offlineVRRig==null) return false;
            Player senderPlayer=room.GetPlayer(sender),target=room.GetPlayer(hit.TargetActor);
            if(senderPlayer==null || target==null || senderPlayer.IsInactive || target.IsInactive) return false;
            MaceState state;
            VRRig rig;
            float observed;
            if(senderPlayer.IsLocal)
            { state=published; rig=GorillaTagger.Instance.offlineVRRig; observed=outbound==hit ? hit.Blocks : fall.Blocks; }
            else
            {
                if(!peers.TryGetValue(sender,out source)) return false;
                state=source.State; rig=source.Rig; observed=source.Fall.Blocks;
            }
            if(state==null || !state.Enabled || !state.Equipped || state.Session!=hit.SourceSession || rig==null || rig.rightHandTransform==null) return false;
            VRRig targetRig;
            MaceState targetState;
            if(target.IsLocal) { targetState=published; targetRig=GorillaTagger.Instance.offlineVRRig; }
            else { Peer victim; if(!peers.TryGetValue(target.ActorNumber,out victim)) return false; targetState=victim.State; targetRig=victim.Rig; }
            if(targetRig==null || targetState==null || !targetState.Enabled || !targetState.Receive || targetState.Session!=hit.TargetSession) return false;
            float size=RigScale(rig),targetSize=RigScale(targetRig);
            Vector3 point=new Vector3(hit.X,hit.Y,hit.Z),body=Body(targetRig,targetSize);
            if((rig.transform.position-body).sqrMagnitude>Mathf.Pow(Mathf.Min(4.5f,3.2f*size),2) || (point-body).sqrMagnitude>Mathf.Pow(0.85f*targetSize,2) ||
                Blocked(point,body,GTPlayer.Instance) || Blocked(rig.transform.position,point,GTPlayer.Instance)) return false;
            bool near=false;
            if(senderPlayer.IsLocal) near=(point-MaceVisuals.HeadPosition(rig.rightHandTransform,state.Scale)).sqrMagnitude<0.64f*size*size;
            else for(int i=0;i<source.Count;i++)
            {
                int previous=(i+source.Tips.Length-1)%source.Tips.Length;
                float dt=source.Times[i]-source.Times[previous];
                if(dt>0.005f && dt<0.2f && (source.Tips[i]-source.Tips[previous]).magnitude/dt>=0.5f*size &&
                    Time.unscaledTime-source.Times[i]<=0.55f && Time.unscaledTime-source.Times[previous]<=0.55f &&
                    Distance(source.Tips[previous],source.Tips[i],point)<0.49f*size*size) { near=true; break; }
            }
            if(!near) return false;
            blocks=Mathf.Min(hit.Blocks,Mathf.Clamp(observed+0.75f,0,64));
            return true;
        }
        private static bool Tagged(Player player)
        { NetPlayer net=player!=null ? NetPlayer.Get(player) : null; return net!=null && net.IsValid && GorillaGameManager.instance.LocalIsTagged(net); }
        private void ReceiveHit(int sender,MaceHit hit)
        {
            Peer source; float blocks;
            if(!Validate(sender,hit,out source,out blocks)) return;
            if(source!=null) source.Hit=hit;
            if(hit.TargetActor!=PhotonNetwork.LocalPlayer.ActorNumber) return;
            if(source==null || Time.unscaledTime-source.LastReceive<0.7f) return;
            source.LastReceive=Time.unscaledTime;
            bool attackerTagged=Tagged(room.GetPlayer(sender)),targetTagged=Tagged(PhotonNetwork.LocalPlayer);
            if(MaceCombatRules.Resolve(true,true,attackerTagged,targetTagged,false)==0) return;
            bool saved=false;
            Client defender=null;
            if(attackerTagged && !targetTagged)
                foreach(Client client in clients.Values)
                    if(Active(client) && client.Save!=null && client.Commit!=null) { try { if(client.Save()) { saved=true; defender=client; break; } } catch { } }
            int outcome=MaceCombatRules.Resolve(true,true,attackerTagged,targetTagged,saved);
            MaceReceipt receipt=new MaceReceipt(sender,hit.SourceSession,hit.Sequence,session,PhotonNetwork.Time,outcome);
            // A receipt must be queued before movement. Dropped/unpublished requests
            // never change infection or another client's body.
            if(!transport.SendReceipt(receipt)) return;
            if(defender!=null) defender.Commit();
            if(outcome==2)
            {
                Vector3 away=GorillaTagger.Instance.offlineVRRig.transform.position-source.Rig.transform.position;
                away.y=0; if(away.sqrMagnitude<0.01f) away=source.Rig.transform.forward;
                float speed=MaceMechanics.Knockback(blocks,source.State.Density,source.State.Knockback,source.State.Power);
                ApplyVelocity(away.normalized*speed+Vector3.up*Mathf.Min(6,speed*0.32f));
            }
            Say(outcome==1 ? "Totem saved you from the mace" : outcome==2 ? "Mace knockback" : "Mace tag sent to host");
        }
        private void ReceiveReceipt(int targetActor,MaceReceipt receipt)
        {
            if(!Allowed() || room==null) return;
            MaceHit hit;
            Peer source=null;
            if(receipt.SourceActor==PhotonNetwork.LocalPlayer.ActorNumber) hit=outbound;
            else { if(!peers.TryGetValue(receipt.SourceActor,out source)) return; hit=source.Hit; }
            if(hit==null || hit.Sequence!=receipt.Sequence || hit.TargetActor!=targetActor || hit.SourceSession!=receipt.SourceSession || hit.TargetSession!=receipt.TargetSession) return;
            if(source!=null && source.Resolved>=hit.Sequence) return;
            if(source!=null) source.Resolved=hit.Sequence;
            if(receipt.Outcome==3 && PhotonNetwork.IsMasterClient && NetworkSystem.Instance!=null && NetworkSystem.Instance.IsMasterClient)
            {
                Peer ignored; float blocks;
                if(Validate(receipt.SourceActor,hit,out ignored,out blocks) && Tagged(room.GetPlayer(receipt.SourceActor)) && !Tagged(room.GetPlayer(targetActor)))
                {
                    NetPlayer attacker=NetPlayer.Get(room.GetPlayer(receipt.SourceActor)),victim=NetPlayer.Get(room.GetPlayer(targetActor));
                    ((GorillaTagManager)GorillaGameManager.instance).ReportTag(victim,attacker);
                }
            }
            if(receipt.SourceActor==PhotonNetwork.LocalPlayer.ActorNumber && windSequence<hit.Sequence)
            {
                windSequence=hit.Sequence;
                if(receipt.Outcome!=1 && published!=null)
                {
                    float lift=MaceMechanics.WindLift(hit.Blocks,published.WindBurst);
                    if(lift>0) ApplyVelocity(Vector3.up*lift);
                }
                Say(receipt.Outcome==1 ? "Their totem saved them" : receipt.Outcome==2 ? "Mace hit" : "Mace tag requested");
            }
            if(loaded) visuals.Impact(new Vector3(hit.X,hit.Y,hit.Z),receipt.Outcome==1,hit.Blocks);
        }
        private static void ApplyVelocity(Vector3 impulse)
        {
            GTPlayer player=GTPlayer.Instance;
            if(player==null || player.playerRigidBody==null) return;
            float size=Safe(player.scale,1,0.2f,4);
            player.playerRigidBody.linearVelocity=Vector3.ClampMagnitude(player.playerRigidBody.linearVelocity+impulse*size,20*size);
        }
        private void Hide() { if(held!=null) UnityEngine.Object.Destroy(held); held=null; }
        private void Reset()
        {
            if(room==null && session==null && held==null && peers.Count==0) return;
            if(loaded) visuals.ClearEffects();
            Hide(); foreach(Peer peer in peers.Values) if(peer.Item!=null) UnityEngine.Object.Destroy(peer.Item);
            peers.Clear(); fall.Reset(); room=null; session=null; published=null; outbound=null; sequence=windSequence=masterActor=0; hadTip=false; equipped=false; nextSwing=0;
        }
        private void Detach(string token)
        {
            clients.Remove(token); if(clients.Count!=0 || disposed) return;
            disposed=true; Reset(); transport.Dispose(); visuals.Dispose();
            if(ReferenceEquals(AppDomain.CurrentDomain.GetData(MaceClient.Slot),Bus)) AppDomain.CurrentDomain.SetData(MaceClient.Slot,null);
        }
    }
}
