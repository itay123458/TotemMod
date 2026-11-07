using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GorillaLocomotion;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using UnityEngine.XR;
using Utilla.Attributes;

namespace TotemMod
{
    [BepInPlugin("totemmod.gtag.rhys", "Totem Mod", TotemPlugin.ModVersion)]
    [BepInProcess("Gorilla Tag.exe")]
    [BepInDependency("org.legoandmars.gorillatag.utilla", "1.5.0")]
    [ModdedGamemode]
    public sealed partial class TotemPlugin : BaseUnityPlugin
    {
        internal static TotemPlugin Instance;
        internal static ManualLogSource Log;
        private readonly EquipState state = new EquipState();
        private TotemAssets assets;
        private Harmony harmony;
        private GameObject held;
        private GameObject runtimeRoot;
        private readonly TotemInventory inventory = new TotemInventory();
        private readonly TotemNotice notice = new TotemNotice();
        private readonly PalmAnchor localPalm = new PalmAnchor();
        private readonly TotemParticlePool particlePool = new TotemParticlePool();
        private readonly List<PopParticleBatch> activeBursts=new List<PopParticleBatch>();
        private ConfigEntry<Vector3> handPosition;
        private ConfigEntry<Vector3> handRotation;
        private ConfigEntry<float> heldScale;
        private float noticeRetryAt;
        private ConfigEntry<float> size;
        private ConfigEntry<float> volume;
        private ConfigEntry<bool> fx;
        private ConfigEntry<bool> keyboard;
        private Material gold;
        private Material green;
        private bool inModdedRoom;
        private Photon.Realtime.Room moddedRoom;
        private bool wasTagged;
        private PhotonSync sync;
        private ConfigEntry<bool> protection;
        private bool protectionReady;
        private readonly Dictionary<int, FriendTotem> friends = new Dictionary<int, FriendTotem>();
        private sealed class FriendTotem
        {
            internal BetaAppearance Appearance,RenderAppearance;
            internal Renderer[] TintTargets;
            internal Transform CachedHand;
            internal float HandLookupAt,NextTintAt,NextBetaRetryAt;
            internal bool AvatarFallback,AvatarAllowed;
            internal readonly RemoteState State = new RemoteState();
            internal readonly PopDeduplicator Effects = new PopDeduplicator();
            internal readonly TotemInventory Supply = new TotemInventory();
            internal readonly PalmAnchor Palm = new PalmAnchor();
            internal GameObject Held;
            internal float PendingPopUntil;
            internal string ConsumedSession;
            internal int ConsumedPops = -1;
        }
        private float retryAt;
        private float lastWarning;

        private bool RoomAllowed
        {
            get { return RoomAccess.Allows(PhotonNetwork.InRoom, PhotonNetwork.CurrentRoom != null, inModdedRoom && moddedRoom == PhotonNetwork.CurrentRoom); }
        }
        private bool Allowed { get { return RoomAllowed && (modEnabled == null || modEnabled.Value); } }

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            handPosition = Config.Bind("Hand", "PalmOffset", new Vector3(0f, 0.005f, 0f), "Totem center relative to palm.01.L, in meters. Replaces the older wrist GripPosition setting.");
            handRotation = Config.Bind("Hand", "PalmRotation", new Vector3(-90f, -90f, 0f), "Totem orientation relative to palm.01.L. Replaces the older GripRotation setting.");
            heldScale = Config.Bind("Hand", "HeldScale", 0.75f, "Held size multiplier; pop animation keeps the full configured TotemSize.");
            keyboard = Config.Bind("Controls", "KeyboardPreview", true, "Keyboard Y can equip too, but only in a modded room. No offline preview.");
            protection = Config.Bind("Gameplay", "EnableTotemProtection", true, "Host rule for PRIVATE MODDED standard Infection/Tag only: an equipped totem saves its owner from one incoming tag. The room host must run this mod.");
            size = Config.Bind("General", "TotemSize", 1f, "Totem size multiplier, clamped between 0.25 and 3.");
            volume = Config.Bind("General", "PopVolume", 0.9f, "Minecraft totem pop volume, 0 to 1.");
            fx = Config.Bind("General", "EnablePopFx", true, "Play the Minecraft pop sound, animation and gold/green particles.");
            InitializeSettings();
            InitializeAutoUpdater();
            sync = new PhotonSync(() => isActiveAndEnabled && Allowed, ReceiveFriend, RemoveFriend, ClearFriends, ReceiveRescue,ReceiveRules,ReceiveRefillRequest,ReceiveRefillResult,
                actor => true,ReceiveDeveloperCommand,DeveloperCommandResult,SignDeveloperCommand,VerifyDeveloperCommand);
            InitializeBetaSync();
            harmony = new Harmony("totemmod.gtag.rhys");
            try { Log.LogInfo("Confirmed-tag observers attached: " + TagObserver.Install(harmony)); }
            catch (Exception error) { Log.LogWarning("Tag observers: " + error.Message + ". Tagged-state fallback remains active."); }
            try { HostProtection.Install(harmony); protectionReady = true; }
            catch (Exception error) { Log.LogWarning("Host totem protection unavailable: " + error.Message); }
            Log.LogInfo("Totem Mod "+ModVersion+" loaded. Y equips/unequips. Hold X or press F7 for Totem Settings. MODDED ROOMS ONLY.");
        }

        private bool EnsureAssets()
        {
            if (assets != null) return true;
            if (Time.unscaledTime < retryAt) return false;
            retryAt = Time.unscaledTime + 5f;
            TotemAssets next = new TotemAssets();
            try
            {
                next.Load();
                assets = next;
                runtimeRoot = new GameObject("TotemMod effects");
                UnityEngine.Object.DontDestroyOnLoad(runtimeRoot);
                Shader shader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Standard");
                if (shader == null) throw new InvalidOperationException("Particle shader is unavailable.");
                gold = new Material(shader); gold.color = new Color(1f, 0.8f, 0.15f);
                green = new Material(shader); green.color = new Color(0.25f, 1f, 0.22f);
                gold.name = "Totem gold pixel sparks"; green.name = "Totem emerald pixel sparks";
                // The Standard fallback remains readable in dark maps. Unlit
                // shaders ignore these optional properties.
                foreach (Material material in new[] { gold,green })
                {
                    if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness",0f);
                    if (material.HasProperty("_EmissionColor"))
                    {
                        material.EnableKeyword("_EMISSION");
                        material.SetColor("_EmissionColor",material.color);
                    }
                }
                Log.LogInfo("Minecraft assets ready: " + assets.Mesh.vertexCount + " mesh vertices, " + assets.Pop.length.ToString("F2") + " seconds of pop audio.");
                return true;
            }
            catch (Exception error)
            {
                next.Dispose(); assets = null;
                if (runtimeRoot != null) UnityEngine.Object.Destroy(runtimeRoot);
                UnityEngine.Object.Destroy(gold); UnityEngine.Object.Destroy(green);
                Log.LogWarning("Totem assets will retry: " + error.Message);
                return false;
            }
        }

        private static bool YDown()
        {
            // Do not latch a missing poller: it is normally created after plugins.
            ControllerInputPoller poller = ControllerInputPoller.instance;
            if (poller != null && poller.leftControllerSecondaryButton) return true;
            bool down;
            InputDevice device = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            return device.isValid && device.TryGetFeatureValue(CommonUsages.secondaryButton, out down) && down;
        }

        private bool KeyHeld(KeyCode key)
        {
            if (!keyboard.Value) return false;
            try { return Input.GetKey(key); }
            catch (InvalidOperationException) { return false; }
        }

        private void Update()
        {
            TickAutoUpdater();
            TickMenuControls();
            bool allowed = Allowed;
            bool down = YDown() || KeyHeld(KeyCode.Y);
            int regenerated = inventory.Advance(Time.unscaledTime);
            if (!allowed)
            {
                queuedCommands.Clear();
                ruleHostBefore = false;
                state.Update(false, down, false);
                HideHeld();
                notice.Clear();
                ClearEffects();
                if (!RoomAllowed && menu != null) menu.Close();
                if (sync != null) sync.Reset();
                if (betaSync!=null) betaSync.Reset();
                return;
            }
            TickSettings();
            bool tagged = IsTagged();
            if (tagged && !wasTagged) PopFromTag();
            wasTagged = tagged;
            if (regenerated > 0 && notifyRegen.Value) ShowNotice("Totem regenerated  |  " + StockText() + "\nPress Y to equip", false);
            bool equippedBefore = state.Equipped;
            bool emptyPress = state.Update(true, down, inventory.Count > 0);
            if (emptyPress) ShowNotice("Out of totems", true);
            else if (notifyEquip.Value && !equippedBefore && state.Equipped) ShowNotice("Totem equipped  |  " + StockText(), false);
            else if (notifyEquip.Value && equippedBefore && !state.Equipped) ShowNotice("Totem unequipped  |  " + StockText(), false);
            if (!state.Equipped) HideHeld();
            sync.Tick(state.Equipped, SafeSize(), Time.unscaledTime);
            betaSync.Tick(LocalBetaState(),sync.LocalSession,Time.unscaledTime);
            TickDeveloperCommands();
        }

        private void LateUpdate()
        {
            if (!Allowed) return;
            MaintainBetaSource();
            BetaAppearance current=HasBetaAccess && LocalBetaState().Enabled ? LocalBetaState() : null;
            if (held!=null && !SameVisual(localRenderedAppearance,current)) HideHeld();
            UpdateNotice();
            UpdateFriends();
            if (!state.Equipped) return;
            Transform hand = GetHand();
            if (hand == null)
            {
                HideHeld();
                if (Time.unscaledTime > lastWarning + 10f)
                {
                    lastWarning = Time.unscaledTime;
                    Log.LogInfo("Waiting for the left hand rig; attachment retries automatically.");
                }
                return;
            }
            if (!EnsureAssets()) return;
            if (held == null)
            {
                held = CreateLocalVisual(hand, "Totem (held)",false);
                Log.LogInfo("Totem equipped on left hand: " + hand.name);
            }
            if (held.transform.parent != hand) held.transform.SetParent(hand, false);
            PositionHeld(held, hand, SafeSize());
            ApplyBetaHeld(held);
        }

        private Transform GetHand()
        {
            // Both sides must use the rendered VRRig hand bone, not one side's
            // controller/collision follower with a different origin/orientation.
            GorillaTagger tagger = GorillaTagger.Instance;
            if (tagger != null && tagger.offlineVRRig != null && tagger.offlineVRRig.leftHandTransform != null)
                return localPalm.Resolve(tagger.offlineVRRig.transform, tagger.offlineVRRig.leftHandTransform);
            GTPlayer player = GTPlayer.Instance;
            if (player != null)
            {
                Transform hand = player.GetHandFollower(true);
                if (hand != null) return hand;
            }
            return null;
        }

        private static Transform GetHead()
        {
            GorillaTagger tagger = GorillaTagger.Instance;
            if (tagger != null && tagger.mainCamera != null) return tagger.mainCamera.transform;
            GTPlayer player = GTPlayer.Instance;
            if (player != null && player.headCollider != null) return player.headCollider.transform;
            Camera main = Camera.main;
            return main != null ? main.transform : null;
        }

        private static bool IsTagged()
        {
            if (!PhotonNetwork.InRoom || GorillaGameManager.instance == null || NetworkSystem.Instance == null) return false;
            NetPlayer local = NetworkSystem.Instance.LocalPlayer;
            return local != null && GorillaGameManager.instance.LocalIsTagged(local);
        }

        public void PopFromTag()
        {
            if (!isActiveAndEnabled || !Allowed || !state.Equipped) return;
            if (!inventory.Consume(Time.unscaledTime))
            {
                state.Reset(); HideHeld();
                sync.Tick(false, SafeSize(), Time.unscaledTime);
                ShowNotice("Out of totems", true);
                return;
            }
            state.Consume();
            HideHeld();
            sync.Pop(SafeSize(), Time.unscaledTime);
            if (notifyPop.Value || inventory.Count==0) ShowNotice(inventory.Count == 0 ? "Out of totems" : "Totem popped  |  " + StockText() + " left\nPress Y for another", inventory.Count == 0);
            if (!EnsureAssets()) return;
            Transform head = GetHead();
            if (head == null) return;
            if (fx.Value) StartCoroutine(PopAnimation(head, true, SafeSize()));
            Log.LogInfo("Totem POP: " + StockText() + " left. Press Y again to equip another.");
        }

        private IEnumerator PopAnimation(Transform head, bool local, float effectSize,BetaAppearance appearance=null,int ownerActor=0)
        {
            if (!Allowed || !fx.Value || head == null || assets == null || runtimeRoot == null) yield break;
            GameObject effect = new GameObject("Minecraft Totem pop");
            effect.transform.SetParent(runtimeRoot.transform, false);
            if (local) { ownerActor=PhotonNetwork.LocalPlayer.ActorNumber; appearance=HasBetaAccess ? LocalBetaState() : null; }
            if (appearance!=null && (!appearance.Enabled || !appearance.UseInPop)) appearance=null;
            GameObject icon=CreateAppearanceVisual(effect.transform,"Totem pop icon",appearance,ownerActor);
            float betaIconScale=appearance!=null ? appearance.Scale : 1;
            Renderer[] betaPopRenderers=appearance!=null ? icon.GetComponentsInChildren<Renderer>() : null;
            float nextPopTint=0;
            int layer = PopEffectLayer(local);
            foreach (Transform part in icon.GetComponentsInChildren<Transform>(true)) part.gameObject.layer=layer;
            GameObject sound = new GameObject("Minecraft totem sound");
            sound.transform.SetParent(runtimeRoot.transform, false);
            sound.transform.position = head.position;
            AudioSource audio = sound.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = local ? 0f : 1f;
            audio.minDistance = 0.6f;
            audio.maxDistance = 14f;
            audio.volume = popSound.Value && (local || friendPopSound.Value) ? Mathf.Clamp01(volume.Value) : 0;
            audio.pitch = SafeFloat(popPitch.Value,1,0.5f,1.5f);
            audio.clip = assets.Pop;
            audio.Play();
            UnityEngine.Object.Destroy(sound, assets.Pop.length/audio.pitch + 0.1f);
            if (local && haptics.Value) TryVibrate();

            int animation=popMotion.Value;
            int count = PopAnimationStyle.ParticleCount(particleCount.Value,animation);
            float[] seeds = new float[count];
            Color[] colors=new Color[count];
            float sparkSize = SafeFloat(particleSize.Value,1,0.5f,3f);
            float spread = SafeFloat(particleSpread.Value,1,0.5f,2f);
            int palette = Math.Max(0,Math.Min(3,particlePalette.Value));
            int motion = particleMotion.Value == 1 ? 1 : 0;
            int shape=particleShape.Value;
            float brightness=SafeFloat(particleBrightness.Value,1,0.5f,1.5f);
            float sparkSpin=SafeFloat(particleSpin.Value,1,0,2);
            PopParticleBatch burst=null;
            try
            {
            for (int i = 0; i < count; i++)
            {
                seeds[i] = UnityEngine.Random.value;
                Color tint=PopParticleColor(palette,i,seeds[i])*brightness; tint.a=1;
                colors[i]=tint;
            }
            burst=new PopParticleBatch(effect.transform,count,seeds,colors,shape,layer);
            activeBursts.Add(burst);
            float duration = PopAnimationStyle.Duration(animation);
            float elapsed = 0f;
            while (elapsed < duration && head != null && effect != null && Allowed && fx.Value)
            {
                PopIconPose pose=PopAnimationStyle.Evaluate(elapsed,animation);
                effect.transform.position = local ? head.position + head.forward * SafeFloat(popDistance.Value,0.65f,0.4f,1.2f) + head.up * SafeFloat(popHeight.Value,-0.07f,-0.2f,0.2f) : head.position + Vector3.up * 0.08f;
                Transform viewer = GetHead();
                effect.transform.rotation = local || viewer == null ? head.rotation : Quaternion.LookRotation(head.position - viewer.position, Vector3.up);
                icon.transform.localScale = Vector3.one * pose.Scale * effectSize * betaIconScale;
                icon.transform.localPosition = new Vector3(pose.X,pose.Y,pose.Z);
                icon.transform.localRotation = Quaternion.Euler(pose.Pitch,pose.Yaw,pose.Roll);
                if (appearance!=null) icon.transform.localRotation*=Quaternion.Euler(appearance.TiltX,appearance.TiltY,appearance.TiltZ);
                if (betaPopRenderers!=null && elapsed>=nextPopTint) { ApplyAppearanceTint(icon,betaPopRenderers,appearance); nextPopTint=elapsed+0.1f; }
                burst.Tick(elapsed,sparkSize,spread,motion,sparkSpin,animation);
                elapsed += Time.unscaledDeltaTime * SafeFloat(popSpeed.Value,1,0.5f,2f);
                yield return null;
            }
            }
            finally
            {
                if (burst!=null) { activeBursts.Remove(burst); burst.Dispose(); }
                UnityEngine.Object.Destroy(effect);
                if (!Allowed || !fx.Value) UnityEngine.Object.Destroy(sound);
            }
        }

        private static Color PopParticleColor(int palette, int index, float seed)
        {
            Color a, b;
            switch (palette)
            {
                case 1: a = new Color(0.12f,0.95f,0.35f); b = new Color(0.65f,1f,0.24f); break;
                case 2: a = new Color(1f,0.67f,0.06f); b = new Color(1f,0.94f,0.48f); break;
                case 3: a = new Color(0.22f,0.87f,1f); b = new Color(0.9f,1f,1f); break;
                default:
                    a = index%3 == 0 ? new Color(0.2f,0.93f,0.25f) : new Color(1f,0.68f,0.08f);
                    b = index%3 == 0 ? new Color(0.65f,1f,0.27f) : new Color(1f,0.94f,0.38f);
                    break;
            }
            return Color.Lerp(a,b,seed);
        }

        private static int PopEffectLayer(bool local)
        {
            Transform viewer = GetHead();
            Camera camera = viewer != null ? viewer.GetComponent<Camera>() : null;
            if (camera == null) camera = Camera.main;
            if (camera == null) return 0;
            int preferred = local ? (int)UnityLayer.FirstPersonOnly : 0;
            if ((camera.cullingMask & (1 << preferred)) != 0) return preferred;
            for (int i=0; i<32; i++) if ((camera.cullingMask & (1 << i)) != 0) return i;
            return 0;
        }

        private static void TryVibrate()
        {
            try { if (GorillaTagger.Instance != null) GorillaTagger.Instance.StartVibration(true, 0.35f, 0.13f); }
            catch (Exception error) { Log.LogDebug("Optional totem haptic: " + error.Message); }
        }

        private void HideHeld()
        {
            betaHeldRenderers=null;
            if (held != null) UnityEngine.Object.Destroy(held);
            held = null;
        }

        private void ClearEffects()
        {
            if (activeBursts.Count==0 && (runtimeRoot == null || runtimeRoot.transform.childCount == 0)) return;
            StopAllCoroutines();
            for (int i=activeBursts.Count-1;i>=0;i--) activeBursts[i].Dispose();
            activeBursts.Clear();
            particlePool.ReturnAll();
            if (runtimeRoot==null) return;
            foreach (Transform child in runtimeRoot.transform) UnityEngine.Object.Destroy(child.gameObject);
        }

        [ModdedGamemodeJoin]
        public void OnModdedJoin(string gamemode)
        {
            ResetBetaNetwork();
            betaSourceActor.Value=0; betaVisuals.ClearCache();
            queuedCommands.Clear(); commandStatus="Ready";
            inModdedRoom = true;
            moddedRoom = PhotonNetwork.CurrentRoom;
            wasTagged = IsTagged();
            state.Reset();
            ruleHostBefore = false;
            timeCapAnnounced = false;
            if (PhotonNetwork.IsMasterClient) ApplyRules(LoadSavedRules(),false);
            else rules = rules.Rehost(Guid.NewGuid().ToString("N")).ResetTimer();
            ShowNotice("Hold X / F7 for Totem Settings",false);
            Log.LogInfo("Totem: entered modded room; active=" + Allowed + ", host protection=" + CanHostProtect(GorillaGameManager.instance as GorillaTagManager));
        }

        [ModdedGamemodeLeave]
        public void OnModdedLeave(string gamemode)
        {
            ResetBetaNetwork();
            betaSourceActor.Value=0; betaVisuals.ClearCache();
            queuedCommands.Clear(); commandStatus="Ready";
            inModdedRoom = false;
            moddedRoom = null;
            state.Reset(); HideHeld(); ClearEffects();
            notice.Clear();
            if (menu != null) menu.Close();
            if (sync != null) sync.Reset();
        }

        private void OnDisable() { ResetBetaNetwork(); queuedCommands.Clear(); commandAuthority=null; ruleHostBefore = false; state.Reset(); HideHeld(); ClearEffects(); betaVisuals.ClearCache(); notice.Clear(); if (menu != null) menu.Close(); if (sync != null) sync.Reset(); if (configDirty) { Config.Save(); configDirty = false; } }

        private void OnDestroy()
        {
            StopAutoUpdater();
            OnDisable();
            TagObserver.Uninstall();
            if (sync != null) sync.Dispose();
            if (menu != null) menu.Dispose();
            if (menuAssets != null) menuAssets.Dispose();
            particlePool.Dispose();
            if (harmony != null) harmony.UnpatchSelf();
            if (runtimeRoot != null) UnityEngine.Object.Destroy(runtimeRoot);
            if (assets != null) assets.Dispose();
            betaVisuals.Dispose();
            if (betaSync!=null) betaSync.Dispose();
            notice.Dispose();
            UnityEngine.Object.Destroy(gold); UnityEngine.Object.Destroy(green);
            if (Instance == this) Instance = null;
        }
    }
}
