using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using TotemMod;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;

// This executable has its own Photon fixtures. It is compiled separately from
// SyncTests, and exercises the production transport without a live game/room.
namespace ExitGames.Client.Photon { public sealed class Hashtable : Dictionary<object, object> { public static int Created; public Hashtable() { Created++; } } }
namespace Photon.Realtime
{
    public interface IInRoomCallbacks { }
    public interface IMatchmakingCallbacks { }
    public sealed class FriendInfo { }
    public sealed class Room
    {
        public string Name = "beta-test-room";
        public bool IsVisible;
        public readonly Dictionary<int, Player> Players = new Dictionary<int, Player>();
        public Player GetPlayer(int actor) { Player p; return Players.TryGetValue(actor, out p) ? p : null; }
    }
    public sealed class Player
    {
        public int ActorNumber;
        public string UserId;
        public bool IsInactive;
        public bool FailSend;
        public readonly Hashtable CustomProperties = new Hashtable();
        public readonly List<Hashtable> Sent = new List<Hashtable>();
        public bool SetCustomProperties(Hashtable change)
        {
            if (FailSend) return false;
            Hashtable copy = new Hashtable();
            foreach (KeyValuePair<object, object> e in change)
            {
                copy[e.Key] = e.Value;
                if (e.Value == null) CustomProperties.Remove(e.Key); else CustomProperties[e.Key] = e.Value;
            }
            Sent.Add(copy); return true;
        }
    }
}
namespace Photon.Pun
{
    public static class PhotonNetwork
    {
        public static Room CurrentRoom;
        public static Player LocalPlayer;
        public static bool InRoom, OfflineMode;
        public static int Targets;
        public static void AddCallbackTarget(object target) { Targets++; }
        public static void RemoveCallbackTarget(object target) { Targets--; }
    }
}
namespace BetaSyncTestFixtures
{
    internal static class BetaSyncTests
    {
        private static int checks, verifications;
        private static string authority = "1:11111111111111111111111111111111";
        private static bool allowed = true;
        private static readonly string CoreSession = "22222222222222222222222222222222";
        private static readonly BetaAppearance Appearance = new BetaAppearance(true, 2, 0, true, true,
            .2f, .3f, .4f, 1f, 12f, .01f, false, .1f, 0f, 20f, -10f);
        private static void Check(bool value, string label) { checks++; if (!value) throw new Exception(label); }
        private static byte[] Sign(byte[] bytes) { using (SHA256 hash = SHA256.Create()) return hash.ComputeHash(bytes); }
        private static bool Verify(byte[] bytes, byte[] signature)
        {
            verifications++; byte[] expected = Sign(bytes);
            if (expected.Length != signature.Length) return false;
            for (int i = 0; i < expected.Length; i++) if (expected[i] != signature[i]) return false;
            return true;
        }
        private sealed class Peer
        {
            public readonly Player Player;
            public readonly BetaRoomSync Sync;
            public readonly Dictionary<int, BetaAppearance> Remote = new Dictionary<int, BetaAppearance>();
            public int AccessChanges;
            public int Receives;
            public Peer(int actor, bool owner, Func<bool> localAccount = null, Func<int, bool> remoteAccount = null)
            {
                Player = new Player { ActorNumber = actor, UserId = "server-account-" + actor }; PhotonNetwork.CurrentRoom.Players.Add(actor, Player);
                Use(); Sync = new BetaRoomSync(() => allowed, () => owner, () => authority, owner ? (Func<byte[], byte[]>)Sign : null,
                    Verify, (a, s) => { Receives++; Remote[a] = s; }, a => Remote.Remove(a), value => AccessChanges++, localAccount, remoteAccount);
            }
            public void Use() { PhotonNetwork.LocalPlayer = Player; }
            public void Tick(float now) { Use(); Sync.Tick(Appearance, CoreSession, now); }
            public void Event(Player p, string key) { Use(); Hashtable h = new Hashtable(); h[key] = true; Sync.OnPlayerPropertiesUpdate(p, h); }
            public bool Access(int actor) { Use(); return Sync.AccessFor(actor); }
        }
        private static void Model()
        {
            BetaAppearance decoded; object[] d = Appearance.Encode();
            Check(BetaAppearance.TryDecode(d, out decoded) && decoded.Style == 2 && decoded.SourceActor == 0, "Settings round trip");
            for (int i = 0; i < d.Length; i++)
            {
                object old = d[i]; d[i] = "wrong";
                Check(!BetaAppearance.TryDecode(d, out decoded), "Exact primitive type field " + i); d[i] = old;
            }
            d[9] = float.NaN; Check(!BetaAppearance.TryDecode(d, out decoded), "NaN rejected");
            d[9] = 1f; d[13] = .51f; Check(!BetaAppearance.TryDecode(d, out decoded), "Rainbow upper bound");
            d[13] = .1f; d[11] = .026f; Check(!BetaAppearance.TryDecode(d, out decoded), "Bob upper bound");
            d[11] = .01f; d[2] = 5; Check(!BetaAppearance.TryDecode(d, out decoded), "Unknown style rejected");
        }
        private static void Transport()
        {
            PhotonNetwork.CurrentRoom = new Room(); PhotonNetwork.InRoom = true;
            Peer owner = new Peer(1, true,() => true), guest = new Peer(2, false), viewer = new Peer(3, false);
            Peer[] peers = new Peer[] { owner, guest, viewer };
            foreach (Peer p in peers) p.Tick(0f);
            foreach (Peer p in peers) foreach (Peer sender in peers) p.Event(sender.Player, BetaRoomSync.ChallengeKey);
            owner.Tick(.1f); owner.Tick(.5f); owner.Tick(1f);
            foreach (Peer p in peers) p.Event(owner.Player, BetaRoomSync.PermitsKey);
            Check(owner.Access(1) && viewer.Access(1), "Owner auto grant has signed room policy");
            Check(!viewer.Access(3) && viewer.Remote.ContainsKey(1), "Locked viewers render an authorized owner");
            Check(viewer.Remote[1].Session == CoreSession, "Appearance preserves core session");
            guest.Use(); Check(!guest.Sync.Grant(guest.Player.ActorNumber), "Guest cannot self grant");
            owner.Use(); Check(owner.Sync.Grant(2), "Owner selects room member");
            owner.Tick(1.5f);
            object grantedMap = owner.Player.CustomProperties[BetaRoomSync.PermitsKey];
            foreach (Peer p in peers) p.Event(owner.Player, BetaRoomSync.PermitsKey);
            Check(guest.Access(2), "Grant updates recipient access");
            guest.Tick(2f); viewer.Event(guest.Player, BetaRoomSync.AppearanceKey);
            Check(viewer.Remote.ContainsKey(2), "Authorized guest appearance reaches locked viewer");
            Player replacement = new Player { ActorNumber = 2, UserId = "different-server-account" };
            replacement.CustomProperties[BetaRoomSync.ChallengeKey] = guest.Player.CustomProperties[BetaRoomSync.ChallengeKey];
            replacement.CustomProperties[BetaRoomSync.AppearanceKey] = guest.Player.CustomProperties[BetaRoomSync.AppearanceKey];
            replacement.CustomProperties["claimed-permanent-id"] = guest.Player.UserId;
            PhotonNetwork.CurrentRoom.Players[2] = replacement; viewer.Event(replacement, BetaRoomSync.ChallengeKey);
            Check(!viewer.Access(2) && !viewer.Remote.ContainsKey(2), "Copied proof and claimed ID cannot authorize new server-native account");
            PhotonNetwork.CurrentRoom.Players[2] = guest.Player; viewer.Event(guest.Player, BetaRoomSync.ChallengeKey);
            Check(viewer.Access(2) && viewer.Remote.ContainsKey(2), "Original server-native account retains valid signed grant");
            string ownerId = owner.Player.UserId; owner.Player.UserId = "different-owner-account";
            viewer.Event(owner.Player, BetaRoomSync.PermitsKey);
            Check(!viewer.Access(1) && !viewer.Access(2), "Cached proof is bound to actual owner server-native account");
            owner.Player.UserId = ownerId; viewer.Event(owner.Player, BetaRoomSync.PermitsKey);
            string guestId = guest.Player.UserId; guest.Player.UserId = null; owner.Use();
            Check(!owner.Sync.Grant(2), "Signing waits for nonempty server-native target identity"); guest.Player.UserId = guestId;
            int sent = guest.Player.Sent.Count, rsa = verifications;
            int allocated=Hashtable.Created;
            for (int i = 0; i < 50; i++) guest.Tick(2.1f + i * .02f);
            Check(guest.Player.Sent.Count == sent, "Unchanged settings do not stream");
            Check(Hashtable.Created==allocated,"Idle Beta ticks allocate no property dictionaries");
            Check(verifications == rsa, "Tick does not repeat signature verification");
            int count = viewer.Receives; viewer.Event(guest.Player, BetaRoomSync.AppearanceKey);
            Check(viewer.Receives == count, "Repeated snapshots deduplicate callbacks");
            object[] original = (object[])guest.Player.CustomProperties[BetaRoomSync.AppearanceKey];
            object[] malformed = (object[])original.Clone(); malformed[0] = 99;
            guest.Player.CustomProperties[BetaRoomSync.AppearanceKey] = malformed; viewer.Event(guest.Player, BetaRoomSync.AppearanceKey);
            Check(!viewer.Remote.ContainsKey(2), "Unknown frame schema falls back to classic");
            guest.Player.CustomProperties[BetaRoomSync.AppearanceKey] = original; viewer.Event(guest.Player, BetaRoomSync.AppearanceKey);
            Check(viewer.Remote.ContainsKey(2), "Valid latest baseline can restore removed appearance");
            Player spoof = new Player { ActorNumber = 2 };
            spoof.CustomProperties[BetaRoomSync.AppearanceKey] = malformed; viewer.Event(spoof, BetaRoomSync.AppearanceKey);
            Check(viewer.Remote.ContainsKey(2), "Callback requires actual current room member");
            guest.Player.CustomProperties[BetaRoomSync.PermitsKey] = grantedMap; viewer.Event(guest.Player, BetaRoomSync.PermitsKey);
            Check(!viewer.Access(3), "Copied policy cannot authorize another issuer/member");
            owner.Use(); Check(owner.Sync.Revoke(2), "Owner can revoke"); owner.Tick(2f);
            object revokedMap = owner.Player.CustomProperties[BetaRoomSync.PermitsKey];
            foreach (Peer p in peers) p.Event(owner.Player, BetaRoomSync.PermitsKey);
            Check(!guest.Access(2) && !viewer.Remote.ContainsKey(2), "Revocation removes remote Beta immediately");
            owner.Player.CustomProperties[BetaRoomSync.PermitsKey] = grantedMap; viewer.Event(owner.Player, BetaRoomSync.PermitsKey);
            Check(!viewer.Access(2), "Older signed grant cannot replay past revoke");
            owner.Player.CustomProperties[BetaRoomSync.PermitsKey] = revokedMap; viewer.Event(owner.Player, BetaRoomSync.PermitsKey);
            Peer late = new Peer(4, false); late.Tick(3f);
            Check(late.Access(1) && !late.Access(2) && late.Remote.ContainsKey(1), "Late join reads signed policy and existing appearance");
            object[] bad = (object[])((object[])revokedMap).Clone(); object[] entries = (object[])((object[])bad[3]).Clone();
            object[] entry = (object[])((object[])entries[0]).Clone(); byte[] signature = (byte[])((byte[])entry[4]).Clone();
            signature[0] ^= 1; entry[4] = signature; entries[0] = entry; bad[3] = entries;
            owner.Player.CustomProperties[BetaRoomSync.PermitsKey] = bad; viewer.Event(owner.Player, BetaRoomSync.PermitsKey);
            Check(viewer.Access(1) && viewer.Remote.ContainsKey(1), "Invalid developer signature cannot erase existing signed decisions");
            owner.Player.CustomProperties[BetaRoomSync.PermitsKey] = revokedMap; viewer.Event(owner.Player, BetaRoomSync.PermitsKey);
            Check(viewer.Access(1), "Valid current policy restores access");
            PhotonNetwork.CurrentRoom.Name = "another-room";
            Peer differentRoom = new Peer(10, false); differentRoom.Tick(3.5f);
            Check(!differentRoom.Access(1), "Signed policy is bound to actual room name for fresh verifier");
            differentRoom.Use(); differentRoom.Sync.Dispose();
            PhotonNetwork.CurrentRoom.Name = "beta-test-room";
            viewer.Event(owner.Player, BetaRoomSync.PermitsKey);
            Check(viewer.Access(1), "Original room policy still validates");
            authority = "2:33333333333333333333333333333333";
            Check(!viewer.Access(1), "Host authority change invalidates cached grant immediately");
            viewer.Tick(4f); Check(!viewer.Remote.ContainsKey(1), "Host switch clears remote appearance");
            owner.Tick(4f); owner.Tick(4.5f); owner.Tick(5f); viewer.Event(owner.Player, BetaRoomSync.PermitsKey);
            Check(viewer.Access(1), "Fresh signed policy works under new authority");
            PhotonNetwork.CurrentRoom.Players.Remove(1); viewer.Use(); viewer.Sync.OnPlayerLeftRoom(owner.Player);
            Check(!viewer.Access(1) && !viewer.Remote.ContainsKey(1), "Owner departure revokes room policy");
            PhotonNetwork.CurrentRoom.IsVisible = true; late.Tick(6f);
            Check(!late.Access(1) && late.Remote.Count == 0, "Public room gate clears Beta");
            PhotonNetwork.CurrentRoom.IsVisible = false; PhotonNetwork.OfflineMode = true; guest.Tick(6f);
            Check(!guest.Access(2), "Offline gate denies access"); PhotonNetwork.OfflineMode = false;
            allowed = false; viewer.Tick(6f); Check(viewer.Remote.Count == 0, "Allowed predicate closes transport"); allowed = true;
            foreach (Peer p in peers) { p.Use(); p.Sync.Dispose(); }
            late.Use(); late.Sync.Dispose(); Check(PhotonNetwork.Targets == 0, "Dispose unregisters callbacks");
        }
        private static void Accounts()
        {
            PhotonNetwork.CurrentRoom = new Room(); PhotonNetwork.InRoom = true; allowed = true;
            bool accountAllowed = true;
            Peer member = new Peer(7, false, () => accountAllowed);
            Peer viewer = new Peer(8, false, null, actor => actor == 7 && accountAllowed);
            member.Tick(0f); viewer.Tick(0f);
            Check(member.Access(7) && viewer.Access(7), "Trusted account callbacks allow without room owner proof");
            Check(!member.Player.CustomProperties.ContainsKey(BetaRoomSync.PermitsKey) && viewer.Remote.ContainsKey(7), "Account snapshot renders without signed permit map");
            Check(!viewer.Access(8), "No callback means no account authorization");
            object oldFrame = member.Player.CustomProperties[BetaRoomSync.AppearanceKey];
            member.Player.CustomProperties["claimed-permanent-id"] = "authorized-id";
            accountAllowed = false; viewer.Event(member.Player, BetaRoomSync.AppearanceKey);
            Check(!viewer.Access(7) && !viewer.Remote.ContainsKey(7), "Claimed custom-property ID cannot replace trusted account callback");
            accountAllowed = true; viewer.Event(member.Player, BetaRoomSync.AppearanceKey);
            Check(viewer.Remote.ContainsKey(7), "Account access restores current valid snapshot");
            int count = member.Player.Sent.Count; member.Use();
            BetaAppearance changed = new BetaAppearance(false, 0, 0, true, false, 1f, 1f, 1f, 1f, 0f, 0f, false, .1f, 0f, 0f, 0f);
            member.Sync.Tick(changed, CoreSession, .1f);
            Check(member.Player.Sent.Count == count, "Appearance changes obey half-second send limit");
            member.Player.FailSend = true; member.Sync.Tick(changed, CoreSession, .5f);
            Check(member.Player.Sent.Count == count, "Failed property send does not commit appearance");
            member.Player.FailSend = false; member.Sync.Tick(changed, CoreSession, 1f);
            Check(member.Player.Sent.Count == count + 1, "Failed appearance send retries next rate window");
            viewer.Event(member.Player, BetaRoomSync.AppearanceKey);
            Check(!viewer.Remote[7].Enabled, "Enabled toggle synchronizes exactly");
            object currentFrame = member.Player.CustomProperties[BetaRoomSync.AppearanceKey];
            member.Player.CustomProperties[BetaRoomSync.AppearanceKey] = oldFrame; viewer.Event(member.Player, BetaRoomSync.AppearanceKey);
            Check(!viewer.Remote[7].Enabled, "Older appearance sequence cannot revert settings");
            member.Player.CustomProperties[BetaRoomSync.AppearanceKey] = currentFrame;
            member.Use(); member.Sync.Tick(Appearance, "invalid", 1.5f);
            Check(!member.Player.CustomProperties.ContainsKey(BetaRoomSync.AppearanceKey), "Invalid core session clears published appearance");
            member.Tick(2f); viewer.Event(member.Player, BetaRoomSync.AppearanceKey);
            Check(viewer.Remote.ContainsKey(7), "Valid session resumes publishing after invalid session");
            member.Use(); member.Sync.Tick(Appearance, "invalid", 2.5f);
            member.Tick(3f); viewer.Event(member.Player, BetaRoomSync.AppearanceKey);
            Check(member.Player.CustomProperties.ContainsKey(BetaRoomSync.AppearanceKey) && viewer.Remote[7].Enabled,
                "Same settings republish after invalid-session cleanup");
            PhotonNetwork.CurrentRoom.Name = "different-room-name";
            // Account permissions intentionally persist across rooms; signed
            // permits are independently bound to both room name and authority.
            member.Use(); member.Sync.Dispose(); viewer.Use(); viewer.Sync.Dispose();
            Check(PhotonNetwork.Targets == 0, "Account transport disposes cleanly");
        }
        private static void AccountRoomRevocation()
        {
            PhotonNetwork.CurrentRoom = new Room(); PhotonNetwork.InRoom = true;
            Peer owner = new Peer(1, true);
            Peer member = new Peer(7, false, () => true);
            Peer viewer = new Peer(8, false, null, actor => actor == 7);
            Peer[] peers = new Peer[] { owner, member, viewer };
            foreach (Peer p in peers) p.Tick(0f);
            foreach (Peer p in peers) foreach (Peer sender in peers) p.Event(sender.Player, BetaRoomSync.ChallengeKey);
            owner.Tick(.1f); owner.Tick(.5f);
            Check(member.Access(7) && viewer.Remote.ContainsKey(7), "Permanent account starts eligible in room");
            owner.Use(); Check(owner.Sync.Grant(7), "Owner can issue explicit account room grant"); owner.Tick(1f);
            object granted = owner.Player.CustomProperties[BetaRoomSync.PermitsKey];
            foreach (Peer p in peers) p.Event(owner.Player, BetaRoomSync.PermitsKey);
            owner.Use(); Check(owner.Sync.Revoke(7), "Owner can issue explicit account room denial"); owner.Tick(1.5f);
            object revoked = owner.Player.CustomProperties[BetaRoomSync.PermitsKey];
            foreach (Peer p in peers) p.Event(owner.Player, BetaRoomSync.PermitsKey);
            Check(!member.Access(7) && !viewer.Access(7) && !viewer.Remote.ContainsKey(7), "Signed room denial overrides permanent account and removes rendering");
            owner.Player.CustomProperties[BetaRoomSync.PermitsKey] = granted;
            foreach (Peer p in peers) p.Event(owner.Player, BetaRoomSync.PermitsKey);
            Check(!member.Access(7) && !viewer.Access(7), "Older signed grant cannot erase permanent-account room denial");
            object[] empty = (object[])((object[])revoked).Clone(); empty[3] = new object[0];
            owner.Player.CustomProperties[BetaRoomSync.PermitsKey] = empty;
            foreach (Peer p in peers) p.Event(owner.Player, BetaRoomSync.PermitsKey);
            Check(!member.Access(7) && !viewer.Access(7), "Unsigned omission cannot erase permanent-account room denial");
            owner.Player.CustomProperties.Remove(BetaRoomSync.PermitsKey);
            foreach (Peer p in peers) p.Event(owner.Player, BetaRoomSync.PermitsKey);
            Check(!member.Access(7) && !viewer.Access(7), "Deleted policy property cannot erase permanent-account room denial");
            object[] forged = (object[])((object[])revoked).Clone(); object[] entries = (object[])((object[])forged[3]).Clone();
            for (int i = 0; i < entries.Length; i++)
            {
                object[] entry = (object[])entries[i];
                if ((int)entry[0] != 7) continue;
                entry = (object[])entry.Clone(); entry[2] = (int)entry[2] + 1; entry[3] = true; entries[i] = entry;
            }
            forged[3] = entries; owner.Player.CustomProperties[BetaRoomSync.PermitsKey] = forged;
            foreach (Peer p in peers) p.Event(owner.Player, BetaRoomSync.PermitsKey);
            Check(!member.Access(7) && !viewer.Access(7), "Invalid newer signature cannot erase permanent-account room denial");
            owner.Player.CustomProperties[BetaRoomSync.PermitsKey] = revoked;
            member.Use(); member.Sync.Reset(); member.Tick(2f);
            viewer.Event(member.Player, BetaRoomSync.ChallengeKey);
            Check(!member.Access(7) && !viewer.Access(7), "Same-member mod reset and fresh target challenge preserve signed account denial");
            Check(!member.Player.CustomProperties.ContainsKey(BetaRoomSync.AppearanceKey), "Denied account clears own appearance publication");
            owner.Use(); Check(owner.Sync.Grant(7), "Owner can regrant after target mod reset"); owner.Tick(2.5f);
            object regranted = owner.Player.CustomProperties[BetaRoomSync.PermitsKey];
            foreach (Peer p in peers) p.Event(owner.Player, BetaRoomSync.PermitsKey);
            Check(member.Access(7) && viewer.Access(7), "New signed grant supersedes denial from previous target challenge");
            owner.Player.CustomProperties[BetaRoomSync.PermitsKey] = revoked;
            foreach (Peer p in peers) p.Event(owner.Player, BetaRoomSync.PermitsKey);
            Check(member.Access(7) && viewer.Access(7), "Old target-challenge denial cannot replay over newer signed grant");
            owner.Player.CustomProperties[BetaRoomSync.PermitsKey] = regranted;
            owner.Use(); Check(owner.Sync.Revoke(7), "Owner can deny again after refreshed-target grant"); owner.Tick(3f);
            foreach (Peer p in peers) p.Event(owner.Player, BetaRoomSync.PermitsKey);
            Check(!member.Access(7) && !viewer.Access(7), "Latest refreshed-target denial applies");
            PhotonNetwork.CurrentRoom.Players.Remove(1);
            foreach (Peer p in new Peer[] { member, viewer }) { p.Use(); p.Sync.OnPlayerLeftRoom(owner.Player); }
            Check(member.Access(7) && viewer.Access(7), "Permanent account access returns when denying owner leaves");
            member.Tick(3.5f); viewer.Event(member.Player, BetaRoomSync.AppearanceKey);
            Check(viewer.Remote.ContainsKey(7), "Permanent account appearance resumes after owner leaves");
            foreach (Peer p in peers) { p.Use(); p.Sync.Dispose(); }
            Check(PhotonNetwork.Targets == 0, "Room denial scenario disposes cleanly");
        }
        public static int Main()
        {
            try { Model(); Transport(); Accounts(); AccountRoomRevocation(); Console.WriteLine("Beta sync: " + checks + " checks passed."); return 0; }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        }
    }
}
