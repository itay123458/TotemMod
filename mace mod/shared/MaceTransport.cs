using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;

namespace MaceMod.Shared
{
    // Only player custom properties are used. The caller's predicate must require
    // a PRIVATE MODDED room; the transport separately enforces private membership.
    public sealed class MaceTransport : IInRoomCallbacks, IMatchmakingCallbacks, IDisposable
    {
        public const string StateKey = "mace.s2", HitKey = "mace.h2", ReceiptKey = "mace.r2";
        private readonly Func<bool> allowed;
        private readonly Func<byte[], byte[]> signState;
        private readonly Func<byte[], byte[], bool> verifyState;
        private readonly Action<int, MaceState> stateReceived;
        private readonly Action<int, MaceHit> hitReceived;
        private readonly Action<int, MaceReceipt> receiptReceived;
        private readonly Action<int> removed;
        private sealed class Remote
        {
            internal Player Player;
            internal MaceState State;
            internal int Revision, LastHit;
            internal bool Active;
            internal string Identity;
            internal byte[] Signature;
            internal double LastHitTime = double.NegativeInfinity;
            internal readonly HashSet<string> Retired = new HashSet<string>();
        }
        private sealed class Pending
        {
            internal MaceHit Hit;
            internal bool SentReceipt, ReceivedReceipt;
        }
        private readonly Dictionary<int, Remote> peers = new Dictionary<int, Remote>();
        private readonly Dictionary<int, Pending> pending = new Dictionary<int, Pending>();
        private Room room;
        private Player local;
        private string roomName;
        private MaceState own;
        private MaceState requested;
        private string ownIdentity;
        private byte[] ownSignature;
        private int revision, lastSentHit;
        private float nextState = float.NegativeInfinity, nextHit = float.NegativeInfinity;
        private bool disposed;
        public MaceTransport(Func<bool> allowed, Action<int, MaceState> stateReceived,
            Action<int, MaceHit> hitReceived, Action<int, MaceReceipt> receiptReceived, Action<int> removed,
            Func<byte[], byte[]> signState = null, Func<byte[], byte[], bool> verifyState = null)
        {
            if (allowed == null) throw new ArgumentNullException("allowed");
            this.allowed = allowed; this.stateReceived = stateReceived; this.hitReceived = hitReceived;
            this.receiptReceived = receiptReceived; this.removed = removed;
            this.signState = signState; this.verifyState = verifyState;
            PhotonNetwork.AddCallbackTarget(this);
        }
        private bool CanUse()
        {
            Room r = PhotonNetwork.CurrentRoom; Player p = PhotonNetwork.LocalPlayer;
            return !disposed && PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode && r != null && !r.IsVisible &&
                p != null && p.ActorNumber > 0 && !p.IsInactive && object.ReferenceEquals(r.GetPlayer(p.ActorNumber), p) && allowed();
        }
        private bool Bound()
        {
            if (!CanUse()) { if (room != null || own != null) Reset(); return false; }
            return object.ReferenceEquals(room, PhotonNetwork.CurrentRoom) && object.ReferenceEquals(local, PhotonNetwork.LocalPlayer) && roomName == room.Name;
        }
        private bool Member(Player p) { return p != null && p.ActorNumber > 0 && !p.IsInactive && room != null && object.ReferenceEquals(room.GetPlayer(p.ActorNumber), p); }
        private bool Fresh(double time)
        {
            double age = PhotonNetwork.Time - time;
            return !double.IsNaN(age) && !double.IsInfinity(age) && age >= 0d && age <= 2d;
        }
        private static bool Clock(float now) { return !float.IsNaN(now) && !float.IsInfinity(now); }
        public void Tick(MaceState state, float now)
        {
            if (!CanUse()) { if (room != null || own != null) Reset(); return; }
            if (!Bound())
            {
                Reset(); room = PhotonNetwork.CurrentRoom; local = PhotonNetwork.LocalPlayer; roomName = room.Name;
                // Persistent property snapshots are baselines, never actions.
                foreach (Player p in room.Players.Values) if (p != local) ReadState(p);
            }
            List<int> departed = null;
            foreach (KeyValuePair<int, Remote> entry in peers)
            {
                if (Member(entry.Value.Player)) continue;
                if (departed == null) departed = new List<int>();
                departed.Add(entry.Key);
            }
            if (departed != null) foreach (int actor in departed) Remove(actor);
            if (state != null && own != null && requested != null && requested.Equals(state) && ownIdentity == local.UserId && Clock(now)) return;
            MaceState validated;
            if (state == null || !MaceState.TryDecode(state.Encode(), out validated) || !Clock(now)) { ClearPublication(); own = null; return; }
            if (now < nextState) return;
            nextState = now + .25f;
            if (revision == int.MaxValue) return;
            MaceState published = state; byte[] signature = null;
            if (state.Equipped)
            {
                if (signState != null && Identity(local))
                    try { signature = signState(Canonical(local, state, revision + 1)); } catch { signature = null; }
                if (!Proof(local, state, revision + 1, signature))
                { published = new MaceState(state.Session, state.Enabled, false, state.Density, state.Knockback, state.WindBurst, state.Scale, state.Receive, state.Power); signature = null; }
            }
            Hashtable props = new Hashtable(); props[StateKey] = new object[] { 2, roomName, revision + 1, published.Encode(), signature };
            if (!local.SetCustomProperties(props)) return;
            if (own == null || own.Session != state.Session) { lastSentHit = 0; pending.Remove(local.ActorNumber); }
            own = published; requested = state; ownIdentity = local.UserId; ownSignature = signature == null ? null : (byte[])signature.Clone(); revision++;
        }
        private static bool Identity(Player player) { return player != null && !string.IsNullOrEmpty(player.UserId) && player.UserId.Length <= 128; }
        private byte[] Canonical(Player player, MaceState state, int stateRevision)
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write("TotemMaceDev/v2"); writer.Write(roomName); writer.Write(player.UserId); writer.Write(player.ActorNumber);
                writer.Write(state.Session); writer.Write(state.Enabled); writer.Write(state.Equipped); writer.Write(state.Density);
                writer.Write(state.Knockback); writer.Write(state.WindBurst); writer.Write(state.Scale); writer.Write(state.Receive); writer.Write(state.Power);
                writer.Write(stateRevision); writer.Flush(); return stream.ToArray();
            }
        }
        private bool Proof(Player player, MaceState state, int stateRevision, byte[] signature)
        {
            if (!state.Equipped) return signature == null;
            if (!Identity(player) || verifyState == null || signature == null || signature.Length == 0 || signature.Length > 1024) return false;
            try { return verifyState(Canonical(player, state, stateRevision), signature); } catch { return false; }
        }
        private static bool SameBytes(byte[] first, byte[] second)
        {
            if (first == null || second == null) return first == second;
            if (first.Length != second.Length) return false;
            for (int i = 0; i < first.Length; i++) if (first[i] != second[i]) return false;
            return true;
        }
        private bool MatchesFrame(Player player, object[] frame, MaceState expected, int expectedRevision, string identity, byte[] signature)
        {
            MaceState current;
            return player.UserId == identity && DecodeState(frame, out current) && (int)frame[2] == expectedRevision && current.Equals(expected) && SameBytes(frame[4] as byte[], signature);
        }
        private bool CurrentState(int actor, out MaceState state)
        {
            state = null;
            if (local != null && actor == local.ActorNumber)
            {
                if (!Member(local) || own == null || !local.CustomProperties.ContainsKey(StateKey) ||
                    !MatchesFrame(local, local.CustomProperties[StateKey] as object[], own, revision, ownIdentity, ownSignature)) return false;
                state = own; return true;
            }
            Remote remote;
            if (!peers.TryGetValue(actor, out remote) || !remote.Active || !Member(remote.Player)) return false;
            // Revalidate the actual current property, not a stale cached opt-in.
            object[] frame = remote.Player.CustomProperties.ContainsKey(StateKey) ? remote.Player.CustomProperties[StateKey] as object[] : null;
            if (!MatchesFrame(remote.Player, frame, remote.State, remote.Revision, remote.Identity, remote.Signature)) return false;
            state = remote.State; return true;
        }
        private bool DecodeState(object value, out MaceState state)
        {
            state = null; object[] frame = value as object[];
            return frame != null && frame.Length == 5 && MaceWire.Int(frame[0], 2, 2) && frame[1] is string && (string)frame[1] == roomName &&
                MaceWire.Int(frame[2], 1, int.MaxValue) && (frame[4] == null || frame[4] is byte[]) && MaceState.TryDecode(frame[3], out state);
        }
        private void ReadState(Player p)
        {
            if (!Member(p) || p == local) return;
            MaceState state; object value = p.CustomProperties.ContainsKey(StateKey) ? p.CustomProperties[StateKey] : null;
            if (!DecodeState(value, out state)) { Invalidate(p.ActorNumber); return; }
            object[] frame = (object[])value; Remote remote;
            if (peers.TryGetValue(p.ActorNumber, out remote) && !object.ReferenceEquals(remote.Player, p)) { Remove(p.ActorNumber); remote = null; }
            if (remote != null && remote.Active && MatchesFrame(p, frame, remote.State, remote.Revision, remote.Identity, remote.Signature)) return;
            if (!Proof(p, state, (int)frame[2], frame[4] as byte[])) { Invalidate(p.ActorNumber); return; }
            if (remote == null)
            {
                remote = new Remote { Player = p, State = state, Revision = (int)frame[2], Active = true }; peers[p.ActorNumber] = remote;
                MaceHit baseline;
                if (p.CustomProperties.ContainsKey(HitKey) && MaceHit.TryDecode(p.CustomProperties[HitKey], out baseline) && baseline.SourceSession == state.Session)
                    remote.LastHit = baseline.Sequence;
            }
            else
            {
                if (remote.Retired.Contains(state.Session) || (state.Session == remote.State.Session &&
                    ((int)frame[2] < remote.Revision || ((int)frame[2] == remote.Revision && remote.Active)))) return;
                if (remote.State.Session != state.Session)
                {
                    if (remote.Retired.Count >= 64) return;
                    remote.Retired.Add(remote.State.Session); remote.LastHit = 0; remote.LastHitTime = double.NegativeInfinity;
                    ClearPending(p.ActorNumber);
                }
                remote.State = state; remote.Revision = (int)frame[2]; remote.Active = true;
            }
            remote.Identity = p.UserId; remote.Signature = frame[4] == null ? null : (byte[])((byte[])frame[4]).Clone();
            if (stateReceived != null) stateReceived(p.ActorNumber, state);
        }
        private bool ValidHit(int sourceActor, MaceHit hit)
        {
            MaceState source, target;
            return hit != null && sourceActor != hit.TargetActor && Fresh(hit.Time) && CurrentState(sourceActor, out source) && source.Enabled && source.Equipped &&
                source.Session == hit.SourceSession && CurrentState(hit.TargetActor, out target) && target.Receive && target.Session == hit.TargetSession;
        }
        public bool SendHit(MaceHit hit, float now)
        {
            MaceHit valid;
            if (!Bound() || hit == null || !Clock(now) || now < nextHit || !MaceHit.TryDecode(hit.Encode(), out valid) ||
                !ValidHit(local.ActorNumber, hit) || hit.Sequence <= lastSentHit) return false;
            nextHit = now + .5f;
            Hashtable props = new Hashtable(); props[HitKey] = hit.Encode();
            if (!local.SetCustomProperties(props)) return false;
            lastSentHit = hit.Sequence; pending[local.ActorNumber] = new Pending { Hit = hit }; return true;
        }
        private void ReadHit(Player p)
        {
            if (!Member(p) || p == local) return;
            Remote remote; MaceHit hit;
            if (!peers.TryGetValue(p.ActorNumber, out remote) || !remote.Active || !p.CustomProperties.ContainsKey(HitKey) ||
                !MaceHit.TryDecode(p.CustomProperties[HitKey], out hit) || hit.SourceSession != remote.State.Session || hit.Sequence <= remote.LastHit) return;
            // Consume even rejected sequence numbers so stale/future requests cannot
            // become fresh actions later merely by replaying the same property.
            remote.LastHit = hit.Sequence;
            if (!ValidHit(p.ActorNumber, hit) || hit.Time - remote.LastHitTime < .5d) return;
            remote.LastHitTime = hit.Time; pending[p.ActorNumber] = new Pending { Hit = hit };
            if (hitReceived != null) hitReceived(p.ActorNumber, hit);
        }
        private bool MatchesReceipt(int targetActor, MaceReceipt receipt, out Pending request)
        {
            request = null; MaceState source, target;
            if (receipt == null || !Fresh(receipt.Time) || !pending.TryGetValue(receipt.SourceActor, out request)) return false;
            MaceHit hit = request.Hit;
            return hit.TargetActor == targetActor && hit.Sequence == receipt.Sequence && hit.SourceSession == receipt.SourceSession && hit.TargetSession == receipt.TargetSession &&
                receipt.Time >= hit.Time && Fresh(hit.Time) && CurrentState(receipt.SourceActor, out source) && source.Session == hit.SourceSession &&
                CurrentState(targetActor, out target) && target.Session == receipt.TargetSession && target.Receive;
        }
        public bool SendReceipt(MaceReceipt receipt)
        {
            MaceReceipt valid; Pending request;
            if (!Bound() || receipt == null || !MaceReceipt.TryDecode(receipt.Encode(), out valid) || !MatchesReceipt(local.ActorNumber, receipt, out request) || request.SentReceipt) return false;
            Hashtable props = new Hashtable(); props[ReceiptKey] = receipt.Encode();
            if (!local.SetCustomProperties(props)) return false;
            request.SentReceipt = request.ReceivedReceipt = true;
            // A master who is also the hit target must see its own acknowledged
            // result without waiting for a network echo of its own property.
            if (receiptReceived != null) receiptReceived(local.ActorNumber, receipt);
            return true;
        }
        private void ReadReceipt(Player p)
        {
            MaceReceipt receipt; Pending request;
            if (!Member(p) || p == local || !p.CustomProperties.ContainsKey(ReceiptKey) || !MaceReceipt.TryDecode(p.CustomProperties[ReceiptKey], out receipt) ||
                !MatchesReceipt(p.ActorNumber, receipt, out request) || request.ReceivedReceipt) return;
            request.ReceivedReceipt = true;
            if (receiptReceived != null) receiptReceived(p.ActorNumber, receipt);
        }
        public void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
        {
            if (!Bound() || !Member(targetPlayer) || changedProps == null || targetPlayer == local) return;
            if (changedProps.ContainsKey(StateKey)) ReadState(targetPlayer);
            if (changedProps.ContainsKey(HitKey)) ReadHit(targetPlayer);
            if (changedProps.ContainsKey(ReceiptKey)) ReadReceipt(targetPlayer);
        }
        private void ClearPending(int actor)
        {
            pending.Remove(actor);
            foreach (int source in new List<int>(pending.Keys)) if (pending[source].Hit.TargetActor == actor) pending.Remove(source);
        }
        private void Invalidate(int actor)
        {
            Remote remote;
            if (peers.TryGetValue(actor, out remote) && remote.Active)
            { remote.Active = false; ClearPending(actor); if (removed != null) removed(actor); }
        }
        private void Remove(int actor) { Invalidate(actor); peers.Remove(actor); }
        private void ClearPublication()
        {
            Player p = local;
            if (p == null || (!p.CustomProperties.ContainsKey(StateKey) && !p.CustomProperties.ContainsKey(HitKey) && !p.CustomProperties.ContainsKey(ReceiptKey))) return;
            Hashtable clear = new Hashtable(); clear[StateKey] = null; clear[HitKey] = null; clear[ReceiptKey] = null; p.SetCustomProperties(clear);
        }
        public void Reset()
        {
            if (peers.Count != 0) foreach (int actor in new List<int>(peers.Keys)) Remove(actor);
            ClearPublication(); pending.Clear(); own = requested = null; ownIdentity = null; ownSignature = null; room = null; local = null; roomName = null;
            revision = lastSentHit = 0; nextState = nextHit = float.NegativeInfinity;
        }
        public void Dispose() { if (disposed) return; Reset(); disposed = true; PhotonNetwork.RemoveCallbackTarget(this); }
        public void OnPlayerEnteredRoom(Player newPlayer) { if (Bound()) ReadState(newPlayer); }
        public void OnPlayerLeftRoom(Player otherPlayer) { if (otherPlayer != null) Remove(otherPlayer.ActorNumber); }
        public void OnLeftRoom() { Reset(); }
        public void OnPreLeavingRoom() { Reset(); }
        public void OnJoinedRoom() { Reset(); }
        public void OnMasterClientSwitched(Player newMasterClient) { Reset(); }
        public void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged) { if (!CanUse()) Reset(); }
        public void OnFriendListUpdate(List<FriendInfo> friendList) { }
        public void OnCreatedRoom() { }
        public void OnCreateRoomFailed(short returnCode, string message) { }
        public void OnJoinRoomFailed(short returnCode, string message) { }
        public void OnJoinRandomFailed(short returnCode, string message) { }
    }
}
