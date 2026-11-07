using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;

namespace TotemMod
{
    // Only our namespaced local-player property is written. Actor identity comes
    // from Photon's callback/player list, never from an actor ID in the payload.
    internal sealed class PhotonSync : IInRoomCallbacks, IMatchmakingCallbacks, IDisposable
    {
        private readonly Func<bool> allowed;
        private readonly Action<int, TotemSnapshot> received;
        private readonly Action<int> removed;
        private readonly Action cleared;
        private readonly Action<string, int> rescued;
        private readonly Action<RoomRules> rulesReceived;
        private readonly Action<int, string> refillRequested;
        private readonly Action<bool> refillResult;
        private readonly Func<int, bool> authorizeDeveloper;
        private readonly Action<int, string, DeveloperCommand> commandReceived;
        private readonly Action<bool> commandResult;
        private readonly Func<byte[], byte[]> signCommand;
        private readonly Func<int, byte[], byte[], bool> verifyCommand;
        internal const string CommandSessionKey = "totemmod.gtag.rhys/command-session/v1";
        internal const string CommandRequestKey = "totemmod.gtag.rhys/command-request/v1";
        internal const string CommandReplyKey = "totemmod.gtag.rhys/command-reply/v1";
        private Player commandMaster;
        private string commandSession, pendingCommandNonce, pendingCommandSession;
        private bool commandSessionQueued;
        private Player pendingCommandMaster;
        private int commandSequence;
        private float pendingCommandAt, commandAfter = float.NegativeInfinity;
        private sealed class CommandStamp
        {
            public readonly double Time;
            public readonly int Sequence;
            public CommandStamp(double time, int sequence) { Time = time; Sequence = sequence; }
        }
        private readonly Dictionary<int, CommandStamp> commandSeen = new Dictionary<int, CommandStamp>();
        private readonly Dictionary<int, string> commandRequests = new Dictionary<int, string>();
        internal const string RescueKey = "totemmod.gtag.rhys/rescue/v2";
        internal const string DevRequestKey = "totemmod.gtag.rhys/dev-request/v1";
        internal const string DevReplyKey = "totemmod.gtag.rhys/dev-reply/v1";
        private readonly Dictionary<int, HashSet<string>> devSeen = new Dictionary<int, HashSet<string>>();
        private readonly Dictionary<int, string> devRequests = new Dictionary<int, string>();
        private Room devRoom;
        private Player pendingMaster;
        private string pendingNonce;
        private float pendingAt;
        private float requestAfter = float.NegativeInfinity;
        private Room boundRoom;
        private string session;
        private int pops;
        private bool visible;
        private float size = 1f;
        private bool dirty;
        private bool disposed;
        private float retryAt;
        private bool cleanupSent;
        public string LocalSession { get { return session; } }
        public int LocalPops { get { return pops; } }

        public PhotonSync(Func<bool> allowed, Action<int, TotemSnapshot> received, Action<int> removed, Action cleared, Action<string, int> rescued,
            Action<RoomRules> rulesReceived = null, Action<int, string> refillRequested = null, Action<bool> refillResult = null,
            Func<int, bool> authorizeDeveloper = null, Action<int, string, DeveloperCommand> commandReceived = null,
            Action<bool> commandResult = null, Func<byte[], byte[]> signCommand = null, Func<int, byte[], byte[], bool> verifyCommand = null)
        {
            this.allowed = allowed; this.received = received; this.removed = removed; this.cleared = cleared;
            this.rescued = rescued;
            this.rulesReceived = rulesReceived;
            this.refillRequested = refillRequested; this.refillResult = refillResult;
            this.authorizeDeveloper = authorizeDeveloper; this.commandReceived = commandReceived;
            this.commandResult = commandResult; this.signCommand = signCommand; this.verifyCommand = verifyCommand;
            PhotonNetwork.AddCallbackTarget(this);
        }

        public void Tick(bool equipped, float scale, float now)
        {
            if (disposed) return;
            if (!allowed()) { Reset(); return; }
            MaintainDevRoom();
            MaintainCommandHost();
            if (pendingCommandNonce != null && (!DevAllowed() || pendingCommandMaster != CurrentMaster() ||
                pendingCommandSession != HostCommandSession(CurrentMaster()) ||
                (!float.IsNaN(now) && !float.IsInfinity(now) && now - pendingCommandAt >= 6f))) CompleteCommand(false);
            if (pendingNonce != null && (!DevAllowed() || pendingMaster != CurrentMaster() ||
                (!float.IsNaN(now) && !float.IsInfinity(now) && now - pendingAt >= 6f))) CompleteRefill(false);
            if (boundRoom != PhotonNetwork.CurrentRoom)
            {
                cleared();
                boundRoom = PhotonNetwork.CurrentRoom;
                session = Guid.NewGuid().ToString("N");
                pops = 0; dirty = true; retryAt = 0f;
                foreach (Player player in boundRoom.Players.Values) { ReadPlayer(player); ReadRules(player); }
            }
            if (visible != equipped || size != scale) dirty = true;
            visible = equipped; size = scale;
            if (dirty && now >= retryAt)
            {
                if (Publish()) dirty = false;
                retryAt = now + 0.1f;
            }
        }

        public void Pop(float scale, float now)
        {
            if (disposed || !allowed()) return;
            // Flush the equip snapshot first if the tag arrived before the next
            // normal update. Both updates use Photon's reliable property channel.
            Tick(true, scale, now);
            if (pops == int.MaxValue) { session = Guid.NewGuid().ToString("N"); pops = 0; }
            pops++;
            visible = false; dirty = true;
            if (Publish()) dirty = false;
        }

        private bool Publish()
        {
            if (disposed || !allowed() || boundRoom == null || boundRoom != PhotonNetwork.CurrentRoom || PhotonNetwork.LocalPlayer == null) return false;
            Hashtable change = new Hashtable();
            change[TotemSnapshot.PropertyKey] = new TotemSnapshot(session, visible, pops, size).Encode();
            bool queued = PhotonNetwork.LocalPlayer.SetCustomProperties(change);
            if (queued) cleanupSent = false;
            return queued;
        }

        public void AnnounceRescue(int actor, string ownerSession, int previousPops)
        {
            if (disposed || !allowed() || !PhotonNetwork.IsMasterClient || PhotonNetwork.CurrentRoom.IsVisible) return;
            Hashtable change = new Hashtable();
            change[RescueKey] = new object[] { 2, actor, ownerSession, previousPops };
            if (PhotonNetwork.LocalPlayer.SetCustomProperties(change)) cleanupSent = false;
        }

        public bool PublishRules(RoomRules rules)
        {
            if (disposed || !allowed() || !PhotonNetwork.IsMasterClient || PhotonNetwork.CurrentRoom.IsVisible || rules == null) return false;
            Hashtable change = new Hashtable(); change[RoomRules.PropertyKey] = rules.Encode();
            bool queued = PhotonNetwork.LocalPlayer.SetCustomProperties(change);
            if (queued) cleanupSent = false;
            return queued;
        }

        private bool DevAllowed()
        {
            Room room = PhotonNetwork.CurrentRoom;
            Player local = PhotonNetwork.LocalPlayer;
            return !disposed && allowed() && PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode && room != null && !room.IsVisible &&
                local != null && !local.IsInactive && room.GetPlayer(local.ActorNumber) == local;
        }

        private Player CurrentMaster()
        {
            Room room = PhotonNetwork.CurrentRoom;
            if (room == null) return null;
            foreach (Player player in room.Players.Values)
                if (player.IsMasterClient && !player.IsInactive) return player;
            return null;
        }

        private void CompleteRefill(bool accepted)
        {
            if (pendingNonce == null) return;
            pendingNonce = null; pendingMaster = null;
            if (refillResult != null) refillResult(accepted);
        }

        private void MaintainDevRoom()
        {
            if (devRoom == PhotonNetwork.CurrentRoom) return;
            CompleteRefill(false);
            ResetCommands();
            devRoom = PhotonNetwork.CurrentRoom;
            devSeen.Clear(); devRequests.Clear(); requestAfter = float.NegativeInfinity;
        }

        public bool RequestRefill(float now)
        {
            if (!DevAllowed() || PhotonNetwork.IsMasterClient || float.IsNaN(now) || float.IsInfinity(now)) return false;
            MaintainDevRoom();
            Player master = CurrentMaster();
            if (master == null || pendingNonce != null || now < requestAfter) return false;
            string nonce = Guid.NewGuid().ToString("N");
            Hashtable change = new Hashtable(); change[DevRequestKey] = new object[] { 1, nonce };
            if (!PhotonNetwork.LocalPlayer.SetCustomProperties(change)) return false;
            pendingNonce = nonce; pendingMaster = master; pendingAt = now; requestAfter = now + 2f; cleanupSent = false;
            return true;
        }

        public bool ReplyRefill(int actor, string nonce, bool accepted)
        {
            if (!DevAllowed() || !PhotonNetwork.IsMasterClient || CurrentMaster() != PhotonNetwork.LocalPlayer) return false;
            MaintainDevRoom();
            string expected;
            Player requester = PhotonNetwork.CurrentRoom.GetPlayer(actor);
            if (requester == null || requester.IsLocal || requester.IsInactive || !devRequests.TryGetValue(actor, out expected) || nonce != expected) return false;
            Hashtable change = new Hashtable(); change[DevReplyKey] = new object[] { 1, actor, nonce, accepted };
            if (!PhotonNetwork.LocalPlayer.SetCustomProperties(change)) return false;
            devRequests.Remove(actor); cleanupSent = false;
            return true;
        }

        private static bool ValidNonce(object value)
        {
            string nonce = value as string;
            Guid parsed;
            return nonce != null && nonce.Length == 32 && Guid.TryParseExact(nonce, "N", out parsed);
        }

        public bool CanRequestCommands
        {
            get { return DevAllowed() && !PhotonNetwork.IsMasterClient && signCommand != null && HostCommandSession(CurrentMaster()) != null; }
        }

        public bool CommandPending { get { return pendingCommandNonce != null; } }
        public string CommandAuthority
        {
            get
            {
                Player master=CurrentMaster();
                string current=HostCommandSession(master);
                return master == null || current == null ? null : master.ActorNumber.ToString()+":"+current;
            }
        }

        private static string HostCommandSession(Player master)
        {
            if (master == null || !master.CustomProperties.ContainsKey(CommandSessionKey)) return null;
            object[] data = master.CustomProperties[CommandSessionKey] as object[];
            return data != null && data.Length == 2 && data[0] is int && (int)data[0] == 1 && ValidNonce(data[1]) ? (string)data[1] : null;
        }

        private void CompleteCommand(bool accepted)
        {
            if (pendingCommandNonce == null) return;
            pendingCommandNonce = null; pendingCommandMaster = null; pendingCommandSession = null;
            if (commandResult != null) commandResult(accepted);
        }

        private void ResetCommands()
        {
            CompleteCommand(false);
            commandMaster = null; commandSession = null; commandSessionQueued = false;
            commandRequests.Clear(); commandSeen.Clear(); commandAfter = float.NegativeInfinity;
        }

        private void MaintainCommandHost()
        {
            Player master = CurrentMaster();
            if (commandMaster != master)
            {
                ResetCommands(); commandMaster = master;
            }
            if (!DevAllowed() || !PhotonNetwork.IsMasterClient || master != PhotonNetwork.LocalPlayer ||
                commandReceived == null || authorizeDeveloper == null || verifyCommand == null) return;
            if (commandSession == null) commandSession = Guid.NewGuid().ToString("N");
            if (commandSessionQueued || HostCommandSession(master) == commandSession) return;
            Hashtable change = new Hashtable(); change[CommandSessionKey] = new object[] { 1, commandSession };
            if (master.SetCustomProperties(change)) { commandSessionQueued = true; cleanupSent = false; }
        }

        // Bind proof to the actual actor and host's room session. Remote UserId
        // visibility depends on room settings, so it cannot be a shared input.
        private static byte[] CommandBytes(Player sender, int host, string hostSession, string nonce, int sequence, double sentAt, DeveloperCommand command)
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write("TotemMod private room command v1");
                writer.Write(hostSession); writer.Write(host); writer.Write(sender.ActorNumber);
                writer.Write(nonce); writer.Write(sequence); writer.Write(sentAt);
                writer.Write((int)command.Kind); writer.Write(command.Value); writer.Write(command.Number);
                writer.Flush(); return stream.ToArray();
            }
        }

        private static bool CommandClock(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0 && value <= 1e12;
        }

        public bool RequestCommand(DeveloperCommand command, float now)
        {
            if (!CanRequestCommands || command == null || float.IsNaN(now) || float.IsInfinity(now) || !CommandClock(PhotonNetwork.Time)) return false;
            MaintainDevRoom(); MaintainCommandHost();
            if (pendingCommandNonce != null || now < commandAfter || commandSequence == int.MaxValue) return false;
            Player master = CurrentMaster(); string hostSession = HostCommandSession(master);
            if (hostSession == null) return false;
            string nonce = Guid.NewGuid().ToString("N"); int sequence = commandSequence + 1; double sentAt = PhotonNetwork.Time;
            byte[] signature;
            try { signature = signCommand(CommandBytes(PhotonNetwork.LocalPlayer, master.ActorNumber, hostSession, nonce, sequence, sentAt, command)); }
            catch { return false; }
            if (signature == null || signature.Length == 0 || signature.Length > 512) return false;
            Hashtable change = new Hashtable();
            change[CommandRequestKey] = new object[] { 1, hostSession, master.ActorNumber, nonce, sequence, sentAt, command.Encode(), signature };
            if (!PhotonNetwork.LocalPlayer.SetCustomProperties(change)) return false;
            commandSequence = sequence; pendingCommandNonce = nonce; pendingCommandSession = hostSession;
            pendingCommandMaster = master; pendingCommandAt = now; commandAfter = now + .25f; cleanupSent = false;
            return true;
        }

        public bool ReplyCommand(int actor, string nonce, bool accepted)
        {
            if (!DevAllowed() || !PhotonNetwork.IsMasterClient || CurrentMaster() != PhotonNetwork.LocalPlayer) return false;
            MaintainDevRoom(); MaintainCommandHost();
            Player requester = PhotonNetwork.CurrentRoom.GetPlayer(actor); string expected;
            if (requester == null || requester.IsLocal || requester.IsInactive || !commandRequests.TryGetValue(actor,out expected) || expected != nonce) return false;
            Hashtable change = new Hashtable(); change[CommandReplyKey] = new object[] { 1, commandSession, actor, nonce, accepted };
            if (!PhotonNetwork.LocalPlayer.SetCustomProperties(change)) return false;
            commandRequests.Remove(actor); cleanupSent = false; return true;
        }

        private void ReadCommand(Player player, Hashtable change)
        {
            if (!DevAllowed() || player.IsLocal || player.IsInactive) return;
            MaintainDevRoom(); MaintainCommandHost();
            if (PhotonNetwork.IsMasterClient && CurrentMaster() == PhotonNetwork.LocalPlayer && change.ContainsKey(CommandRequestKey))
            {
                object[] request = change[CommandRequestKey] as object[];
                DeveloperCommand command;
                if (request == null || request.Length != 8 || !(request[0] is int) || (int)request[0] != 1 ||
                    !(request[1] is string) || (string)request[1] != commandSession || !(request[2] is int) ||
                    (int)request[2] != PhotonNetwork.LocalPlayer.ActorNumber || !ValidNonce(request[3]) ||
                    !(request[4] is int) || (int)request[4] <= 0 || !(request[5] is double) ||
                    !CommandClock((double)request[5]) || !CommandClock(PhotonNetwork.Time) ||
                    PhotonNetwork.Time < (double)request[5] || PhotonNetwork.Time - (double)request[5] >= 6 ||
                    !DeveloperCommand.TryDecode(request[6],out command) || !(request[7] is byte[]) ||
                    ((byte[])request[7]).Length == 0 || ((byte[])request[7]).Length > 512) return;
                int sequence = (int)request[4]; double sentAt = (double)request[5]; string nonce = (string)request[3];
                CommandStamp last;
                if (commandSeen.TryGetValue(player.ActorNumber,out last) &&
                    (sentAt < last.Time || (sentAt == last.Time && sequence <= last.Sequence))) return;
                bool authorized = false;
                try
                {
                    authorized = authorizeDeveloper != null && authorizeDeveloper(player.ActorNumber) && verifyCommand != null &&
                        verifyCommand(player.ActorNumber,CommandBytes(player,PhotonNetwork.LocalPlayer.ActorNumber,commandSession,nonce,sequence,(double)request[5],command),(byte[])request[7]);
                }
                catch { authorized = false; }
                // Invalid proofs must not advance replay state and suppress a
                // later legitimate packet. They receive a denial only.
                commandRequests[player.ActorNumber] = nonce;
                if (!authorized || commandReceived == null) { ReplyCommand(player.ActorNumber,nonce,false); return; }
                commandSeen[player.ActorNumber] = new CommandStamp(sentAt,sequence);
                commandReceived(player.ActorNumber,nonce,command);
            }
            if (pendingCommandNonce == null || player != pendingCommandMaster || player != CurrentMaster() || !change.ContainsKey(CommandReplyKey)) return;
            object[] reply = change[CommandReplyKey] as object[];
            if (reply == null || reply.Length != 5 || !(reply[0] is int) || (int)reply[0] != 1 ||
                !(reply[1] is string) || (string)reply[1] != pendingCommandSession || HostCommandSession(player) != pendingCommandSession ||
                !(reply[2] is int) || (int)reply[2] != PhotonNetwork.LocalPlayer.ActorNumber ||
                !ValidNonce(reply[3]) || (string)reply[3] != pendingCommandNonce || !(reply[4] is bool)) return;
            CompleteCommand((bool)reply[4]);
        }

        private void ReadDev(Player player, Hashtable change)
        {
            if (!DevAllowed() || player.IsLocal || player.IsInactive) return;
            MaintainDevRoom();
            if (PhotonNetwork.IsMasterClient && CurrentMaster() == PhotonNetwork.LocalPlayer && change.ContainsKey(DevRequestKey))
            {
                object[] request = change[DevRequestKey] as object[];
                if (request != null && request.Length == 2 && request[0] is int && (int)request[0] == 1 && ValidNonce(request[1]))
                {
                    string nonce = (string)request[1];
                    HashSet<string> seen;
                    if (!devSeen.TryGetValue(player.ActorNumber, out seen)) { seen = new HashSet<string>(); devSeen.Add(player.ActorNumber, seen); }
                    if (seen.Add(nonce))
                    {
                        devRequests[player.ActorNumber] = nonce;
                        if (refillRequested != null) refillRequested(player.ActorNumber, nonce);
                    }
                }
            }
            if (pendingNonce == null || player != pendingMaster || player != CurrentMaster() || !change.ContainsKey(DevReplyKey)) return;
            object[] reply = change[DevReplyKey] as object[];
            if (reply == null || reply.Length != 4 || !(reply[0] is int) || (int)reply[0] != 1 ||
                !(reply[1] is int) || (int)reply[1] != PhotonNetwork.LocalPlayer.ActorNumber ||
                !ValidNonce(reply[2]) || (string)reply[2] != pendingNonce || !(reply[3] is bool)) return;
            CompleteRefill((bool)reply[3]);
        }

        private void ReadRules(Player player)
        {
            if (rulesReceived == null || !allowed() || player == null || player.IsLocal || !player.IsMasterClient || PhotonNetwork.CurrentRoom.IsVisible ||
                PhotonNetwork.CurrentRoom.GetPlayer(player.ActorNumber) != player || !player.CustomProperties.ContainsKey(RoomRules.PropertyKey)) return;
            RoomRules packet;
            if (RoomRules.TryDecode(player.CustomProperties[RoomRules.PropertyKey],out packet)) rulesReceived(packet);
        }

        private void ReadPlayer(Player player)
        {
            if (!allowed() || player == null || player.IsLocal || PhotonNetwork.CurrentRoom == null ||
                PhotonNetwork.CurrentRoom.GetPlayer(player.ActorNumber) != player) return;
            TotemSnapshot packet;
            if (player.IsInactive || !player.CustomProperties.ContainsKey(TotemSnapshot.PropertyKey) ||
                !TotemSnapshot.TryDecode(player.CustomProperties[TotemSnapshot.PropertyKey], out packet))
            {
                removed(player.ActorNumber);
                return;
            }
            received(player.ActorNumber, packet);
        }

        public void Reset()
        {
            CompleteRefill(false);
            ResetCommands();
            devRoom = null; devRequests.Clear(); devSeen.Clear(); requestAfter = float.NegativeInfinity;
            if (boundRoom != null) cleared();
            boundRoom = null; session = null; pops = 0; visible = false; dirty = false;
            // SetCustomProperties also clears the local cached property when
            // outside a room, so it cannot leak into the next vanilla join.
            Player local = PhotonNetwork.LocalPlayer;
            if (!cleanupSent && local != null && (local.CustomProperties.ContainsKey(TotemSnapshot.PropertyKey) || local.CustomProperties.ContainsKey(RescueKey) || local.CustomProperties.ContainsKey(RoomRules.PropertyKey) || local.CustomProperties.ContainsKey(DevRequestKey) || local.CustomProperties.ContainsKey(DevReplyKey) || local.CustomProperties.ContainsKey(CommandSessionKey) || local.CustomProperties.ContainsKey(CommandRequestKey) || local.CustomProperties.ContainsKey(CommandReplyKey)))
            {
                Hashtable change = new Hashtable(); change[TotemSnapshot.PropertyKey] = null; change[RescueKey] = null; change[RoomRules.PropertyKey] = null;
                change[DevRequestKey] = null; change[DevReplyKey] = null;
                change[CommandSessionKey] = null; change[CommandRequestKey] = null; change[CommandReplyKey] = null;
                cleanupSent = local.SetCustomProperties(change);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            Reset(); disposed = true;
            PhotonNetwork.RemoveCallbackTarget(this);
        }

        public void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
        {
            if (disposed || !allowed() || changedProps == null || targetPlayer == null || PhotonNetwork.CurrentRoom == null || PhotonNetwork.CurrentRoom.GetPlayer(targetPlayer.ActorNumber) != targetPlayer) return;
            ReadDev(targetPlayer, changedProps);
            ReadCommand(targetPlayer, changedProps);
            if (changedProps.ContainsKey(TotemSnapshot.PropertyKey)) ReadPlayer(targetPlayer);
            if (changedProps.ContainsKey(RoomRules.PropertyKey)) ReadRules(targetPlayer);
            if (!targetPlayer.IsMasterClient || targetPlayer.IsLocal || !changedProps.ContainsKey(RescueKey) || PhotonNetwork.CurrentRoom.IsVisible) return;
            object[] notice = changedProps[RescueKey] as object[];
            if (notice == null || notice.Length != 4 || !(notice[0] is int) || (int)notice[0] != 2 || !(notice[1] is int) ||
                !(notice[2] is string) || !(notice[3] is int) || PhotonNetwork.LocalPlayer == null || (int)notice[1] != PhotonNetwork.LocalPlayer.ActorNumber) return;
            if ((string)notice[2] == session && (int)notice[3] == pops) rescued(session, pops);
        }
        public void OnPlayerEnteredRoom(Player newPlayer) { if (!disposed && allowed()) { ReadPlayer(newPlayer); ReadRules(newPlayer); } }
        public void OnPlayerLeftRoom(Player otherPlayer)
        {
            if (otherPlayer == null) return;
            devRequests.Remove(otherPlayer.ActorNumber); devSeen.Remove(otherPlayer.ActorNumber);
            commandRequests.Remove(otherPlayer.ActorNumber); commandSeen.Remove(otherPlayer.ActorNumber);
            if (otherPlayer == pendingCommandMaster) CompleteCommand(false);
            if (otherPlayer == pendingMaster) CompleteRefill(false);
            removed(otherPlayer.ActorNumber);
        }
        public void OnLeftRoom() { Reset(); }
        public void OnPreLeavingRoom() { Reset(); }
        public void OnJoinedRoom() { CompleteRefill(false); ResetCommands(); devRoom = null; boundRoom = null; cleared(); }
        public void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged) { }
        public void OnMasterClientSwitched(Player newMasterClient)
        {
            CompleteRefill(false); devRequests.Clear();
            ResetCommands();
            if (!disposed && allowed()) ReadRules(newMasterClient);
        }
        public void OnFriendListUpdate(List<FriendInfo> friendList) { }
        public void OnCreatedRoom() { }
        public void OnCreateRoomFailed(short returnCode, string message) { }
        public void OnJoinRoomFailed(short returnCode, string message) { }
        public void OnJoinRandomFailed(short returnCode, string message) { }
    }
}
