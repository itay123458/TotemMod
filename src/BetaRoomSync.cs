using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;

namespace TotemMod
{
    // Developer signatures authorize a specific room member's fresh challenge.
    // Player properties carry the policy, but never confer trust themselves.
    internal sealed class BetaRoomSync : IInRoomCallbacks, IMatchmakingCallbacks, IDisposable
    {
        internal const string ChallengeKey = "totemmod.gtag.rhys/beta-challenge/v1";
        internal const string PermitsKey = "totemmod.gtag.rhys/beta-permits/v1";
        internal const string AppearanceKey = "totemmod.gtag.rhys/beta-appearance/v1";
        private readonly Func<bool> allowed, isDeveloper;
        private readonly Func<string> authority;
        private readonly Func<byte[], byte[]> sign;
        private readonly Func<byte[], byte[], bool> verify;
        private readonly Func<bool> accountLocalAccess;
        private readonly Func<int, bool> accountRemoteAccess;
        private readonly Action<int, BetaAppearance> received;
        private readonly Action<int> removed;
        private readonly Action<bool> localAccessChanged;
        private Room room;
        private Player local;
        private string roomAuthority, challenge;
        private bool disposed, challengeDirty, policyDirty, localAccess;
        private float nextSend = float.NegativeInfinity, nextAuto = float.NegativeInfinity;
        private int sequence;
        private BetaAppearance sentAppearance;
        private string sentSession;
        private readonly Dictionary<int, object[]> ownPermits = new Dictionary<int, object[]>();
        private readonly Dictionary<int, Dictionary<int, Permit>> policies = new Dictionary<int, Dictionary<int, Permit>>();
        private readonly Dictionary<string, int> revisions = new Dictionary<string, int>();
        private readonly Dictionary<int, Dictionary<string, int>> seen = new Dictionary<int, Dictionary<string, int>>();
        private readonly HashSet<int> visible = new HashSet<int>();
        private sealed class Permit
        {
            public Player Owner, Target;
            public string OwnerChallenge, TargetChallenge;
            public string OwnerId, TargetId;
            public bool Granted;
        }

        public BetaRoomSync(Func<bool> allowed, Func<bool> isDeveloper, Func<string> authority,
            Func<byte[], byte[]> sign, Func<byte[], byte[], bool> verify, Action<int, BetaAppearance> received,
            Action<int> removed, Action<bool> localAccessChanged, Func<bool> accountLocalAccess = null,
            Func<int, bool> accountRemoteAccess = null)
        {
            this.allowed = allowed; this.isDeveloper = isDeveloper; this.authority = authority;
            this.sign = sign; this.verify = verify; this.received = received; this.removed = removed;
            this.accountLocalAccess = accountLocalAccess; this.accountRemoteAccess = accountRemoteAccess;
            this.localAccessChanged = localAccessChanged; PhotonNetwork.AddCallbackTarget(this);
        }

        public bool LocalAccess { get { return AccessFor(PhotonNetwork.LocalPlayer == null ? 0 : PhotonNetwork.LocalPlayer.ActorNumber); } }
        private bool RoomAllowed()
        {
            return !disposed && allowed != null && allowed() && PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode &&
                PhotonNetwork.CurrentRoom != null && !PhotonNetwork.CurrentRoom.IsVisible && PhotonNetwork.LocalPlayer != null;
        }
        private bool Bound()
        {
            return RoomAllowed() && room == PhotonNetwork.CurrentRoom && local == PhotonNetwork.LocalPlayer &&
                !string.IsNullOrEmpty(roomAuthority) && authority != null && authority() == roomAuthority;
        }
        private bool Member(Player p) { return room != null && p != null && !p.IsInactive && room.GetPlayer(p.ActorNumber) == p; }
        private static bool Nonce(object value)
        {
            string s = value as string; Guid parsed;
            return s != null && s.Length == 32 && Guid.TryParseExact(s, "N", out parsed);
        }
        private static string Challenge(Player p)
        {
            if (p == null || !p.CustomProperties.ContainsKey(ChallengeKey) || !Nonce(p.CustomProperties[ChallengeKey])) return null;
            return (string)p.CustomProperties[ChallengeKey];
        }
        private static bool NativeIdentity(Player p)
        {
            return p != null && !string.IsNullOrEmpty(p.UserId) && p.UserId.Length <= 128;
        }
        public bool AccessFor(int actor)
        {
            if (!Bound()) return false;
            Player target = room.GetPlayer(actor);
            if (!Member(target)) return false;
            bool granted = false;
            foreach (Dictionary<int, Permit> policy in policies.Values)
            {
                Permit p;
                if (!policy.TryGetValue(actor, out p) || p.Target != target || !Member(p.Owner) ||
                    Challenge(p.Owner) != p.OwnerChallenge || p.Owner.UserId != p.OwnerId || target.UserId != p.TargetId) continue;
                // A current developer's signed room denial also applies to
                // permanent accounts, and disappears with that owner/session.
                if (!p.Granted) return false;
                if (Challenge(target) != p.TargetChallenge) continue;
                granted = true;
            }
            // Account callbacks inspect trusted local identity/server-published
            // UserId. No identity claim in a custom property is consulted here.
            try
            {
                if (target == local && accountLocalAccess != null && accountLocalAccess()) return true;
                if (target != local && accountRemoteAccess != null && accountRemoteAccess(actor)) return true;
            }
            catch { }
            return granted;
        }

        public void Tick(BetaAppearance appearance, string coreSession, float now)
        {
            if (disposed) return;
            if (!RoomAllowed()) { Reset(); return; }
            string currentAuthority = authority == null ? null : authority();
            if (string.IsNullOrEmpty(currentAuthority) || currentAuthority.Length > 160) { Reset(); return; }
            if (room != PhotonNetwork.CurrentRoom || local != PhotonNetwork.LocalPlayer || roomAuthority != currentAuthority)
            {
                Reset(); room = PhotonNetwork.CurrentRoom; local = PhotonNetwork.LocalPlayer; roomAuthority = currentAuthority;
                challenge = Guid.NewGuid().ToString("N"); challengeDirty = true;
                foreach (Player p in room.Players.Values) ReadPolicy(p);
                RefreshAccess();
            }
            if (float.IsNaN(now) || float.IsInfinity(now)) return;
            RefreshLocalAccess();
            object[] ownPermit;
            bool ownGranted=ownPermits.TryGetValue(local.ActorNumber,out ownPermit) && (string)ownPermit[1]==challenge && (bool)ownPermit[3];
            if (now >= nextAuto && isDeveloper != null && isDeveloper() && !ownGranted && Challenge(local) == challenge)
            { Grant(local.ActorNumber); nextAuto = now + .5f; }
            if (now < nextSend) return;
            bool publishAppearance = LocalAccess && appearance != null && appearance.IsValid && Nonce(coreSession) &&
                (!appearance.SameSettings(sentAppearance) || sentSession != coreSession);
            bool removeAppearance=(!LocalAccess || appearance == null || !appearance.IsValid || !Nonce(coreSession)) && local.CustomProperties.ContainsKey(AppearanceKey);
            if (!challengeDirty && !policyDirty && !publishAppearance && !removeAppearance) return;
            Hashtable change = new Hashtable();
            if (challengeDirty) change[ChallengeKey] = challenge;
            if (policyDirty) change[PermitsKey] = OwnPolicy();
            if (publishAppearance && sequence < int.MaxValue)
                change[AppearanceKey] = new object[] { 1, roomAuthority, challenge, coreSession, sequence + 1, appearance.Encode() };
            else if (removeAppearance) change[AppearanceKey] = null;
            if (change.Count == 0) return;
            nextSend = now + .5f;
            if (!local.SetCustomProperties(change)) return;
            challengeDirty = false;
            if (policyDirty) { policyDirty = false; ReadPolicy(local); RefreshAccess(); }
            if (publishAppearance) { sequence++; sentAppearance = appearance; sentSession = coreSession; }
            else if (change.ContainsKey(AppearanceKey)) { sentAppearance = null; sentSession = null; }
        }

        public bool Grant(int actor) { return SetPermit(actor, true); }
        public bool Revoke(int actor) { return SetPermit(actor, false); }
        private bool SetPermit(int actor, bool grant)
        {
            if (!Bound() || isDeveloper == null || !isDeveloper() || sign == null || !Member(local) || !NativeIdentity(local) || Challenge(local) != challenge) return false;
            Player target = room.GetPlayer(actor); string targetChallenge = Challenge(target);
            if (!Member(target) || !NativeIdentity(target) || targetChallenge == null || (ownPermits.Count >= 64 && !ownPermits.ContainsKey(actor))) return false;
            object[] previous;
            int revision = 1;
            if (ownPermits.TryGetValue(actor, out previous))
            {
                if ((string)previous[1] == targetChallenge && (bool)previous[3] == grant) return true;
                if ((int)previous[2] == int.MaxValue) return false;
                revision = (int)previous[2] + 1;
            }
            byte[] signature;
            try { signature = sign(Canonical(local.ActorNumber, challenge, actor, targetChallenge, revision, grant)); }
            catch { return false; }
            if (signature == null || signature.Length == 0 || signature.Length > 512) return false;
            ownPermits[actor] = new object[] { actor, targetChallenge, revision, grant, signature };
            policyDirty = true;
            // Authorization becomes observable only after the signed policy is
            // published, allowing failed sends to retry without granting locally.
            return true;
        }
        private object[] OwnPolicy()
        {
            object[] entries = new object[ownPermits.Count]; int i = 0;
            foreach (object[] permit in ownPermits.Values) entries[i++] = permit;
            return new object[] { 1, local.ActorNumber, challenge, entries };
        }
        private byte[] Canonical(int owner, string ownerChallenge, int actor, string targetChallenge, int revision, bool grant)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8))
                {
                    writer.Write("TotemMod.BetaPermit.v1"); writer.Write(room.Name ?? ""); writer.Write(roomAuthority);
                    writer.Write(owner); writer.Write(room.GetPlayer(owner).UserId); writer.Write(ownerChallenge);
                    writer.Write(actor); writer.Write(room.GetPlayer(actor).UserId); writer.Write(targetChallenge);
                    writer.Write(revision); writer.Write(grant); writer.Flush(); return stream.ToArray();
                }
            }
        }
        private void ReadPolicy(Player owner)
        {
            if (!Member(owner) || !NativeIdentity(owner)) return;
            if (!Bound() || verify == null || !owner.CustomProperties.ContainsKey(PermitsKey)) return;
            object[] map = owner.CustomProperties[PermitsKey] as object[];
            if (map == null || map.Length != 4 || !(map[0] is int) || (int)map[0] != 1 || !(map[1] is int) ||
                (int)map[1] != owner.ActorNumber || !Nonce(map[2]) || (string)map[2] != Challenge(owner)) return;
            object[] entries = map[3] as object[];
            if (entries == null || entries.Length > 64) return;
            Dictionary<int, Permit> parsed = new Dictionary<int, Permit>();
            Dictionary<string, int> nextRevisions = new Dictionary<string, int>();
            HashSet<int> actors = new HashSet<int>();
            foreach (object entry in entries)
            {
                object[] e = entry as object[];
                if (e == null || e.Length != 5 || !(e[0] is int) || !Nonce(e[1]) || !(e[2] is int) ||
                    (int)e[2] <= 0 || !(e[3] is bool) || !(e[4] is byte[])) return;
                int actor = (int)e[0], revision = (int)e[2]; byte[] signature = (byte[])e[4];
                if (signature.Length == 0 || signature.Length > 512 || !actors.Add(actor)) return;
                Player target = room.GetPlayer(actor);
                if (!Member(target) || !NativeIdentity(target) || ((bool)e[3] && Challenge(target) != (string)e[1])) continue;
                string stamp = owner.ActorNumber + ":" + (string)map[2] + ":" + actor + ":" + target.UserId;
                int last; if (revisions.TryGetValue(stamp, out last) && revision < last) continue;
                bool valid;
                try { valid = verify(Canonical(owner.ActorNumber, (string)map[2], actor, (string)e[1], revision, (bool)e[3]), signature); }
                catch { valid = false; }
                if (!valid) return;
                // Equal revisions are valid retained entries in the full map.
                nextRevisions[stamp] = revision;
                parsed[actor] = new Permit { Owner = owner, Target = target, OwnerChallenge = (string)map[2],
                    TargetChallenge = (string)e[1], OwnerId = owner.UserId, TargetId = target.UserId, Granted = (bool)e[3] };
            }
            // Only signed updates can change an existing decision. An omitted
            // entry, deleted property, malformed map, or stale replay cannot
            // erase a denial and expose the permanent-account fallback.
            // Commit after every applicable entry has passed verification.
            Dictionary<int, Permit> current;
            if (!policies.TryGetValue(owner.ActorNumber, out current))
            { current = new Dictionary<int, Permit>(); policies[owner.ActorNumber] = current; }
            foreach (KeyValuePair<int, Permit> entry in parsed) current[entry.Key] = entry.Value;
            foreach (KeyValuePair<string, int> entry in nextRevisions) revisions[entry.Key] = entry.Value;
        }

        private void RefreshAccess()
        {
            RefreshLocalAccess();
            foreach (int actor in new List<int>(visible)) if (!AccessFor(actor)) Remove(actor);
            if (room != null) foreach (Player p in room.Players.Values) if (p != local && AccessFor(p.ActorNumber)) ReadAppearance(p);
        }
        private void RefreshLocalAccess()
        {
            bool next = LocalAccess;
            if (next != localAccess)
            {
                localAccess = next; sentAppearance = null; sentSession = null;
                if (localAccessChanged != null) localAccessChanged(next);
            }
        }
        private void Remove(int actor) { if (visible.Remove(actor) && removed != null) removed(actor); }
        private void ReadAppearance(Player sender)
        {
            if (!Bound() || sender == local || !Member(sender)) return;
            if (!AccessFor(sender.ActorNumber) || !sender.CustomProperties.ContainsKey(AppearanceKey)) { Remove(sender.ActorNumber); return; }
            object[] d = sender.CustomProperties[AppearanceKey] as object[];
            BetaAppearance appearance;
            if (d == null || d.Length != 6 || !(d[0] is int) || (int)d[0] != 1 || !(d[1] is string) ||
                (string)d[1] != roomAuthority || !Nonce(d[2]) || (string)d[2] != Challenge(sender) || !Nonce(d[3]) ||
                !(d[4] is int) || (int)d[4] <= 0 || !BetaAppearance.TryDecode(d[5], out appearance))
            { Remove(sender.ActorNumber); return; }
            Dictionary<string, int> sessions;
            if (!seen.TryGetValue(sender.ActorNumber, out sessions)) { sessions = new Dictionary<string, int>(); seen[sender.ActorNumber] = sessions; }
            int previous;
            if (sessions.TryGetValue((string)d[3], out previous) && ((int)d[4] < previous || ((int)d[4] == previous && visible.Contains(sender.ActorNumber)))) return;
            if (!sessions.ContainsKey((string)d[3]) && sessions.Count >= 32) { Remove(sender.ActorNumber); return; }
            sessions[(string)d[3]] = (int)d[4]; visible.Add(sender.ActorNumber);
            if (received != null) received(sender.ActorNumber, appearance.WithSession((string)d[3]));
        }

        public void Reset()
        {
            if (visible.Count != 0) foreach (int actor in new List<int>(visible)) Remove(actor);
            if (localAccess && localAccessChanged != null) localAccessChanged(false);
            localAccess = false; room = null; local = null; roomAuthority = null; challenge = null;
            ownPermits.Clear(); policies.Clear(); revisions.Clear(); seen.Clear();
            sequence = 0; sentAppearance = null; sentSession = null; challengeDirty = policyDirty = false;
            nextSend = nextAuto = float.NegativeInfinity;
            Player p = PhotonNetwork.LocalPlayer;
            if (p != null && (p.CustomProperties.ContainsKey(ChallengeKey) || p.CustomProperties.ContainsKey(PermitsKey) || p.CustomProperties.ContainsKey(AppearanceKey)))
            {
                Hashtable clear = new Hashtable(); clear[ChallengeKey] = null; clear[PermitsKey] = null; clear[AppearanceKey] = null;
                p.SetCustomProperties(clear);
            }
        }
        public void Dispose() { if (disposed) return; Reset(); disposed = true; PhotonNetwork.RemoveCallbackTarget(this); }
        public void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
        {
            if (!Bound() || !Member(targetPlayer) || changedProps == null) return;
            if (changedProps.ContainsKey(ChallengeKey))
            {
                seen.Remove(targetPlayer.ActorNumber); Remove(targetPlayer.ActorNumber);
                foreach (Player p in room.Players.Values) ReadPolicy(p);
                RefreshAccess();
            }
            else if (changedProps.ContainsKey(PermitsKey)) { ReadPolicy(targetPlayer); RefreshAccess(); }
            if (changedProps.ContainsKey(AppearanceKey)) ReadAppearance(targetPlayer);
        }
        public void OnPlayerEnteredRoom(Player newPlayer) { if (!Bound()) return; ReadPolicy(newPlayer); RefreshAccess(); }
        public void OnPlayerLeftRoom(Player otherPlayer)
        {
            if (otherPlayer == null) return;
            policies.Remove(otherPlayer.ActorNumber); seen.Remove(otherPlayer.ActorNumber); ownPermits.Remove(otherPlayer.ActorNumber);
            Remove(otherPlayer.ActorNumber); RefreshAccess();
        }
        public void OnLeftRoom() { Reset(); }
        public void OnPreLeavingRoom() { Reset(); }
        public void OnJoinedRoom() { Reset(); }
        public void OnMasterClientSwitched(Player newMasterClient) { Reset(); }
        public void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged) { }
        public void OnFriendListUpdate(List<FriendInfo> friendList) { }
        public void OnCreatedRoom() { }
        public void OnCreateRoomFailed(short returnCode, string message) { }
        public void OnJoinRoomFailed(short returnCode, string message) { }
        public void OnJoinRandomFailed(short returnCode, string message) { }
    }
}
