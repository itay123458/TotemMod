using System;
using System.Globalization;
using BepInEx.Configuration;
using Photon.Pun;
using UnityEngine;
using UnityEngine.XR;

namespace TotemMod
{
    public sealed partial class TotemPlugin
    {
        private RoomRules rules;
        private TotemMenu menu;
        private MinecraftMenuAssets menuAssets;
        private readonly MenuHoldGesture menuGesture = new MenuHoldGesture();
        private bool triggerBefore;
        private bool ruleHostBefore;
        private bool rulesDirty;
        private double ruleRetryAt;
        private bool timeCapAnnounced;
        private bool configDirty;
        private float configSaveAt;
        private float menuRetryAt;
        private float demoReadyAt;
        private ConfigEntry<int> savedPreset, savedCapacity, savedRound, particleCount;
        private ConfigEntry<float> savedRegen, friendDistance, popSpeed, hudScale, hudDuration;
        private ConfigEntry<bool> savedAutoStart, haptics, showFriends, hudEnabled, hudCountdown;
        private ConfigEntry<Vector3> hudOffset;
        private ConfigEntry<bool> modEnabled, allowRefillRequests;
        private ConfigEntry<float> menuScale;
        private ConfigEntry<int> pointerStyle;
        private bool menuForDeveloper;
        private ConfigEntry<float> particleSize,particleSpread;
        private ConfigEntry<int> particlePalette,particleMotion;
        private readonly RoomCommandQueue queuedCommands = new RoomCommandQueue();
        private float sendCommandsAt;
        private string commandStatus = "Ready";
        private string commandAuthority;
        private float developerCheckAt;
        private bool developerCached;

        private bool IsDeveloper
        {
            get
            {
                if (Time.unscaledTime<developerCheckAt) return developerCached;
                developerCheckAt=Time.unscaledTime+0.5f;
                try
                {
                    developerCached=SteamManager.Initialized && DeveloperAccess.Allows(Steamworks.SteamUser.GetSteamID().m_SteamID,true,Steamworks.SteamUser.BLoggedOn());
                }
                catch (Exception) { developerCached=false; }
                return developerCached;
            }
        }

        private void InitializeSettings()
        {
            Config.SaveOnConfigSet = false;
            modEnabled = Config.Bind("General","Enabled",true,"Enable your totem. Settings can still open while disabled in a modded room.");
            allowRefillRequests = Config.Bind("Developer","AllowRefillRequests",true,"Private host: accept Dev-page refill requests from friends using this mod.");
            particleSize = Config.Bind("Effects","ParticleSize",1f,new ConfigDescription("Pixel spark size multiplier.",new AcceptableValueRange<float>(0.5f,3f)));
            particleSpread = Config.Bind("Effects","ParticleSpread",1f,new ConfigDescription("Pixel spark burst spread.",new AcceptableValueRange<float>(0.5f,2f)));
            particlePalette = Config.Bind("Effects","ParticlePalette",0,new ConfigDescription("0 Totem, 1 Emerald, 2 Gold, 3 Frost.",new AcceptableValueRange<int>(0,3)));
            particleMotion = Config.Bind("Effects","ParticleMotion",0,new ConfigDescription("0 Burst, 1 Swirl.",new AcceptableValueRange<int>(0,1)));
            menuScale = Config.Bind("Menu","Scale",1f,new ConfigDescription("Settings menu size.",new AcceptableValueRange<float>(0.6f,1.6f)));
            pointerStyle = Config.Bind("Menu","PointerColor",0,new ConfigDescription("0 cyan, 1 white, 2 gold, 3 green.",new AcceptableValueRange<int>(0,3)));
            savedPreset = Config.Bind("Presets", "Selected", 0, "0 Default, 1 Scrims, 2 Filming, 3 Casual, 4 Sandbox, 5 Custom. Private room host controls shared rules.");
            savedCapacity = Config.Bind("Presets", "TotemCapacity", 5, new ConfigDescription("Custom stock limit.", new AcceptableValueRange<int>(1,20)));
            savedRegen = Config.Bind("Presets", "RechargeSeconds", 40f, new ConfigDescription("Custom recharge interval.", new AcceptableValueRange<float>(1f,600f)));
            savedRound = Config.Bind("Presets", "RoundSeconds", 180, new ConfigDescription("Round timer cap.", new AcceptableValueRange<int>(30,600)));
            savedAutoStart = Config.Bind("Presets", "AutoStartOnTag", false, "Start timer on the first confirmed tag after reset.");
            particleCount = Config.Bind("Effects", "Particles", 32, new ConfigDescription("Particles per pop, 0 disables them.", new AcceptableValueRange<int>(0,64)));
            haptics = Config.Bind("Effects", "Haptics", true, "Local controller vibration on pop.");
            showFriends = Config.Bind("Effects", "FriendVisuals", true, "Render nearby friends' totems and pop effects.");
            friendDistance = Config.Bind("Effects", "FriendDistance", 20f, new ConfigDescription("Maximum distance for friend visual effects.", new AcceptableValueRange<float>(5f,50f)));
            popSpeed = Config.Bind("Effects", "AnimationSpeed", 1f, new ConfigDescription("Pop animation speed.", new AcceptableValueRange<float>(0.5f,2f)));
            hudEnabled = Config.Bind("Notifications", "Enabled", true, "Show local status notices.");
            hudCountdown = Config.Bind("Notifications", "RechargeCountdown", true, "Keep the empty-stock recharge countdown visible.");
            hudScale = Config.Bind("Notifications", "Scale", 1f, new ConfigDescription("Notification scale.", new AcceptableValueRange<float>(0.6f,1.5f)));
            hudDuration = Config.Bind("Notifications", "Duration", 5f, new ConfigDescription("Seconds to show notices.", new AcceptableValueRange<float>(2f,10f)));
            hudOffset = Config.Bind("Notifications", "Offset", new Vector3(-0.28f,-0.22f,0.85f), "Notification position relative to your eyes.");
            InitializeExpandedSettings();
            rules = LoadSavedRules();
            inventory.Configure(rules.Capacity,rules.Regeneration,Time.unscaledTime);
        }

        private RoomRules LoadSavedRules()
        {
            string session = Guid.NewGuid().ToString("N");
            int preset = Math.Max(0,Math.Min(5,savedPreset.Value));
            RoomRules initial = RoomRules.ForPreset(preset,session,0);
            if (preset == 5)
                initial = new RoomRules(session,0,5,Math.Max(1,Math.Min(20,savedCapacity.Value)),
                    SafeFloat(savedRegen.Value,40f,1f,600f),Math.Max(30,Math.Min(600,savedRound.Value)),protection.Value,savedAutoStart.Value,0,0,0);
            if (preset != 5 && initial.Protection != protection.Value)
                initial = new RoomRules(initial.Session,0,initial.Preset,initial.Capacity,initial.Regeneration,initial.RoundSeconds,protection.Value,initial.AutoStart,0,0,0);
            return initial;
        }

        private bool CanEditRules()
        {
            return Allowed && PhotonNetwork.IsMasterClient && NetworkSystem.Instance != null && NetworkSystem.Instance.IsMasterClient &&
                PhotonNetwork.CurrentRoom != null && !PhotonNetwork.CurrentRoom.IsVisible;
        }

        private static float SafeFloat(float value,float fallback,float min,float max)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value,min,max);
        }

        private void ChangedConfig()
        {
            configDirty = true; configSaveAt = Time.unscaledTime + 1f;
        }

        private void ApplyRules(RoomRules next,bool save)
        {
            if (next == null) return;
            RoomRules old = rules;
            bool refill = old != null && old.Session == next.Session && next.Refill > old.Refill;
            rules = next;
            inventory.Configure(next.Capacity,next.Regeneration,Time.unscaledTime);
            if (refill) inventory.Refill();
            foreach (FriendTotem friend in friends.Values)
            {
                friend.Supply.Configure(next.Capacity,next.Regeneration,Time.unscaledTime);
                if (refill) friend.Supply.Refill();
            }
            if (save)
            {
                savedPreset.Value = next.Preset; savedCapacity.Value = next.Capacity;
                savedRegen.Value = (float)next.Regeneration; savedRound.Value = next.RoundSeconds;
                savedAutoStart.Value = next.AutoStart; protection.Value = next.Protection;
                ChangedConfig();
            }
            if (refill) ShowNotice("Stock refilled  |  " + StockText(),false);
        }

        private void EditRules(RoomRules next)
        {
            if (!CanEditRules()) return;
            ApplyRules(next,true); rulesDirty = true;
        }

        private void ReceiveRules(RoomRules next)
        {
            if (next == null || !Allowed || PhotonNetwork.IsMasterClient || PhotonNetwork.CurrentRoom.IsVisible) return;
            if (rules != null && rules.Session == next.Session && next.Revision <= rules.Revision) return;
            ApplyRules(next,false);
        }

        private string StockText() { return inventory.Count + "/" + inventory.Capacity; }
        private static string ClockText(int seconds) { return (seconds/60).ToString("00") + ":" + (seconds%60).ToString("00"); }
        private string MenuStatus()
        {
            return RoomRules.PresetName(rules.Preset) + "  |  " + StockText() + " totems  |  " + rules.Regeneration.ToString("0.#",CultureInfo.InvariantCulture) +
                "s recharge  |  " + ClockText(rules.Remaining(PhotonNetwork.Time)) + (CanEditRules() ? "  [HOST]" : "  [FOLLOW HOST]");
        }

        private void TickSettings()
        {
            bool host = CanEditRules();
            if (host && !ruleHostBefore)
            {
                ApplyRules(rules.Rehost(Guid.NewGuid().ToString("N")),false);
                rulesDirty = true;
            }
            ruleHostBefore = host;
            bool finished = rules.Phase == 2 || (rules.Phase == 1 && rules.IsTimeCapped(PhotonNetwork.Time));
            if (finished && !timeCapAnnounced)
            {
                ShowNotice("TIME CAP  |  " + ClockText(rules.RoundSeconds) + "\nTotem protection paused",false);
                timeCapAnnounced = true;
            }
            else if (!finished) timeCapAnnounced = false;
            if (host && rules.Phase == 1 && finished) EditRules(rules.Stop(PhotonNetwork.Time));
            if (host && rulesDirty && Time.unscaledTime >= ruleRetryAt)
            {
                if (sync.PublishRules(rules)) rulesDirty = false;
                ruleRetryAt = Time.unscaledTime + 0.5;
            }
        }

        private void TickMenuControls()
        {
            if (configDirty && Time.unscaledTime >= configSaveAt)
            {
                Config.Save(); configDirty = false;
            }
            if (!RoomAllowed)
            {
                if (menu != null) menu.Close();
                menuGesture.Update(false,MenuButtonDown(),Time.unscaledTime);
                return;
            }
            if (Time.unscaledTime >= menuRetryAt)
            {
                try { TickMenuInput(); }
                catch (Exception error)
                {
                    if (menu != null) menu.Close();
                    menuRetryAt = Time.unscaledTime + 10;
                    Log.LogWarning("Totem menu will retry: " + error.Message);
                }
            }
        }

        private void ReceiveRefillRequest(int actor,string nonce)
        {
            if (!CanEditRules()) return;
            FriendTotem friend;
            bool accepted = allowRefillRequests.Value && friends.TryGetValue(actor,out friend);
            if (!sync.ReplyRefill(actor,nonce,accepted)) return;
            if (accepted && friends.TryGetValue(actor,out friend)) friend.Supply.Refill();
        }

        private void ReceiveRefillResult(bool accepted)
        {
            if (!Allowed) return;
            if (accepted) inventory.Refill();
            ShowNotice(accepted ? "Dev refill accepted  |  " + StockText() : "Refill unavailable / declined by host",false);
        }

        private void RefillMine()
        {
            if (!IsDeveloper) return;
            if (CanEditRules()) { inventory.Refill(); ShowNotice("Stock refilled  |  " + StockText(),false); }
            else if (sync.RequestRefill(Time.unscaledTime)) ShowNotice("Asking host for a refill...",false);
        }

        private void DemoPop()
        {
            if (IsDeveloper) PreviewPop();
        }

        private void ToggleFromMenu()
        {
            if (!IsDeveloper) return;
            if (state.Toggle(Allowed,inventory.Count > 0)) ShowNotice("Out of totems",true);
            if (!state.Equipped) HideHeld();
        }

        private bool CanControlRoom()
        {
            return CanEditRules() || (IsDeveloper && Allowed && sync != null && sync.CanRequestCommands);
        }

        private byte[] SignDeveloperCommand(byte[] data)
        {
            return DeveloperIdentity.Sign(data,System.IO.Path.Combine(BepInEx.Paths.ConfigPath,"TotemMod.developer-key.xml"),IsDeveloper);
        }

        private bool VerifyDeveloperCommand(int actor,byte[] data,byte[] signature)
        {
            return DeveloperIdentity.Verify(data,signature);
        }

        private void ControlRoom(DeveloperCommand command)
        {
            if (CanEditRules()) { ApplyRoomCommand(command); return; }
            if (!IsDeveloper || !CanControlRoom()) return;
            if (queuedCommands.Enqueue(command)) commandStatus="Queued";
        }

        private void ApplyRoomCommand(DeveloperCommand command)
        {
            if (!CanEditRules()) return;
            switch (command.Kind)
            {
                case DeveloperCommandKind.Preset: EditRules(rules.SelectPreset(command.Value)); break;
                case DeveloperCommandKind.Capacity: EditRules(rules.Change(capacity:command.Value)); break;
                case DeveloperCommandKind.Regen: EditRules(rules.Change(regeneration:command.Number)); break;
                case DeveloperCommandKind.RoundSeconds: EditRules(rules.Change(roundSeconds:command.Value)); break;
                case DeveloperCommandKind.Protection: EditRules(rules.Change(protection:command.Value != 0)); break;
                case DeveloperCommandKind.AutoStart: EditRules(rules.Change(autoStart:command.Value != 0)); break;
                case DeveloperCommandKind.StartTimer: EditRules(rules.StartConfigured(PhotonNetwork.Time)); break;
                case DeveloperCommandKind.StartScrim: EditRules(rules.SelectPreset(1).Start(PhotonNetwork.Time)); break;
                case DeveloperCommandKind.StopTimer: EditRules(rules.Stop(PhotonNetwork.Time)); break;
                case DeveloperCommandKind.ResetTimer: EditRules(rules.ResetTimer()); break;
                case DeveloperCommandKind.RefillAll: EditRules(rules.RefillStock()); break;
            }
        }

        private void ReceiveDeveloperCommand(int actor,string nonce,DeveloperCommand command)
        {
            if (!CanEditRules()) return;
            ApplyRoomCommand(command);
            if (sync.PublishRules(rules)) rulesDirty=false;
            sync.ReplyCommand(actor,nonce,true);
        }

        private void DeveloperCommandResult(bool accepted)
        {
            commandStatus=accepted ? "Applied by host" : "Unavailable / declined";
            queuedCommands.Complete(accepted);
            if (IsDeveloper && Allowed) ShowNotice(accepted ? "Dev room change applied" : "Dev request unavailable / declined",false);
        }

        private void TickDeveloperCommands()
        {
            string authority=sync.CommandAuthority;
            if (authority != commandAuthority) { queuedCommands.Clear(); commandAuthority=authority; }
            if (!IsDeveloper || !CanControlRoom()) { queuedCommands.Clear(); return; }
            if (queuedCommands.Count == 0 || Time.unscaledTime < sendCommandsAt) return;
            if (CanEditRules())
            {
                ApplyRoomCommand(queuedCommands.Take()); return;
            }
            if (sync.RequestCommand(queuedCommands.Next,Time.unscaledTime))
            {
                queuedCommands.Begin(); sendCommandsAt=Time.unscaledTime+0.3f; commandStatus="Waiting for host";
            }
            else if (!sync.CommandPending)
            {
                queuedCommands.Clear(); commandStatus="Developer key unavailable";
                ShowNotice("Dev command unavailable; check developer key / host",false);
            }
        }

        private void RestoreParticles()
        {
            if (!IsDeveloper) return;
            fx.Value=true; particleCount.Value=48; particleSize.Value=1.25f; particleSpread.Value=1;
            particlePalette.Value=0; particleMotion.Value=0; ChangedConfig(); DemoPop();
        }

        internal void OnConfirmedTag(NetPlayer player)
        {
            if (!Allowed) return;
            if (CanEditRules() && rules.AutoStart && rules.Phase == 0) EditRules(rules.Start(PhotonNetwork.Time));
            if (player != null && player.IsLocal) PopFromTag();
        }

        private bool RulesPermitProtection()
        {
            if (!rules.Protection || rules.IsTimeCapped(PhotonNetwork.Time)) return false;
            return true;
        }

        private static bool MenuButtonDown()
        {
            ControllerInputPoller poller = ControllerInputPoller.instance;
            if (poller != null && poller.leftControllerPrimaryButton) return true;
            bool down;
            InputDevice device = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            return device.isValid && device.TryGetFeatureValue(CommonUsages.primaryButton,out down) && down;
        }

        private static bool SelectDown()
        {
            ControllerInputPoller poller = ControllerInputPoller.instance;
            if (poller != null && poller.rightControllerIndexFloat > 0.7f) return true;
            bool down;
            InputDevice device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            return device.isValid && device.TryGetFeatureValue(CommonUsages.triggerButton,out down) && down;
        }

        private static bool Pressed(KeyCode key)
        {
            try { return Input.GetKeyDown(key); } catch (InvalidOperationException) { return false; }
        }
        private static bool MenuKeyHeld(KeyCode key)
        { try { return Input.GetKey(key); } catch (InvalidOperationException) { return false; } }

        private void TickMenuInput()
        {
            bool developer = IsDeveloper;
            bool beta=developer || HasBetaAccess;
            if (menu != null && (menuForDeveloper != developer || betaMenuBefore!=beta)) { menu.Dispose(); menu = null; }
            bool trigger = SelectDown();
            bool toggleMenu = menuGesture.Update(RoomAllowed,MenuButtonDown(),Time.unscaledTime,menuHoldSeconds.Value) || (RoomAllowed && Pressed(KeyCode.F7));
            if (toggleMenu)
            {
                Transform head = GetHead();
                Camera camera = head != null ? head.GetComponent<Camera>() : null;
                if (camera != null)
                {
                    if (menu == null)
                    {
                        if (menuAssets == null)
                        {
                            MinecraftMenuAssets next = new MinecraftMenuAssets();
                            try { next.Load(); menuAssets = next; }
                            catch { next.Dispose(); throw; }
                        }
                        menu = new TotemMenu(() => notice.LoadFont("Pixellari") ?? notice.LoadFont("JetBrainsMono-ExtraBold"),MenuStatus,MenuPages(),menuAssets.Skin);
                        menuForDeveloper = developer;
                        betaMenuBefore=beta;
                    }
                    ApplyMenuSettings();
                    menu.Toggle(camera);
                    triggerBefore = trigger;
                }
            }
            if (menu != null && menu.IsOpen)
            {
                if (Pressed(KeyCode.Escape)) menu.Close();
                if (Pressed(KeyCode.DownArrow)) menu.MoveFocus(1);
                if (Pressed(KeyCode.UpArrow)) menu.MoveFocus(-1);
                if (Pressed(KeyCode.LeftArrow)) menu.ActivateFocused(-1);
                if (Pressed(KeyCode.RightArrow)) menu.ActivateFocused(1);
                if (Pressed(KeyCode.Return)) menu.ActivateFocused(1);
                if (Pressed(KeyCode.Tab)) { if (MenuKeyHeld(KeyCode.LeftShift) || MenuKeyHeld(KeyCode.RightShift)) menu.NextCategory(1); else menu.NextPage(1); }
                if (Pressed(KeyCode.PageDown)) menu.NextCategory(1);
                if (Pressed(KeyCode.PageUp)) menu.NextCategory(-1);
                Transform controller = GorillaLocomotion.GTPlayer.Instance != null ? GorillaLocomotion.GTPlayer.Instance.GetControllerTransform(false) : null;
                Ray ray = controller != null ? new Ray(controller.position,controller.forward) : new Ray(Vector3.zero,Vector3.forward);
                ApplyMenuSettings();
                menu.Tick(ray,controller != null,trigger && !triggerBefore,Time.unscaledTime,trigger);
            }
            triggerBefore = trigger;
        }
    }
}
