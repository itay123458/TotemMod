using BepInEx.Configuration;
using GorillaLocomotion;
using Photon.Pun;
using UnityEngine;
using UnityEngine.XR;

namespace TotemMod
{
    public sealed partial class TotemPlugin
    {
        private readonly SurvivalState survival=new SurvivalState();
        private readonly MinecraftInventory minecraftInventory=new MinecraftInventory();
        private readonly SurvivalFall survivalFall=new SurvivalFall();
        private ConfigEntry<bool> survivalEnabled,survivalHudVisible,survivalFallDamage,survivalTagDamage;
        private ConfigEntry<float> survivalHudScale;
        private SurvivalAssets survivalAssets;
        private MinecraftHud minecraftHud;
        private MinecraftHeldFood minecraftFood;
        private AudioSource survivalAudio;
        private GameObject survivalAudioRoot;
        private Photon.Realtime.Room survivalRoom;
        private Vector3 survivalPosition,survivalSpawn;
        private Quaternion survivalSpawnRotation;
        private bool survivalHasPosition,survivalHasSpawn,survivalAWasDown,survivalBWasDown,survivalTriggerWasDown;
        private float survivalRetryAt,survivalNextError,eatingStarted=-1,eatingNextSound;
        private int eatingSlot=-1;
        private CraftItem eatingItem;
        private bool SurvivalAllowed { get { return Allowed && PhotonNetwork.CurrentRoom!=null && !PhotonNetwork.CurrentRoom.IsVisible; } }
        private bool CanEquipSurvivalTotem()
        { return !SurvivalAllowed || survivalEnabled==null || !survivalEnabled.Value || !survival.Dead; }
        private float EatingProgress { get { return eatingStarted<0 ? 0 : Mathf.Clamp01((Time.unscaledTime-eatingStarted)/1.6f); } }

        private void InitializeSurvival()
        {
            survivalEnabled=Config.Bind("Minecraft","Survival",true,"Functional health, hunger, food, falling damage and local respawn in private modded rooms.");
            survivalHudVisible=Config.Bind("Minecraft","ShowHud",true,"Show the Minecraft hearts, hunger bar, offhand and nine-slot hotbar.");
            survivalFallDamage=Config.Bind("Minecraft","FallDamage",true,"Falls beyond three player-scaled meters deal health damage on landing.");
            survivalTagDamage=Config.Bind("Minecraft","TagDamage",true,"An accepted Gorilla Tag tag deals two hearts of Minecraft damage. Native infection rules remain active.");
            survivalHudScale=SliderSetting("Minecraft","HudScale",1,0.6f,1.5f,"Size of the Minecraft HUD and inventory.");
            survival.Reset(Time.unscaledTime);
        }

        private bool EnsureSurvivalVisuals()
        {
            if(survivalAssets!=null && minecraftHud!=null) return true;
            if(Time.unscaledTime<survivalRetryAt) return false;
            survivalRetryAt=Time.unscaledTime+5;
            SurvivalAssets next=new SurvivalAssets();
            try
            {
                next.Load();
                Font font=notice.LoadFont("Pixellari") ?? notice.LoadFont("JetBrainsMono-ExtraBold");
                if(font==null) throw new System.InvalidOperationException("The Minecraft UI font is not ready.");
                minecraftHud=new MinecraftHud(next,font); minecraftFood=new MinecraftHeldFood(next);
                survivalAssets=next;
                survivalAudioRoot=new GameObject("PrimalCraft survival sounds");
                UnityEngine.Object.DontDestroyOnLoad(survivalAudioRoot);
                survivalAudio=survivalAudioRoot.AddComponent<AudioSource>();
                survivalAudio.playOnAwake=false; survivalAudio.spatialBlend=0; survivalAudio.ignoreListenerPause=true;
                Log.LogInfo("Minecraft inventory ready: 36 slots, 9-slot hotbar, 10 hearts and 10 hunger icons.");
                return true;
            }
            catch(System.Exception error)
            {
                if(minecraftHud!=null) minecraftHud.Dispose(); minecraftHud=null;
                if(minecraftFood!=null) minecraftFood.Dispose(); minecraftFood=null;
                next.Dispose(); survivalAssets=null;
                Log.LogWarning("Minecraft UI not ready; retrying: "+error.Message); return false;
            }
        }

        private static bool MinecraftButton(bool primary)
        {
            ControllerInputPoller poller=ControllerInputPoller.instance;
            if(poller!=null && (primary ? poller.rightControllerPrimaryButton : poller.rightControllerSecondaryButton)) return true;
            bool value;
            InputDevice device=InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            return device.isValid && device.TryGetFeatureValue(primary ? CommonUsages.primaryButton : CommonUsages.secondaryButton,out value) && value;
        }

        private static bool MinecraftMouseHeld()
        { try { return Input.GetMouseButton(1); } catch(System.InvalidOperationException) { return false; } }

        private void TickSurvival()
        {
            if(survivalEnabled==null) return;
            bool a=MinecraftButton(true),b=MinecraftButton(false);
            bool pressA=a && !survivalAWasDown,pressB=b && !survivalBWasDown;
            survivalAWasDown=a; survivalBWasDown=b;
            if(!SurvivalAllowed || !survivalEnabled.Value)
            {
                survival.Advance(Time.unscaledTime,0,false);
                survivalHasPosition=false; survivalFall.Reset(); HideSurvival(); return;
            }
            GTPlayer player=GTPlayer.Instance;
            if(player==null) return;
            float now=Time.unscaledTime;
            if(survivalRoom!=PhotonNetwork.CurrentRoom)
            {
                HideSurvival(); survivalRoom=PhotonNetwork.CurrentRoom;
                survival.Reset(now); minecraftInventory.Reset(); survivalFall.Reset(); survivalHasPosition=false;
                survivalSpawn=player.transform.position; survivalSpawnRotation=player.transform.rotation; survivalHasSpawn=true;
                Log.LogInfo("Minecraft survival started for the current modded room.");
            }
            Vector3 position=player.transform.position;
            float scale=SafeFloat(player.scale,1,0.2f,4);
            float moved=survivalHasPosition ? Vector3.Distance(position,survivalPosition)/scale : 0;
            if(moved>5 || float.IsNaN(moved) || float.IsInfinity(moved)) moved=0;
            survival.Advance(now,moved);
            survivalPosition=position; survivalHasPosition=true;
            bool groundedFloor=player.BodyOnGround;
            bool supported=groundedFloor || player.IsHandTouching(true) || player.IsHandTouching(false);
            if(!groundedFloor && player.bodyCollider!=null)
            {
                RaycastHit hit; Bounds bounds=player.bodyCollider.bounds;
                groundedFloor=Physics.Raycast(bounds.center,Vector3.down,out hit,bounds.extents.y+0.04f*scale,player.locomotionEnabledLayers,QueryTriggerInteraction.Ignore) && hit.normal.y>0.5f;
            }
            supported|=groundedFloor;
            int fallDamage=survivalFall.Sample(position.y,now,supported,scale,groundedFloor);
            if(survivalFallDamage.Value && fallDamage>0) ApplySurvivalDamage(fallDamage,"Fall");
            if(survival.RespawnReady(now)) RespawnSurvival();
            if(pressB || Pressed(KeyCode.E)) OpenMinecraftInventory();
            bool settings=menu!=null && menu.IsOpen;
            if(!survival.Dead && !settings)
            {
                if(minecraftHud!=null && minecraftHud.IsOpen)
                {
                    if(Pressed(KeyCode.Escape)) minecraftHud.Close(minecraftInventory);
                    if(Pressed(KeyCode.LeftArrow)) minecraftHud.MoveFocus(-1);
                    if(Pressed(KeyCode.RightArrow)) minecraftHud.MoveFocus(1);
                    if(Pressed(KeyCode.UpArrow)) minecraftHud.MoveFocus(-9);
                    if(Pressed(KeyCode.DownArrow)) minecraftHud.MoveFocus(9);
                    if(Pressed(KeyCode.Return)) minecraftHud.Activate(minecraftInventory);
                }
                else
                {
                    if(pressA) minecraftInventory.Cycle(1);
                    for(int i=0;i<9;i++) if(Pressed((KeyCode)((int)KeyCode.Alpha1+i))) minecraftInventory.Select(i);
                }
            }
            TickSurvivalEating(settings || minecraftHud!=null && minecraftHud.IsOpen);
        }

        private void TickSurvivalEating(bool blocked)
        {
            bool held=SelectDown() || MinecraftMouseHeld();
            CraftStack selected=minecraftInventory.Get(minecraftInventory.Selected);
            float now=Time.unscaledTime;
            if(blocked || survival.Dead || !held || selected.Count<=0 || !survival.CanEat(selected.Item))
            { CancelSurvivalEating(); return; }
            if(eatingStarted<0 || eatingSlot!=minecraftInventory.Selected || eatingItem!=selected.Item)
            {
                eatingStarted=now; eatingSlot=minecraftInventory.Selected; eatingItem=selected.Item; eatingNextSound=now;
            }
            if(now>=eatingNextSound)
            { PlaySurvivalSound(survivalAssets!=null ? survivalAssets.EatSound : null,0.35f); eatingNextSound=now+0.4f; }
            if(now-eatingStarted<1.6f) return;
            if(minecraftInventory.ConsumeSelected(eatingItem) && survival.Eat(eatingItem,now))
            {
                PlaySurvivalSound(survivalAssets!=null ? survivalAssets.BurpSound : null,0.45f);
                Log.LogInfo("Minecraft food eaten: "+eatingItem+"; health="+survival.Health+"/20, hunger="+survival.Hunger+"/20.");
            }
            CancelSurvivalEating();
        }

        private void CancelSurvivalEating() { eatingStarted=-1; eatingSlot=-1; eatingItem=CraftItem.None; }

        private void ApplySurvivalDamage(int amount,string source)
        {
            if(!SurvivalAllowed || survivalEnabled==null || !survivalEnabled.Value || !survival.Damage(amount,Time.unscaledTime)) return;
            PlaySurvivalSound(survivalAssets!=null ? survivalAssets.HurtSound : null,0.6f);
            Log.LogInfo("Minecraft damage: "+source+" "+amount+"; health="+survival.Health+"/20.");
            if(!survival.Dead) return;
            if(state.Equipped && inventory.Count>0) PopFromTag();
            if(!survival.Dead) return;
            CancelSurvivalEating();
            if(minecraftHud!=null) minecraftHud.Close(minecraftInventory);
            if(minecraftFood!=null) minecraftFood.Hide();
            ShowNotice("You died\nRespawning in 3 seconds",false);
        }

        private void SurvivalTagHit()
        { if(survivalTagDamage!=null && survivalTagDamage.Value) ApplySurvivalDamage(4,"Tag"); }

        private void RespawnSurvival()
        {
            GTPlayer player=GTPlayer.Instance;
            if(!SurvivalAllowed || player==null || !survivalHasSpawn) return;
            player.TeleportTo(survivalSpawn,survivalSpawnRotation,false,false);
            survival.Reset(Time.unscaledTime); survivalFall.Reset(); survivalPosition=player.transform.position; survivalHasPosition=true;
            CancelSurvivalEating(); ShowNotice("Respawned",false);
            Log.LogInfo("Minecraft health respawned; native Gorilla Tag infection remains unchanged.");
        }

        private void OpenMinecraftInventory()
        {
            if(!SurvivalAllowed || survivalEnabled==null || !survivalEnabled.Value || survival.Dead || !EnsureSurvivalVisuals()) return;
            Transform head=GetHead(); Camera camera=head!=null ? head.GetComponent<Camera>() : null;
            if(camera==null) return;
            if(menu!=null) menu.Close();
            CancelSurvivalEating(); minecraftHud.Scale=survivalHudScale.Value; minecraftHud.Toggle(camera,minecraftInventory);
        }

        private void CloseMinecraftInventory()
        { if(minecraftHud!=null && minecraftHud.IsOpen) minecraftHud.Close(minecraftInventory); CancelSurvivalEating(); }

        private void RenderSurvival()
        {
            if(!SurvivalAllowed || survivalEnabled==null || !survivalEnabled.Value) { HideSurvival(); return; }
            if(!EnsureSurvivalVisuals()) return;
            Transform head=GetHead(); Camera camera=head!=null ? head.GetComponent<Camera>() : null;
            if(camera==null) return;
            try
            {
                GTPlayer player=GTPlayer.Instance;
                Transform controller=player!=null ? player.GetControllerTransform(false) : null;
                Ray ray=controller!=null ? new Ray(controller.position,controller.forward) : new Ray(camera.transform.position,camera.transform.forward);
                bool pointerValid=controller!=null;
                bool trigger=SelectDown();
                if(!InputDevices.GetDeviceAtXRNode(XRNode.RightHand).isValid)
                {
                    try { ray=camera.ScreenPointToRay(Input.mousePosition); trigger=Input.GetMouseButton(0); pointerValid=true; }
                    catch(System.InvalidOperationException) { }
                }
                bool settings=menu!=null && menu.IsOpen;
                minecraftHud.Scale=survivalHudScale.Value;
                minecraftHud.Tick(camera,minecraftInventory,survival,inventory.Count,state.Equipped,
                    survivalHudVisible.Value || minecraftHud.IsOpen,ray,pointerValid && !settings,trigger && !settings,trigger && !survivalTriggerWasDown && !settings,Time.unscaledTime,EatingProgress);
                survivalTriggerWasDown=trigger;
                VRRig rig=GorillaTagger.Instance!=null ? GorillaTagger.Instance.offlineVRRig : null;
                CraftStack selected=minecraftInventory.Get(minecraftInventory.Selected);
                bool show=!survival.Dead && !settings && !minecraftHud.IsOpen && selected.Count>0;
                minecraftFood.Pose(rig,player!=null ? player.GetHandFollower(false) : null,camera,
                    show ? selected.Item : CraftItem.None,player!=null ? player.scale : 1,EatingProgress,Time.unscaledTime);
            }
            catch(System.Exception error)
            {
                if(Time.unscaledTime>=survivalNextError) { survivalNextError=Time.unscaledTime+10; Log.LogError("Minecraft render: "+error); }
            }
        }

        private void PlaySurvivalSound(AudioClip clip,float gain)
        { if(survivalAudio!=null && clip!=null) survivalAudio.PlayOneShot(clip,Mathf.Clamp01(gain*volume.Value)); }

        private void HideSurvival()
        {
            CancelSurvivalEating(); survivalTriggerWasDown=false;
            if(minecraftFood!=null) minecraftFood.Hide();
            if(minecraftHud!=null)
            {
                minecraftHud.Close(minecraftInventory);
                minecraftHud.Tick(null,minecraftInventory,survival,inventory.Count,state.Equipped,false,new Ray(),false,false,false,Time.unscaledTime,0);
            }
            if(survivalAudio!=null) survivalAudio.Stop();
        }

        private void DisposeSurvival()
        {
            HideSurvival();
            if(minecraftFood!=null) minecraftFood.Dispose(); minecraftFood=null;
            if(minecraftHud!=null) minecraftHud.Dispose(); minecraftHud=null;
            if(survivalAssets!=null) survivalAssets.Dispose(); survivalAssets=null;
            UnityEngine.Object.Destroy(survivalAudioRoot); survivalAudioRoot=null; survivalAudio=null;
        }

        private MenuCategory MinecraftCategory()
        {
            return new MenuCategory("Minecraft",new[] { new MenuPage("Inventory and survival",new[] {
                Button("Open inventory (B / E)",OpenMinecraftInventory,()=>SurvivalAllowed && survivalEnabled.Value && !survival.Dead),
                Check("Functional health and hunger",survivalEnabled),Check("Show Minecraft HUD",survivalHudVisible),
                Slider("HUD size",survivalHudScale,0.6f,1.5f,0.05f,"x"),
                Check("Fall damage",survivalFallDamage),Check("Tag damage",survivalTagDamage),
                new MenuEntry(()=>"Health: "+survival.Health+"/20 | Hunger: "+survival.Hunger+"/20",null),
                new MenuEntry(()=>"A: next slot | Hold trigger: eat | Y: offhand",null)
            }) });
        }
    }
}
