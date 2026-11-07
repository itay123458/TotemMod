using System;
using System.Collections.Generic;
using MaceMod.Shared;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;

// Real protocol/transport; Photon is replaced only at the network boundary.
namespace ExitGames.Client.Photon { public sealed class Hashtable : Dictionary<object, object> { } }
namespace Photon.Realtime
{
    public interface IInRoomCallbacks { }
    public interface IMatchmakingCallbacks { }
    public sealed class FriendInfo { }
    public sealed class Room
    {
        public string Name = "private-modded";
        public bool IsVisible;
        public readonly Dictionary<int, Player> Players = new Dictionary<int, Player>();
        public Player GetPlayer(int actor) { Player p; return Players.TryGetValue(actor, out p) ? p : null; }
    }
    public sealed class Player
    {
        public int ActorNumber;
        public bool IsInactive, FailSend;
        public readonly Hashtable CustomProperties = new Hashtable();
        public readonly List<Hashtable> Sent = new List<Hashtable>();
        public bool SetCustomProperties(Hashtable change)
        {
            if (FailSend) return false;
            foreach (KeyValuePair<object, object> e in change)
                if (e.Value == null) CustomProperties.Remove(e.Key); else CustomProperties[e.Key] = e.Value;
            Sent.Add(change); return true;
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
        public static double Time;
        public static int Targets;
        public static void AddCallbackTarget(object target) { Targets++; }
        public static void RemoveCallbackTarget(object target) { Targets--; }
    }
}
internal static class MaceProtocolTests
{
    private const string A = "11111111111111111111111111111111", B = "22222222222222222222222222222222";
    private static int checks;
    private static void Check(bool value, string label) { checks++; if (!value) throw new Exception(label); }
    private static MaceState State(string session) { return new MaceState(session, true, true, 3, 1, 2, 1.25f, true); }
    private static void Models()
    {
        MaceState state; object[] s = State(A).Encode();
        Check(MaceState.TryDecode(s, out state) && state.Session == A && state.Density == 3 && state.Receive, "State round trip");
        Check(s.Length == 10 && (int)s[0] == 1 && (float)s[7] == 1.25f && (float)s[9] == 1f, "State wire schema with default power");
        for (int i = 0; i < s.Length; i++) { object v = s[i]; s[i] = null; Check(!MaceState.TryDecode(s, out state), "Null state field " + i); s[i] = v; }
        s[2] = 1; Check(!MaceState.TryDecode(s, out state), "Bool is exact type"); s = State(A).Encode();
        s[7] = double.NaN; Check(!MaceState.TryDecode(s, out state), "Float is exact type");
        foreach (float value in new float[] { float.NaN, float.PositiveInfinity, .49f, 1.51f }) { s[7] = value; Check(!MaceState.TryDecode(s, out state), "Invalid scale"); }
        s = State(A).Encode(); s[4] = 6; Check(!MaceState.TryDecode(s, out state), "Density cap");
        s = State(A).Encode(); s[5] = -1; Check(!MaceState.TryDecode(s, out state), "Knockback lower bound");
        s = State(A).Encode(); s[6] = 4; Check(!MaceState.TryDecode(s, out state), "Wind burst cap");
        foreach (float value in new float[] { float.NaN, float.PositiveInfinity, .24f, 2.01f }) { s = State(A).Encode(); s[9] = value; Check(!MaceState.TryDecode(s, out state), "Invalid power"); }
        foreach (string value in new string[] { "", "not-a-guid", "11111111-1111-1111-1111-111111111111", "00000000000000000000000000000000" })
        { s = State(A).Encode(); s[1] = value; Check(!MaceState.TryDecode(s, out state), "Invalid session"); }
        MaceHit hit; object[] h = new MaceHit(A, 2, B, 1, 100d, 12f, 1f, 2f, 3f).Encode();
        Check(MaceHit.TryDecode(h, out hit) && hit.TargetActor == 2 && hit.Blocks == 12f && hit.Time == 100d, "Hit round trip");
        Check(h.Length == 10 && (int)h[0] == 1, "Hit wire schema");
        for (int i = 0; i < h.Length; i++) { object v = h[i]; h[i] = null; Check(!MaceHit.TryDecode(h, out hit), "Null hit field " + i); h[i] = v; }
        foreach (float value in new float[] { float.NaN, float.NegativeInfinity, -1f, 64.01f }) { h[6] = value; Check(!MaceHit.TryDecode(h, out hit), "Invalid fall blocks"); }
        h = new MaceHit(A, 2, B, 1, 100d, 12f, 1f, 2f, 3f).Encode(); h[7] = 100000f; Check(!MaceHit.TryDecode(h, out hit), "Coordinate cap");
        h[7] = 1f; h[5] = double.NaN; Check(!MaceHit.TryDecode(h, out hit), "NaN clock");
        h[5] = 100f; Check(!MaceHit.TryDecode(h, out hit), "Clock exact double");
        h[5] = 100d; h[4] = 0; Check(!MaceHit.TryDecode(h, out hit), "Positive sequence");
        MaceReceipt receipt; object[] r = new MaceReceipt(1, A, 1, B, 100d, 3).Encode();
        Check(MaceReceipt.TryDecode(r, out receipt) && receipt.Outcome == 3 && receipt.SourceActor == 1, "Receipt round trip");
        for (int i = 0; i < r.Length; i++) { object v = r[i]; r[i] = null; Check(!MaceReceipt.TryDecode(r, out receipt), "Null receipt field " + i); r[i] = v; }
        r[6] = 4; Check(!MaceReceipt.TryDecode(r, out receipt), "Unknown outcome");
    }
    private sealed class Peer
    {
        public readonly Player Player;
        public readonly MaceTransport Transport;
        public MaceState State;
        public bool Allowed = true;
        public int States, Hits, Receipts, Removed;
        public Peer(int actor, string session)
        {
            State = MaceProtocolTests.State(session);
            Player = new Player { ActorNumber = actor }; PhotonNetwork.CurrentRoom.Players.Add(actor, Player); Use();
            Transport = new MaceTransport(() => Allowed, (a, s) => States++, (a, h) => Hits++, (a, r) => Receipts++, a => Removed++);
        }
        public void Use() { PhotonNetwork.LocalPlayer = Player; }
        public void Tick(float time) { Use(); Transport.Tick(State, time); }
        public void Event(Peer sender, string key) { Event(sender.Player, key); }
        public void Event(Player sender, string key) { Use(); Hashtable changes = new Hashtable(); changes[key] = true; Transport.OnPlayerPropertiesUpdate(sender, changes); }
        public bool Hit(int target, string session, int seq, float now) { Use(); return Transport.SendHit(new MaceHit(State.Session, target, session, seq, PhotonNetwork.Time, 6f, 0f, 0f, 0f), now); }
        public bool Receipt(int actor, string session, int seq) { Use(); return Transport.SendReceipt(new MaceReceipt(actor, session, seq, State.Session, PhotonNetwork.Time, 3)); }
        public void Dispose() { Use(); Transport.Dispose(); }
    }
    private static void TransportCases()
    {
        PhotonNetwork.CurrentRoom = new Room(); PhotonNetwork.InRoom = true; PhotonNetwork.OfflineMode = false; PhotonNetwork.Time = 100d;
        Peer source = new Peer(1, A), target = new Peer(2, B);
        source.Tick(0f); target.Tick(0f); source.Event(target, MaceTransport.StateKey); target.Event(source, MaceTransport.StateKey);
        int count = source.Player.Sent.Count; source.Tick(.1f); source.Tick(1f); Check(source.Player.Sent.Count == count, "Unchanged state does not stream");
        source.State = new MaceState(A, true, true, 4, 1, 2, 1f, true); source.Tick(.1f);
        Check(source.Player.Sent.Count == count, "State rate at most 4Hz"); source.Tick(.25f); Check(source.Player.Sent.Count == count + 1, "State change sent at rate boundary");
        target.Event(source, MaceTransport.StateKey);
        Check(source.Hit(2, B, 1, 1f), "Fresh hit sent to opted-in current peer");
        target.Event(source, MaceTransport.HitKey); Check(target.Hits == 1, "Current hit received once");
        target.Event(source, MaceTransport.HitKey); Check(target.Hits == 1, "Hit replay rejected");
        Check(target.Receipt(1, A, 1), "Target responds to observed hit");
        Check(target.Receipts == 1, "Local host target observes its own successful receipt");
        Check(!target.Receipt(1, A, 1) && target.Receipts == 1, "Local target receipt delivered once");
        target.Event(target, MaceTransport.ReceiptKey); Check(target.Receipts == 1, "Own property callback cannot echo receipt");
        source.Event(target, MaceTransport.ReceiptKey);
        Check(source.Receipts == 1, "Matching target receipt received"); source.Event(target, MaceTransport.ReceiptKey); Check(source.Receipts == 1, "Receipt replay rejected");
        Check(!target.Receipt(1, A, 99), "No unsolicited target receipt");
        Check(!source.Hit(2, B, 2, 1.1f), "Hit rate at most 2Hz");
        object oldHit = source.Player.CustomProperties[MaceTransport.HitKey];
        Peer late = new Peer(3, "33333333333333333333333333333333"); late.Tick(2f);
        Check(late.States == 2 && late.Hits == 0 && late.Receipts == 0, "Late join scans states only");
        late.Event(source, MaceTransport.HitKey); Check(late.Hits == 0, "Late join cached hit not replayed");
        PhotonNetwork.Time = 103d; source.Player.CustomProperties[MaceTransport.HitKey] = oldHit; target.Event(source, MaceTransport.HitKey); Check(target.Hits == 1, "Stale hit rejected");
        Check(source.Hit(2, B, 2, 2f), "Second fresh hit sent");
        Player spoof = new Player { ActorNumber = 1 }; spoof.CustomProperties[MaceTransport.HitKey] = source.Player.CustomProperties[MaceTransport.HitKey];
        target.Event(spoof, MaceTransport.HitKey); Check(target.Hits == 1, "Callback identity requires actual member");
        target.Event(source, MaceTransport.HitKey); Check(target.Hits == 2, "Real member accepted");
        source.Player.IsInactive = true; Check(!source.Hit(2, B, 3, 3f), "Inactive local member rejected"); source.Player.IsInactive = false;
        object oldState = target.Player.CustomProperties[MaceTransport.StateKey];
        target.State = MaceProtocolTests.State("44444444444444444444444444444444"); target.Tick(3f); source.Event(target, MaceTransport.StateKey);
        Check(!source.Hit(2, B, 3, 3f), "Target session reset rejects prior nonce");
        target.Player.CustomProperties[MaceTransport.StateKey] = oldState; source.Event(target, MaceTransport.StateKey);
        Check(!source.Hit(2, B, 3, 3f), "Retired session cannot replay");
        source.Allowed = false; source.Tick(4f); Check(!source.Player.CustomProperties.ContainsKey(MaceTransport.StateKey), "Private modded predicate closes and clears");
        source.Allowed = true; PhotonNetwork.CurrentRoom.IsVisible = true; target.Tick(4f); Check(!target.Hit(1, A, 3, 4f), "Public room gate");
        PhotonNetwork.CurrentRoom.IsVisible = false; PhotonNetwork.OfflineMode = true; late.Tick(4f); Check(!late.Player.CustomProperties.ContainsKey(MaceTransport.StateKey), "Offline gate clears");
        PhotonNetwork.OfflineMode = false; source.Dispose(); target.Dispose(); late.Dispose(); Check(PhotonNetwork.Targets == 0, "Dispose unregisters");
    }
    private static void AdversarialCases()
    {
        PhotonNetwork.CurrentRoom = new Room(); PhotonNetwork.InRoom = true; PhotonNetwork.OfflineMode = false; PhotonNetwork.Time = 200d;
        Peer source = new Peer(1, A), target = new Peer(2, B), observer = new Peer(3, "33333333333333333333333333333333");
        Peer[] peers = new Peer[] { source, target, observer };
        foreach (Peer p in peers) p.Tick(0f);
        foreach (Peer p in peers) foreach (Peer other in peers) if (p != other) p.Event(other, MaceTransport.StateKey);
        Check(source.Hit(2, B, 1, 0f), "Observer fixture sends hit"); target.Event(source, MaceTransport.HitKey); observer.Event(source, MaceTransport.HitKey);
        Check(observer.Hits == 1, "Independent host observes source request");
        observer.Use(); Check(!observer.Transport.SendReceipt(new MaceReceipt(1, A, 1, B, 200d, 3)), "Third member cannot answer for target");
        Check(target.Receipt(1, A, 1), "Real target sends receipt"); observer.Event(target, MaceTransport.ReceiptKey); Check(observer.Receipts == 1, "Host accepts exact matching target receipt");
        object[] wrong = new MaceReceipt(1, A, 1, B, 200d, 3).Encode(); observer.Player.CustomProperties[MaceTransport.ReceiptKey] = wrong;
        source.Event(observer, MaceTransport.ReceiptKey); Check(source.Receipts == 0, "Copied receipt cannot impersonate target actor");
        target.Player.CustomProperties[MaceTransport.ReceiptKey] = new MaceReceipt(1, A, 99, B, 200d, 3).Encode();
        source.Event(target, MaceTransport.ReceiptKey); Check(source.Receipts == 0, "Receipt must identify exact pending sequence");
        target.Player.CustomProperties[MaceTransport.ReceiptKey] = new MaceReceipt(1, A, 1, B, 199d, 3).Encode();
        source.Event(target, MaceTransport.ReceiptKey); Check(source.Receipts == 0, "Receipt cannot precede request");
        target.Player.CustomProperties[MaceTransport.ReceiptKey] = new MaceReceipt(1, A, 1, B, 200d, 3).Encode();
        source.Event(target, MaceTransport.ReceiptKey); Check(source.Receipts == 1, "Invalid receipt does not consume valid pending request");
        PhotonNetwork.Time = 201d;
        source.Player.CustomProperties[MaceTransport.HitKey] = new MaceHit(A, 2, B, 2, 203d, 2f, 0f, 0f, 0f).Encode();
        target.Event(source, MaceTransport.HitKey); Check(target.Hits == 1, "Future hit rejected");
        PhotonNetwork.Time = 203d; target.Event(source, MaceTransport.HitKey); Check(target.Hits == 1, "Rejected future sequence cannot be replayed later");
        source.Player.CustomProperties[MaceTransport.HitKey] = new MaceHit(A, 2, B, 3, 200d, 2f, 0f, 0f, 0f).Encode();
        target.Event(source, MaceTransport.HitKey); Check(target.Hits == 1, "Unseen stale sequence rejected");
        target.State = new MaceState(B, false, false, 0, 0, 0, 1f, false); target.Tick(1f); source.Event(target, MaceTransport.StateKey);
        Check(!source.Hit(2, B, 4, 2f), "Receive opt-out blocks sending");
        target.State = new MaceState(B, false, false, 0, 0, 0, 1f, true); target.Tick(2f); source.Event(target, MaceTransport.StateKey);
        Check(source.Hit(2, B, 4, 2f), "Receive consent works with attack disabled");
        target.Event(source, MaceTransport.HitKey); Check(target.Hits == 2, "Updated Totem receiver does not require mace equipped");
        object validSourceState = source.Player.CustomProperties[MaceTransport.StateKey];
        object currentSourceHit = source.Player.CustomProperties[MaceTransport.HitKey];
        source.Player.CustomProperties[MaceTransport.StateKey] = new object[] { 99 }; target.Event(source, MaceTransport.StateKey);
        source.Player.CustomProperties[MaceTransport.HitKey] = new MaceHit(A, 2, B, 1, 200d, 2f, 0f, 0f, 0f).Encode();
        source.Player.CustomProperties[MaceTransport.StateKey] = validSourceState; target.Event(source, MaceTransport.StateKey);
        source.Player.CustomProperties[MaceTransport.HitKey] = currentSourceHit; target.Event(source, MaceTransport.HitKey);
        Check(target.Hits == 2, "Malformed state cannot erase processed hit history");
        source.State = new MaceState(A, false, false, 0, 0, 0, 1f, true); source.Tick(3f);
        Check(!source.Hit(2, B, 5, 3f), "Disabled attacker cannot send");
        // A restarted mod begins revision numbering from one with a new nonce.
        target.Player.CustomProperties[MaceTransport.StateKey] = new object[] { 1, "private-modded", 1, State("44444444444444444444444444444444").Encode() };
        int oldStates = source.States; source.Event(target, MaceTransport.StateKey);
        Check(source.States == oldStates + 1, "Fresh session accepts restarted state revision");
        source.Use(); PhotonNetwork.InRoom = false; Check(!source.Transport.SendHit(new MaceHit(A, 2, B, 6, 203d, 1f, 0f, 0f, 0f), 4f), "Send requires InRoom");
        PhotonNetwork.InRoom = true;
        source.Tick(4f); int oldRemoved = source.Removed;
        PhotonNetwork.CurrentRoom.Name = "other-private-room"; source.Tick(5f);
        Check(source.Removed > oldRemoved && !source.Hit(2, B, 7, 5f), "Room name transition clears former peer state");
        foreach (Peer p in peers) p.Dispose(); Check(PhotonNetwork.Targets == 0, "Adversarial fixture disposes cleanly");
    }
    private static void IdleReset()
    {
        PhotonNetwork.CurrentRoom = new Room(); PhotonNetwork.InRoom = true; PhotonNetwork.OfflineMode = false;
        Peer idle = new Peer(1, A);
        AppDomain.MonitoringIsEnabled = true;
        for (int i = 0; i < 10; i++) idle.Transport.Reset();
        GC.Collect();
        long before = AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize;
        for (int i = 0; i < 1000; i++) idle.Transport.Reset();
        GC.Collect();
        long allocated = AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize - before;
        Check(allocated < 1024, "Already-empty Reset must not allocate per frame: " + allocated + " bytes");
        idle.Dispose();
    }
    public static int Main()
    {
        try { Models(); TransportCases(); AdversarialCases(); IdleReset(); Console.WriteLine("Mace protocol: " + checks + " checks passed."); return 0; }
        catch (Exception e) { Console.Error.WriteLine(e.Message); return 1; }
    }
}
