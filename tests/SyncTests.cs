using System;
using System.Collections.Generic;
using TotemMod;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;

// In-memory Photon property boundary, exercising production PhotonSync on two
// peers without connecting accounts, joining a room, or touching game rules.
namespace ExitGames.Client.Photon { public sealed class Hashtable : Dictionary<object, object> { } }
namespace Photon.Realtime
{
    public interface IInRoomCallbacks { }
    public interface IMatchmakingCallbacks { }
    public sealed class FriendInfo { }
    public sealed class Room
    {
        public bool IsVisible;
        public readonly Dictionary<int, Player> Players = new Dictionary<int, Player>();
        public Player GetPlayer(int actor) { Player p; return Players.TryGetValue(actor, out p) ? p : null; }
    }
    public sealed class Player
    {
        public int ActorNumber;
        public string UserId;
        public bool IsInactive;
        public bool IsMasterClient;
        public bool IsLocal { get { return this == PhotonNetwork.LocalPlayer; } }
        public readonly Hashtable CustomProperties = new Hashtable();
        public readonly List<Hashtable> Sent = new List<Hashtable>();
        public bool FailSend;
        public bool SetCustomProperties(Hashtable change)
        {
            if (FailSend) return false;
            Hashtable copy = new Hashtable();
            foreach (KeyValuePair<object, object> e in change)
            {
                copy[e.Key] = e.Value;
                if (e.Value == null) CustomProperties.Remove(e.Key); else CustomProperties[e.Key] = e.Value;
            }
            Sent.Add(copy);
            return true;
        }
    }
}
namespace Photon.Pun
{
    public static class PhotonNetwork
    {
        public static bool InRoom;
        public static bool OfflineMode;
        public static Room CurrentRoom;
        public static Player LocalPlayer;
        public static bool IsMasterClient { get { return LocalPlayer.IsMasterClient; } }
        public static int Targets;
        public static double Time;
        public static void AddCallbackTarget(object target) { Targets++; }
        public static void RemoveCallbackTarget(object target) { Targets--; }
    }
}
internal static class SyncTests
{
    private static int checks;
    private static void Check(bool value, string label) { checks++; if (!value) throw new Exception(label); }
    private static string Id(int n) { return n.ToString("x32"); }
    private static Hashtable DevChange(string key, object value) { Hashtable change = new Hashtable(); change[key] = value; return change; }
    private static byte[] Sign(byte[] bytes)
    {
        using (System.Security.Cryptography.SHA256 hash = System.Security.Cryptography.SHA256.Create()) return hash.ComputeHash(bytes);
    }
    private static bool Verify(int actor, byte[] bytes, byte[] signature)
    {
        byte[] expected = Sign(bytes);
        if (signature.Length != expected.Length) return false;
        for (int i = 0; i < expected.Length; i++) if (expected[i] != signature[i]) return false;
        return true;
    }
    private static void Commands()
    {
        Room room = new Room();
        Player host = new Player { ActorNumber = 1, IsMasterClient = true, UserId = "host" };
        Player guest = new Player { ActorNumber = 2, UserId = "developer" };
        Player other = new Player { ActorNumber = 3, UserId = "other" };
        room.Players.Add(1,host); room.Players.Add(2,guest); room.Players.Add(3,other);
        PhotonNetwork.CurrentRoom = room; PhotonNetwork.InRoom = true; PhotonNetwork.OfflineMode = false;
        PhotonNetwork.Time = 100; PhotonNetwork.LocalPlayer = host;
        bool authorized = true; int actions = 0, results = 0, accepted = 0;
        string nonce = null; DeveloperCommand received = null;
        PhotonSync hostSync = new PhotonSync(() => true,(a,s) => {},a => {},() => {},(s,p) => {},
            authorizeDeveloper: a => authorized && a == 2, verifyCommand: Verify,
            commandReceived: (a,n,c) => { Check(a == 2,"Command actor derives from current room member"); actions++; nonce = n; received = c; });
        PhotonSync guestSync = new PhotonSync(() => true,(a,s) => {},a => {},() => {},(s,p) => {},
            signCommand: Sign, commandResult: ok => { results++; if (ok) accepted++; });
        DeveloperCommand command = new DeveloperCommand(DeveloperCommandKind.Capacity,8);
        hostSync.Tick(false,1,0);
        Check(host.CustomProperties.ContainsKey(PhotonSync.CommandSessionKey),"Private host publishes fresh command authority session");
        Check(!hostSync.RequestCommand(command,0),"Actual host does not send guest command requests");
        PhotonNetwork.LocalPlayer = guest; guestSync.Tick(false,1,0);
        room.IsVisible = true; Check(!guestSync.RequestCommand(command,0),"Public command request rejected"); room.IsVisible = false;
        PhotonNetwork.OfflineMode = true; Check(!guestSync.RequestCommand(command,0),"Offline command request rejected"); PhotonNetwork.OfflineMode = false;
        Check(!guestSync.RequestCommand(null,0) && !guestSync.RequestCommand(command,float.NaN),"Null command and nonfinite clock rejected");
        guest.FailSend = true; Check(!guestSync.RequestCommand(command,0),"Failed command publication creates no pending request"); guest.FailSend = false;
        Check(guestSync.RequestCommand(command,0),"Guest can send signed bounded command to actual private host");
        Hashtable request = guest.Sent[guest.Sent.Count-1];
        Check(!guestSync.RequestCommand(command,1),"Only one command can await host reply");
        PhotonNetwork.LocalPlayer = host;
        hostSync.OnPlayerEnteredRoom(guest); hostSync.Tick(false,1,0);
        Check(actions == 0,"Cached player command never executes from joining or polling");
        hostSync.OnPlayerPropertiesUpdate(new Player {ActorNumber=2,UserId="developer"},request);
        Check(actions == 0,"Forged callback member rejected");
        room.IsVisible = true; hostSync.OnPlayerPropertiesUpdate(guest,request); room.IsVisible = false;
        Check(actions == 0,"Public command callback rejected");
        object[] original = (object[])request[PhotonSync.CommandRequestKey];
        int[] badFields = { 0, 1, 2, 3, 4, 5, 6, 7 };
        object[] badValues = { 2, Id(991), 3, "bad", 0, double.NaN, new object[] {1,1,21,0f}, new byte[513] };
        for (int i = 0; i < badFields.Length; i++)
        {
            object[] bad = (object[])original.Clone(); bad[badFields[i]] = badValues[i];
            hostSync.OnPlayerPropertiesUpdate(guest,DevChange(PhotonSync.CommandRequestKey,bad));
            Check(actions == 0,"Malformed command envelope cannot execute");
        }
        guest.IsInactive = true; hostSync.OnPlayerPropertiesUpdate(guest,request); guest.IsInactive = false;
        Check(actions == 0,"Inactive room member cannot issue developer commands");
        object[] forged = (object[])((object[])request[PhotonSync.CommandRequestKey]).Clone();
        forged[6] = new DeveloperCommand(DeveloperCommandKind.Capacity,20).Encode();
        hostSync.OnPlayerPropertiesUpdate(guest,DevChange(PhotonSync.CommandRequestKey,forged));
        Check(actions == 0,"Changing signed command payload rejects execution");
        hostSync.OnPlayerPropertiesUpdate(other,request);
        Check(actions == 0,"Copying another member signed command rejects execution");
        hostSync.OnPlayerPropertiesUpdate(guest,request);
        Check(actions == 1 && received.Kind == DeveloperCommandKind.Capacity && received.Value == 8,"Authorized host receives validated command");
        hostSync.OnPlayerPropertiesUpdate(guest,request); Check(actions == 1,"Command request replay never executes twice");
        Check(!hostSync.ReplyCommand(2,Id(1000),true),"Command acknowledgment must match received nonce");
        Check(hostSync.ReplyCommand(2,nonce,true),"Host replies only to outstanding command");
        Hashtable reply = host.Sent[host.Sent.Count-1];
        PhotonNetwork.LocalPlayer = guest;
        guestSync.OnPlayerPropertiesUpdate(other,reply); Check(results == 0,"Other member cannot acknowledge command");
        object[] validReply = (object[])reply[PhotonSync.CommandReplyKey];
        object[] wrongReply = (object[])validReply.Clone(); wrongReply[3] = Id(777);
        guestSync.OnPlayerPropertiesUpdate(host,DevChange(PhotonSync.CommandReplyKey,wrongReply));
        wrongReply = (object[])validReply.Clone(); wrongReply[1] = Id(778);
        guestSync.OnPlayerPropertiesUpdate(host,DevChange(PhotonSync.CommandReplyKey,wrongReply));
        wrongReply = (object[])validReply.Clone(); wrongReply[2] = 3;
        guestSync.OnPlayerPropertiesUpdate(host,DevChange(PhotonSync.CommandReplyKey,wrongReply));
        Check(results == 0,"Command reply must match authority session, nonce and requester actor");
        guestSync.OnPlayerPropertiesUpdate(host,reply); guestSync.OnPlayerPropertiesUpdate(host,reply);
        Check(results == 1 && accepted == 1,"Actual host matching reply completes exactly once");
        Check(!guestSync.RequestCommand(command,.249f) && guestSync.RequestCommand(command,.25f),"Command send cooldown allows slider updates every quarter second");
        Hashtable second = guest.Sent[guest.Sent.Count-1];
        PhotonNetwork.LocalPlayer = host; authorized = false; hostSync.OnPlayerPropertiesUpdate(guest,second);
        Check(actions == 1,"Unauthorized developer never executes host callback");
        reply = host.Sent[host.Sent.Count-1];
        PhotonNetwork.LocalPlayer = guest; guestSync.OnPlayerPropertiesUpdate(host,reply);
        Check(results == 2 && accepted == 1,"Unauthorized signed requester receives denial");
        Check(guestSync.RequestCommand(command,1),"New command after denial can be sent");
        Hashtable stale = guest.Sent[guest.Sent.Count-1];
        guestSync.Tick(false,1,6.99f); Check(results == 2,"Command waits before six second timeout");
        guestSync.Tick(false,1,7); Check(results == 3,"Command timeout reports failure once");
        PhotonNetwork.LocalPlayer = host; authorized = true; PhotonNetwork.Time = 107;
        hostSync.OnPlayerPropertiesUpdate(guest,stale); Check(actions == 1,"Delayed command past shared-clock lifetime never executes");
        PhotonNetwork.LocalPlayer = guest; Check(guestSync.RequestCommand(command,8),"Command can restart after timeout");
        Hashtable oldRoomRequest = guest.Sent[guest.Sent.Count-1];
        guestSync.OnMasterClientSwitched(other); Check(results == 4,"Master switch cancels command pending state");
        PhotonNetwork.LocalPlayer = host; hostSync.OnMasterClientSwitched(host); hostSync.Tick(false,1,8);
        hostSync.OnPlayerPropertiesUpdate(guest,oldRoomRequest); Check(actions == 1,"Old authority session cannot execute after host transition");
        PhotonNetwork.LocalPlayer = guest; Check(guestSync.RequestCommand(command,9),"Request binds new authority session");
        Hashtable freshRequest = guest.Sent[guest.Sent.Count-1];
        PhotonNetwork.LocalPlayer = host; hostSync.OnPlayerPropertiesUpdate(guest,freshRequest);
        Check(actions == 2,"New authority accepts fresh command");
        hostSync.ReplyCommand(2,nonce,true); reply = host.Sent[host.Sent.Count-1];
        PhotonNetwork.LocalPlayer = guest; guestSync.OnPlayerPropertiesUpdate(host,reply);
        guestSync.Dispose();
        guestSync = new PhotonSync(() => true,(a,s) => {},a => {},() => {},(s,p) => {},signCommand: Sign,
            commandResult: ok => { results++; if (ok) accepted++; });
        PhotonNetwork.Time = 108;
        Check(guestSync.RequestCommand(command,10),"Plugin restart can send a new command in same room");
        freshRequest = guest.Sent[guest.Sent.Count-1];
        PhotonNetwork.LocalPlayer = host; hostSync.OnPlayerPropertiesUpdate(guest,freshRequest);
        Check(actions == 3,"Newer signed timestamp allows restarted sender sequence without accepting old replay");
        hostSync.OnPlayerPropertiesUpdate(guest,oldRoomRequest); Check(actions == 3,"Pre-restart request cannot replay");
        PhotonNetwork.LocalPlayer = guest;
        guestSync.Reset(); Check(results == 6 && !guest.CustomProperties.ContainsKey(PhotonSync.CommandRequestKey),"Reset cancels commands and clears command properties");
        Check(guestSync.RequestCommand(command,11),"Explicit reset can restart private command protocol");
        guestSync.OnLeftRoom(); Check(results == 7 && !guestSync.CommandPending,"Leaving room cancels pending command");
        PhotonSync noSigner = new PhotonSync(() => true,(a,s) => {},a => {},() => {},(s,p) => {});
        Check(!noSigner.CanRequestCommands && !noSigner.RequestCommand(command,12),"Unsigned clients cannot request commands"); noSigner.Dispose();
        PhotonNetwork.LocalPlayer = host; hostSync.Dispose();
        Check(!host.CustomProperties.ContainsKey(PhotonSync.CommandSessionKey) && !host.CustomProperties.ContainsKey(PhotonSync.CommandReplyKey),"Host unload clears command authority and reply properties");
        PhotonNetwork.LocalPlayer = guest; guestSync.Dispose();
        Check(PhotonNetwork.Targets == 0,"Command fixture unregisters all peers");
    }
    private static void DevRefills()
    {
        Room room = new Room();
        Player host = new Player { ActorNumber = 1, IsMasterClient = true };
        Player guest = new Player { ActorNumber = 2 };
        Player other = new Player { ActorNumber = 3 };
        room.Players.Add(1, host); room.Players.Add(2, guest); room.Players.Add(3, other);
        PhotonNetwork.CurrentRoom = room; PhotonNetwork.InRoom = true; PhotonNetwork.LocalPlayer = host;
        bool enabled = true;
        int requests = 0, results = 0, acceptedResults = 0, requester = 0;
        string requestedNonce = null;
        PhotonSync hostSync = new PhotonSync(() => enabled, (actor,snap) => {}, actor => {}, () => {}, (id,pops) => {},
            refillRequested: (actor,requestNonce) => { requests++; requester = actor; requestedNonce = requestNonce; });
        PhotonSync guestSync = new PhotonSync(() => enabled, (actor,snap) => {}, actor => {}, () => {}, (id,pops) => {},
            refillResult: accepted => { results++; if (accepted) acceptedResults++; });
        hostSync.Tick(false,1,0);
        Check(!hostSync.RequestRefill(0), "Host does not issue a nonhost refill request");
        Check(!hostSync.ReplyRefill(2,Id(1),true), "Unsolicited replies are rejected");
        PhotonNetwork.LocalPlayer = guest; guestSync.Tick(false,1,0);
        room.IsVisible = true; int before = guest.Sent.Count;
        Check(!guestSync.RequestRefill(0) && guest.Sent.Count == before, "Public room cannot write refill requests");
        room.IsVisible = false; PhotonNetwork.InRoom = false;
        Check(!guestSync.RequestRefill(0) && guest.Sent.Count == before, "Offline cannot write refill requests");
        PhotonNetwork.InRoom = true; enabled = false;
        Check(!guestSync.RequestRefill(0), "Unallowed room cannot request"); enabled = true;
        PhotonNetwork.OfflineMode = true;
        Check(!guestSync.RequestRefill(0), "Photon offline-mode room cannot request"); PhotonNetwork.OfflineMode = false;
        Check(!guestSync.RequestRefill(float.NaN), "Invalid request clock is rejected");
        guest.FailSend = true;
        Check(!guestSync.RequestRefill(0), "Failed send does not create pending request"); guest.FailSend = false;
        Check(guestSync.RequestRefill(0), "Guest can request a private refill");
        Hashtable request = guest.Sent[guest.Sent.Count-1];
        object[] requestData = (object[])request[PhotonSync.DevRequestKey];
        string nonce = (string)requestData[1]; Guid parsed;
        Check(requestData.Length == 2 && (int)requestData[0] == 1 && Guid.TryParseExact(nonce,"N",out parsed), "Request has versioned nonce");
        Check(!guestSync.RequestRefill(3), "Only one request can be pending");
        PhotonNetwork.LocalPlayer = host;
        hostSync.OnPlayerEnteredRoom(guest); hostSync.Tick(false,1,0);
        Check(requests == 0, "Join and polling do not replay cached requests");
        hostSync.OnPlayerPropertiesUpdate(new Player { ActorNumber = 2 }, request);
        hostSync.OnPlayerPropertiesUpdate(host, request);
        Check(requests == 0, "Spoofed and local requester identities are rejected");
        room.IsVisible = true; hostSync.OnPlayerPropertiesUpdate(guest,request); room.IsVisible = false;
        Check(requests == 0, "Public request callbacks are rejected");
        hostSync.OnPlayerPropertiesUpdate(guest,DevChange(PhotonSync.DevRequestKey,new object[] { 1, "bad" }));
        hostSync.OnPlayerPropertiesUpdate(guest,DevChange(PhotonSync.DevRequestKey,new object[] { 1L, nonce }));
        Check(requests == 0, "Malformed nonce and version are rejected");
        hostSync.OnPlayerPropertiesUpdate(guest,request);
        Check(requests == 1 && requester == 2 && requestedNonce == nonce, "Host receives actual requester and nonce");
        hostSync.OnPlayerPropertiesUpdate(guest,request);
        Check(requests == 1, "Duplicate request callback does not repeat host action");
        Check(!hostSync.ReplyRefill(2,Id(777),true), "Reply must match outstanding nonce");
        room.IsVisible = true; before = host.Sent.Count;
        Check(!hostSync.ReplyRefill(2,nonce,true) && host.Sent.Count == before, "Public replies cannot write"); room.IsVisible = false;
        PhotonNetwork.InRoom = false;
        Check(!hostSync.ReplyRefill(2,nonce,true), "Offline replies cannot write"); PhotonNetwork.InRoom = true;
        PhotonNetwork.LocalPlayer = guest;
        Hashtable reply = DevChange(PhotonSync.DevReplyKey,new object[] { 1, 2, nonce, true });
        guestSync.OnPlayerPropertiesUpdate(other,reply);
        guestSync.OnPlayerPropertiesUpdate(new Player { ActorNumber = 1, IsMasterClient = true },reply);
        guestSync.OnPlayerPropertiesUpdate(host,DevChange(PhotonSync.DevReplyKey,new object[] { 1, 2, Id(999), true }));
        guestSync.OnPlayerPropertiesUpdate(host,DevChange(PhotonSync.DevReplyKey,new object[] { 1, 3, nonce, true }));
        guestSync.OnPlayerPropertiesUpdate(host,DevChange(PhotonSync.DevReplyKey,new object[] { 1, 2, nonce, "true" }));
        Check(results == 0, "Spoofed, wrong nonce, wrong actor, and malformed acknowledgments are ignored");
        PhotonNetwork.LocalPlayer = host; host.FailSend = true;
        Check(!hostSync.ReplyRefill(2,nonce,true), "Failed acknowledgment remains replyable"); host.FailSend = false;
        Check(hostSync.ReplyRefill(2,nonce,true), "Host can acknowledge accepted refill");
        Check(!hostSync.ReplyRefill(2,nonce,true), "Reply cannot be issued twice");
        reply = host.Sent[host.Sent.Count-1];
        PhotonNetwork.LocalPlayer = guest; guestSync.OnPlayerPropertiesUpdate(host,reply); guestSync.OnPlayerPropertiesUpdate(host,reply);
        Check(results == 1 && acceptedResults == 1, "Matching master acknowledgment resolves once");
        Check(!guestSync.RequestRefill(1.99f) && guestSync.RequestRefill(2), "Request rate limit is exactly two seconds");
        Hashtable second = guest.Sent[guest.Sent.Count-1];
        PhotonNetwork.LocalPlayer = host; hostSync.OnPlayerPropertiesUpdate(guest,second); hostSync.OnPlayerPropertiesUpdate(guest,request);
        Check(requests == 2, "Old request cannot replay after a newer nonce");
        string secondNonce = (string)((object[])second[PhotonSync.DevRequestKey])[1];
        Check(hostSync.ReplyRefill(2,secondNonce,false), "Host can deny a request"); reply = host.Sent[host.Sent.Count-1];
        PhotonNetwork.LocalPlayer = guest; guestSync.OnPlayerPropertiesUpdate(host,reply);
        Check(results == 2 && acceptedResults == 1, "Denial reaches requester without acceptance");
        Check(guestSync.RequestRefill(4), "Request after cooldown succeeds");
        guestSync.Tick(false,1,9.999f); Check(results == 2, "Pending request remains before six seconds");
        guestSync.Tick(false,1,10); Check(results == 3 && acceptedResults == 1, "Six-second timeout resolves false");
        guestSync.Tick(false,1,11); Check(results == 3, "Timeout callback occurs once");
        Check(guestSync.RequestRefill(12), "New request can follow timeout");
        guestSync.OnMasterClientSwitched(other);
        Check(results == 4 && acceptedResults == 1, "Master switch cancels pending request");
        Check(guestSync.RequestRefill(14), "Master cancellation allows later request");
        guestSync.Reset();
        Check(results == 5 && !guest.CustomProperties.ContainsKey(PhotonSync.DevRequestKey) && !guest.CustomProperties.ContainsKey(PhotonSync.DevReplyKey), "Reset clears pending request and protocol properties");
        Check(guestSync.RequestRefill(16), "Request can restart after explicit reset");
        object[] activeRequest = (object[])guest.Sent[guest.Sent.Count-1][PhotonSync.DevRequestKey];
        host.IsMasterClient = false; other.IsMasterClient = true;
        guestSync.OnPlayerPropertiesUpdate(host,DevChange(PhotonSync.DevReplyKey,new object[] { 1, 2, activeRequest[1], true }));
        Check(results == 5, "Former host cannot acknowledge pending request after authority changes");
        guestSync.Tick(false,1,16);
        Check(results == 6 && acceptedResults == 1, "Polling also cancels requests when current master changes");
        PhotonNetwork.LocalPlayer = host;
        Check(!hostSync.ReplyRefill(2,(string)activeRequest[1],true), "Former master cannot reply");
        host.IsMasterClient = true; other.IsMasterClient = false;
        hostSync.Reset();
        Check(!host.CustomProperties.ContainsKey(PhotonSync.DevReplyKey), "Host reset clears reply property");
        hostSync.Dispose(); PhotonNetwork.LocalPlayer = guest; guestSync.Dispose();
        Check(PhotonNetwork.Targets == 0, "Dev protocol callbacks clean up");
    }
    public static int Main()
    {
        for (int bits = 0; bits < 8; bits++)
            Check(RoomAccess.Allows((bits&1)!=0, (bits&2)!=0, (bits&4)!=0) == (bits == 7), "Only a connected modded room may activate.");
        TotemSnapshot packet;
        Check(TotemSnapshot.TryDecode(new TotemSnapshot(Id(1), true, 0, 1.2f).Encode(), out packet) && packet.Equipped && packet.Size == 1.2f, "Packet round trip.");
        object[] invalid = { null, "fake", new object[0], new object[] { 3, Id(1), true, 0, 1f },
            new object[] { 2, "not-a-session", true, 0, 1f }, new object[] { 2, Id(1), true, -1, 1f },
            new object[] { 2, Id(1), true, 0, float.NaN }, new object[] { 2, Id(1), true, 0, float.PositiveInfinity },
            new object[] { 2, Id(1), true, 0, 99f }, new object[] { 2, Id(1), true, "0", 1f } };
        foreach (object value in invalid) Check(!TotemSnapshot.TryDecode(value, out packet), "Malformed packet rejected.");
        RemoteState remote = new RemoteState();
        Check(!remote.Apply(new TotemSnapshot(Id(1), true, 7, 1f)) && remote.Snapshot.Equipped, "Late join shows equipped state without replaying old pops.");
        Check(remote.Apply(new TotemSnapshot(Id(1), false, 8, 1f)), "New pop is delivered.");
        Check(!remote.Apply(new TotemSnapshot(Id(1), false, 8, 1f)), "Duplicate pop is ignored.");
        Check(!remote.Apply(new TotemSnapshot(Id(1), true, 7, 1f)) && !remote.Snapshot.Equipped, "Stale pop counter cannot restore an old totem.");
        Check(!remote.Apply(new TotemSnapshot(Id(2), true, 0, 1f)), "Plugin restart establishes a fresh baseline.");
        PopDeduplicator effects = new PopDeduplicator();
        Check(effects.Accept(Id(1), 1), "Host save starts one remote pop.");
        Check(!effects.Accept(Id(1), 1), "Owner acknowledgment never replays the same pop, regardless of network delay.");
        Check(effects.Accept(Id(1), 2), "A new equipped totem can pop again.");
        Check(!effects.Accept(Id(1), 1), "Older acknowledgments cannot replay after another pop.");
        Check(effects.Accept(Id(2), 1), "A new plugin session has an independent pop sequence.");

        Room room = new Room();
        Player a = new Player { ActorNumber = 1, IsMasterClient = true };
        Player b = new Player { ActorNumber = 2 };
        room.Players.Add(1, a); room.Players.Add(2, b);
        bool enabled = false;
        Dictionary<int, RemoteState> aView = new Dictionary<int, RemoteState>();
        Dictionary<int, RemoteState> bView = new Dictionary<int, RemoteState>();
        int aPops = 0, bPops = 0, rescues = 0;
        Action<Dictionary<int, RemoteState>, int, TotemSnapshot, Action> receive = (view, actor, snap, popped) => {
            RemoteState record; if (!view.TryGetValue(actor, out record)) { record = new RemoteState(); view.Add(actor, record); }
            if (record.Apply(snap)) popped();
        };
        PhotonNetwork.CurrentRoom = room; PhotonNetwork.LocalPlayer = a; PhotonNetwork.InRoom = false;
        PhotonSync sender = new PhotonSync(() => RoomAccess.Allows(PhotonNetwork.InRoom, PhotonNetwork.CurrentRoom != null, enabled),
            (actor, snap) => receive(aView, actor, snap, () => aPops++), actor => aView.Remove(actor), () => aView.Clear(), (id,p) => rescues++);
        PhotonSync receiver = new PhotonSync(() => RoomAccess.Allows(PhotonNetwork.InRoom, PhotonNetwork.CurrentRoom != null, enabled),
            (actor, snap) => receive(bView, actor, snap, () => bPops++), actor => bView.Remove(actor), () => bView.Clear(), (id,p) => rescues++);
        sender.Tick(true, 1f, 0f);
        Check(a.Sent.Count == 0, "Offline input never publishes a totem.");
        PhotonNetwork.InRoom = true;
        sender.Tick(true, 1f, 0f);
        Check(a.Sent.Count == 0, "Vanilla-room input never publishes a totem.");
        enabled = true; sender.Tick(false, 1f, 0f);
        Check(a.Sent.Count == 1, "Activation publishes a hidden baseline.");
        sender.Tick(false, 1f, 1f);
        Check(a.Sent.Count == 1, "Unchanged state is not sent every frame.");
        PhotonNetwork.LocalPlayer = b; receiver.Tick(false, 1f, 0f);
        Check(bView.ContainsKey(1) && !bView[1].Snapshot.Equipped, "New peer reads current player properties.");
        PhotonNetwork.LocalPlayer = a; sender.Tick(true, 1.4f, 2f);
        PhotonNetwork.LocalPlayer = b; receiver.OnPlayerPropertiesUpdate(a, a.Sent[a.Sent.Count-1]);
        Check(bView[1].Snapshot.Equipped && bView[1].Snapshot.Size == 1.4f, "Friend sees equip and configured size.");
        PhotonNetwork.LocalPlayer = a; sender.Tick(false, 1.4f, 2.2f);
        PhotonNetwork.LocalPlayer = b; receiver.OnPlayerPropertiesUpdate(a, a.Sent[a.Sent.Count-1]);
        Check(!bView[1].Snapshot.Equipped && bPops == 0, "Unequipping hides the friend's totem without a pop or sound event.");
        PhotonNetwork.LocalPlayer = a; sender.Tick(true, 1.4f, 2.4f);
        PhotonNetwork.LocalPlayer = b; receiver.OnPlayerPropertiesUpdate(a, a.Sent[a.Sent.Count-1]);
        Check(bView[1].Snapshot.Equipped && bPops == 0, "Re-equipping restores the friend's totem without spending it.");
        PhotonNetwork.LocalPlayer = a; sender.Pop(1.4f, 3f);
        PhotonNetwork.LocalPlayer = b; receiver.OnPlayerPropertiesUpdate(a, a.Sent[a.Sent.Count-1]);
        receiver.OnPlayerPropertiesUpdate(a, a.Sent[a.Sent.Count-1]);
        Check(bPops == 1 && !bView[1].Snapshot.Equipped, "Friend sees one pop, duplicate callbacks do not replay it.");
        receiver.OnPlayerPropertiesUpdate(b, b.Sent[0]);
        Check(!bView.ContainsKey(2), "Own properties cannot create a second local totem.");
        Player spoof = new Player { ActorNumber = 1 };
        spoof.CustomProperties[TotemSnapshot.PropertyKey] = new TotemSnapshot(Id(99), true, 0, 1f).Encode();
        receiver.OnPlayerPropertiesUpdate(spoof, a.Sent[0]);
        Check(bView[1].Snapshot.Session != Id(99), "Callback actor must belong to this room.");
        receiver.OnPlayerLeftRoom(a);
        Check(!bView.ContainsKey(1), "Leaving friend is cleaned up.");

        // B equips, then the current host announces a save for that exact session.
        receiver.Tick(true, 1f, 4f);
        string bSession = receiver.LocalSession;
        PhotonNetwork.LocalPlayer = a; sender.AnnounceRescue(2, bSession, 0);
        Hashtable notice = a.Sent[a.Sent.Count-1];
        PhotonNetwork.LocalPlayer = b; receiver.OnPlayerPropertiesUpdate(a, notice);
        Check(rescues == 1, "Host can announce a save for this equipped owner's session.");
        receiver.Pop(1f, 5f);
        receiver.OnPlayerPropertiesUpdate(a, notice);
        Check(rescues == 1, "Consumed/old rescue notices cannot pop a new totem.");
        Player outsider = new Player { ActorNumber = 3 };
        room.Players.Add(3, outsider);
        receiver.OnPlayerPropertiesUpdate(outsider, notice);
        Check(rescues == 1, "Non-host rescue announcements are ignored.");
        room.IsVisible = true; receiver.OnPlayerPropertiesUpdate(a, notice);
        Check(rescues == 1, "Gameplay saves are ignored outside private rooms.");
        room.IsVisible = false;

        PhotonNetwork.LocalPlayer = a; a.FailSend = true;
        int before = a.Sent.Count; sender.Tick(true, 1f, 6f);
        Check(a.Sent.Count == before, "Failed send remains pending.");
        a.FailSend = false; sender.Tick(true, 1f, 7f);
        Check(a.Sent.Count == before+1, "Failed publication retries without requiring another button press.");
        PhotonNetwork.LocalPlayer = b; enabled = false;
        receiver.Tick(true, 1f, 8f);
        Check(bView.Count == 0 && !b.CustomProperties.ContainsKey(TotemSnapshot.PropertyKey), "Leaving modded mode clears visuals and cached state.");
        before = b.Sent.Count; receiver.Tick(true, 1f, 9f);
        Check(b.Sent.Count == before, "Cleanup does not flood the network.");
        PhotonNetwork.InRoom = false; receiver.OnLeftRoom();
        receiver.Dispose(); PhotonNetwork.LocalPlayer = a; sender.Dispose();
        Check(PhotonNetwork.Targets == 0, "All Photon callbacks are unregistered on unload.");
        enabled = true; PhotonNetwork.InRoom = true; PhotonNetwork.LocalPlayer = a;
        RoomRules receivedRules = null;
        PhotonSync hostRules = new PhotonSync(() => enabled,(actor,snap) => {},actor => {},() => {},(id,p) => {});
        PhotonSync guestRules = new PhotonSync(() => enabled,(actor,snap) => {},actor => {},() => {},(id,p) => {},value => receivedRules = value);
        RoomRules scrims = RoomRules.ForPreset(1,Id(700),0);
        Check(hostRules.PublishRules(scrims),"Private host can publish Scrims rules.");
        PhotonNetwork.LocalPlayer = b; guestRules.Tick(false,1f,10f);
        Check(receivedRules != null && receivedRules.Capacity == 3 && receivedRules.RoundSeconds == 180,"Late join reads the host preset baseline.");
        Check(!guestRules.PublishRules(scrims),"Nonhost cannot publish gameplay rules.");
        receivedRules = null;
        outsider.CustomProperties[RoomRules.PropertyKey] = scrims.Encode();
        Hashtable changedRules = new Hashtable(); changedRules[RoomRules.PropertyKey] = scrims.Encode();
        guestRules.OnPlayerPropertiesUpdate(outsider,changedRules);
        Check(receivedRules == null,"Nonhost property updates cannot change room rules.");
        PhotonNetwork.LocalPlayer = a;
        RoomRules started = scrims.Start(100);
        hostRules.PublishRules(started);
        PhotonNetwork.LocalPlayer = b; guestRules.OnPlayerPropertiesUpdate(a,changedRules);
        Check(receivedRules != null && receivedRules.EndsAt == 280 && receivedRules.Remaining(160) == 120,"Peers receive the same shared-clock deadline.");
        room.IsVisible = true; receivedRules = null;
        guestRules.OnPlayerPropertiesUpdate(a,changedRules);
        Check(receivedRules == null,"Public-room rule packets are ignored.");
        PhotonNetwork.LocalPlayer = a;
        Check(!hostRules.PublishRules(scrims),"Public-room rule publication is disabled.");
        room.IsVisible = false; enabled = false; receivedRules = null;
        PhotonNetwork.LocalPlayer = b; guestRules.OnPlayerPropertiesUpdate(a,changedRules);
        Check(receivedRules == null,"Nonmodded rule callbacks are ignored.");
        guestRules.Dispose(); PhotonNetwork.LocalPlayer = a; hostRules.Dispose();
        Check(!a.CustomProperties.ContainsKey(RoomRules.PropertyKey) && PhotonNetwork.Targets == 0,"Unload clears shared rules and callbacks.");
        DevRefills();
        checks += DeveloperCommandTests.Run();
        Commands();
        Console.WriteLine("PASS " + checks + " modded-room/protocol/two-peer/property/replay/host-notice/cleanup checks");
        return 0;
    }
}
