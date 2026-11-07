using Photon.Pun;

namespace TotemMod
{
    public sealed partial class TotemPlugin
    {
        internal bool CanHostProtect(GorillaTagManager manager)
        {
            return protectionReady && isActiveAndEnabled && Allowed && RulesPermitProtection() && PhotonNetwork.IsMasterClient &&
                NetworkSystem.Instance != null && NetworkSystem.Instance.IsMasterClient &&
                PhotonNetwork.CurrentRoom != null && !PhotonNetwork.CurrentRoom.IsVisible &&
                manager != null && manager.GetType() == typeof(GorillaTagManager);
        }

        internal ProtectionTicket ReserveProtection(GorillaTagManager manager, NetPlayer player)
        {
            if (!CanHostProtect(manager) || player == null || !player.IsValid || manager.LocalIsTagged(player)) return null;
            if (player.IsLocal)
                return state.Equipped && inventory.Count > 0 && sync.LocalSession != null ? new ProtectionTicket(player, sync.LocalSession, sync.LocalPops) : null;
            FriendTotem friend;
            if (!friends.TryGetValue(player.ActorNumber, out friend)) return null;
            TotemSnapshot packet = friend.State.Snapshot;
            friend.Supply.Advance(UnityEngine.Time.unscaledTime);
            if (friend.Supply.Count == 0) return null;
            if (packet == null || !packet.Equipped || (friend.ConsumedSession == packet.Session && friend.ConsumedPops == packet.Pops)) return null;
            return new ProtectionTicket(player, packet.Session, packet.Pops);
        }

        internal bool CommitProtection(GorillaTagManager manager, ProtectionTicket ticket)
        {
            if (!CanHostProtect(manager) || ticket == null) return false;
            if (ticket.Player.IsLocal)
            {
                if (sync.LocalSession != ticket.Session || sync.LocalPops < ticket.Pops || sync.LocalPops > ticket.Pops + 1) return false;
                // Infection broadcasts precede AddInfectedPlayer, so the visual
                // observer may already have popped the host's local totem.
                PopFromTag();
            }
            else
            {
                FriendTotem friend;
                if (!friends.TryGetValue(ticket.Player.ActorNumber, out friend)) return false;
                TotemSnapshot packet = friend.State.Snapshot;
                if (packet.Session != ticket.Session || packet.Pops != ticket.Pops || !packet.Equipped ||
                    (friend.ConsumedSession == packet.Session && friend.ConsumedPops == packet.Pops)) return false;
                if (!friend.Supply.Consume(UnityEngine.Time.unscaledTime)) return false;
                friend.ConsumedSession = packet.Session; friend.ConsumedPops = packet.Pops;
                QueueFriendPop(friend, ticket.Session, ticket.Pops + 1);
                sync.AnnounceRescue(ticket.Player.ActorNumber, ticket.Session, ticket.Pops);
            }
            Log.LogInfo("Host totem save accepted for actor " + ticket.Player.ActorNumber + "; tagger stays infected, target stays untagged.");
            return true;
        }

        private void ReceiveRescue(string session, int pops)
        {
            if (!Allowed || PhotonNetwork.CurrentRoom.IsVisible || session != sync.LocalSession || pops != sync.LocalPops) return;
            PopFromTag();
        }
    }
}
