using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace TotemMod
{
    public sealed partial class TotemPlugin
    {
        private readonly List<int> goneFriends = new List<int>();
        private float noticeTickAt;
        private float SafeSize()
        {
            float value = size.Value;
            return float.IsNaN(value) || float.IsInfinity(value) ? 1f : Mathf.Clamp(value, 0.25f, 3f);
        }

        private void PositionHeld(GameObject totem, Transform hand, float scale)
        {
            if (totem.transform.parent != hand) totem.transform.SetParent(hand, false);
            float held = heldScale.Value;
            if (float.IsNaN(held) || float.IsInfinity(held)) held = 0.75f;
            held = Mathf.Clamp(held, 0.25f, 1.5f) * scale;
            Quaternion rotation = Quaternion.Euler(handRotation.Value);
            totem.transform.localRotation = rotation;
            totem.transform.localScale = Vector3.one * held;
            // Center on the palm. A lower-third model offset lifts the item
            // above the hand as the hand rotates, instead of gripping it.
            totem.transform.localPosition = handPosition.Value;
        }

        private void ShowNotice(string message, bool empty)
        {
            if (Allowed && hudEnabled.Value) notice.Show(message, Time.unscaledTime, empty);
        }

        private void UpdateNotice()
        {
            if (Time.unscaledTime < noticeRetryAt || Time.unscaledTime < noticeTickAt) return;
            noticeTickAt = Time.unscaledTime + 0.1f;
            if (!hudEnabled.Value) { notice.Hide(); return; }
            Transform head = GetHead();
            Camera camera = head != null ? head.GetComponent<Camera>() : null;
            if (camera == null) return;
            try
            {
                string timer = hudShowTimer.Value && rules.Phase == 1 ? "ROUND " + ClockText(rules.Remaining(PhotonNetwork.Time)) + " / " + ClockText(rules.RoundSeconds) : null;
                if (hudPersistentStock.Value) timer=(timer!=null ? timer+"\n" : "")+"STOCK "+StockText();
                notice.Configure(hudCountdown.Value,SafeFloat(hudScale.Value,1,0.6f,1.5f),SafeFloat(hudDuration.Value,5,2,10),hudOffset.Value,inventory.Capacity,timer,notifyRegen.Value);
                notice.Tick(camera, Time.unscaledTime, inventory.Count, inventory.SecondsUntilNext(Time.unscaledTime));
            }
            catch (System.Exception error)
            {
                notice.Hide(); noticeRetryAt = Time.unscaledTime + 10f;
                Log.LogWarning("Totem notification will retry: " + error.Message);
            }
        }

        private static Transform GetFriendHand(int actor, FriendTotem friend)
        {
            if (friend.CachedHand!=null && Time.unscaledTime<friend.HandLookupAt) return friend.CachedHand;
            friend.HandLookupAt=Time.unscaledTime+0.5f;
            if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null || GorillaGameManager.instance == null) return null;
            Player player = PhotonNetwork.CurrentRoom.GetPlayer(actor);
            if (player == null || player.IsLocal || player.IsInactive) return null;
            NetPlayer net = NetPlayer.Get(player);
            if (net == null || !net.IsValid) return null;
            VRRig rig = GorillaGameManager.instance.FindPlayerVRRig(net);
            friend.CachedHand=rig != null ? friend.Palm.Resolve(rig.transform, rig.leftHandTransform) : null;
            return friend.CachedHand;
        }

        private void ReceiveFriend(int actor, TotemSnapshot packet)
        {
            if (!Allowed) return;
            FriendTotem friend;
            if (!friends.TryGetValue(actor, out friend))
            {
                friend = new FriendTotem();
                friend.Supply.Configure(rules.Capacity,rules.Regeneration,Time.unscaledTime);
                friends.Add(actor, friend);
            }
            if (friend.State.Apply(packet)) QueueFriendPop(friend, packet.Session, packet.Pops);
            if (!friend.State.Snapshot.Equipped && friend.Held != null)
            {
                UnityEngine.Object.Destroy(friend.Held); friend.Held = null;
            }
        }

        private void QueueFriendPop(FriendTotem friend, string session, int popCounter)
        {
            if (!friend.Effects.Accept(session, popCounter)) return;
            friend.PendingPopUntil = Time.unscaledTime + 1.5f;
            if (friend.Held != null) UnityEngine.Object.Destroy(friend.Held);
            friend.Held = null;
        }

        private void RemoveFriend(int actor)
        {
            FriendTotem friend;
            if (!friends.TryGetValue(actor, out friend)) return;
            if (friend.Held != null) UnityEngine.Object.Destroy(friend.Held);
            friends.Remove(actor);
        }

        private void ClearFriends()
        {
            foreach (FriendTotem friend in friends.Values)
                if (friend.Held != null) UnityEngine.Object.Destroy(friend.Held);
            friends.Clear();
        }

        private void UpdateFriends()
        {
            if (friends.Count == 0) return;
            goneFriends.Clear();
            Transform viewer = GetHead();
            float range = SafeFloat(friendDistance.Value,20,5,50);
            int avatars=0;
            foreach (KeyValuePair<int, FriendTotem> entry in friends)
            {
                Player player = PhotonNetwork.CurrentRoom.GetPlayer(entry.Key);
                FriendTotem friend = entry.Value;
                if (player == null || player.IsInactive) { goneFriends.Add(entry.Key); continue; }
                TotemSnapshot packet = friend.State.Snapshot;
                if (packet==null || !packet.Equipped && friend.PendingPopUntil<=Time.unscaledTime)
                {
                    if (friend.Held!=null) UnityEngine.Object.Destroy(friend.Held);
                    friend.Held=null; continue;
                }
                if (!showFriends.Value)
                { if (friend.Held!=null) friend.Held.SetActive(false); friend.PendingPopUntil=0; continue; }
                Transform hand = GetFriendHand(entry.Key, friend);
                if (hand == null)
                {
                    if (friend.Held != null) UnityEngine.Object.Destroy(friend.Held);
                    friend.Held = null;
                    continue;
                }
                float distance=viewer!=null ? (hand.position-viewer.position).sqrMagnitude : 0;
                if (distance > range*range)
                {
                    if (friend.Held != null) friend.Held.SetActive(false);
                    friend.PendingPopUntil = 0;
                    continue;
                }
                BetaAppearance appearance=FriendBeta(entry.Key,friend);
                bool allowAvatar=appearance==null || appearance.Style!=BetaTotemVisuals.PlayerModel || avatars<3 && distance<=144;
                if (appearance!=null && appearance.Style==BetaTotemVisuals.PlayerModel && allowAvatar) avatars++;
                if (friend.Held!=null && (!SameVisual(friend.RenderAppearance,appearance) || friend.AvatarAllowed!=allowAvatar || friend.AvatarFallback && allowAvatar && Time.unscaledTime>=friend.NextBetaRetryAt))
                { UnityEngine.Object.Destroy(friend.Held); friend.Held=null; }
                bool used = friend.ConsumedSession == packet.Session && friend.ConsumedPops == packet.Pops;
                if ((packet.Equipped && !used) || friend.PendingPopUntil > Time.unscaledTime)
                {
                    if (!EnsureAssets()) continue;
                    if (friend.PendingPopUntil > Time.unscaledTime)
                    {
                        friend.PendingPopUntil = 0f;
                        if (fx.Value) StartCoroutine(PopAnimation(hand, false, packet.Size*SafeFloat(friendRenderScale.Value,1,0.5f,1.5f),allowAvatar ? appearance : null,entry.Key));
                    }
                    if (packet.Equipped && !used)
                    {
                        if (friend.Held == null)
                        {
                            friend.Held=CreateAppearanceVisual(hand,"Friend totem",appearance,entry.Key,allowAvatar);
                            friend.RenderAppearance=appearance; friend.AvatarAllowed=allowAvatar;
                            friend.TintTargets=friend.Held.GetComponentsInChildren<Renderer>();
                            friend.AvatarFallback=appearance!=null && appearance.Style==BetaTotemVisuals.PlayerModel && friend.Held.GetComponent<BetaTotemVisualOwner>()==null;
                            friend.NextBetaRetryAt=Time.unscaledTime+2;
                        }
                        if (!friend.Held.activeSelf) friend.Held.SetActive(true);
                        PositionHeld(friend.Held, hand, packet.Size*SafeFloat(friendRenderScale.Value,1,0.5f,1.5f));
                        if (appearance!=null)
                        {
                            ApplyAppearanceHeld(friend.Held,appearance);
                            if (Time.unscaledTime>=friend.NextTintAt) { ApplyAppearanceTint(friend.Held,friend.TintTargets,appearance); friend.NextTintAt=Time.unscaledTime+0.1f; }
                        }
                    }
                }
            }
            foreach (int actor in goneFriends) RemoveFriend(actor);
        }
    }
}
