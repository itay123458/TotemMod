using System;
using BepInEx.Configuration;
using UnityEngine;

namespace TotemMod
{
    public sealed partial class TotemPlugin
    {
        private static readonly Color[] PointerColors={new Color(0.3f,0.92f,1),Color.white,new Color(1,0.8f,0.15f),Color.green};
        private static readonly string[] PointerNames={"Cyan","White","Gold","Green"};
        private static readonly string[] QualityNames={"Off","Minimal","Balanced","High"};
        private static readonly string[] HudNames={"Lower left","Lower right","Bottom center"};
        private static readonly string[] PaletteNames={"Totem","Emerald","Gold","Frost"};
        private static string OnOff(bool value) { return value ? "ON" : "OFF"; }
        private void SetFloat(ConfigEntry<float> entry,float value,float min,float max) { entry.Value=Mathf.Clamp(value,min,max); ChangedConfig(); }
        private void Flip(ConfigEntry<bool> entry) { entry.Value=!entry.Value; ChangedConfig(); }
        private MenuEntry Button(string title,Action action,Func<bool> enabled=null)
        { return new MenuEntry(() => title,d => action(),false,enabled); }
        private MenuEntry Check(string title,ConfigEntry<bool> entry)
        { return new MenuEntry(() => title+": "+OnOff(entry.Value),d => Flip(entry)); }
        private MenuEntry Slider(string title,ConfigEntry<float> entry,float min,float max,float step,string unit="")
        { return new MenuEntry(() => title+": "+entry.Value.ToString("0.##")+unit,() => entry.Value,v => SetFloat(entry,v,min,max),min,max,step); }
        private MenuEntry FloatSlider(string title,ConfigEntry<float> entry,float min,float max,float step)
        { return new MenuEntry(() => title+": "+Mathf.RoundToInt(entry.Value*100)+"%",() => entry.Value,v => SetFloat(entry,v,min,max),min,max,step); }
        private MenuEntry Select(string title,ConfigEntry<int> entry,string[] names)
        { return new MenuEntry(() => title+": "+names[entry.Value],d => { entry.Value=(entry.Value+d+names.Length)%names.Length; ChangedConfig(); },true); }
        private MenuEntry RoomButton(string title,DeveloperCommandKind kind,int value=0,float number=0)
        { return Button(title,() => ControlRoom(new DeveloperCommand(kind,value,number)),CanControlRoom); }
        private MenuEntry RoomDevButton(string title,DeveloperCommandKind kind,int value=0,float number=0)
        { return Button(title,() => { if (IsDeveloper) ControlRoom(new DeveloperCommand(kind,value,number)); },() => IsDeveloper && CanControlRoom()); }
        private MenuEntry DevButton(string title,Action action,Func<bool> enabled=null)
        { return Button(title,() => { if (IsDeveloper) action(); },() => IsDeveloper && (enabled==null || enabled())); }
        private float ControlNumber(DeveloperCommandKind kind,float confirmed) { return queuedCommands.Desired(kind,confirmed); }
        private MenuEntry RuleSlider(string title,DeveloperCommandKind kind,Func<float> confirmed,float min,float max,float step,string unit="")
        { return new MenuEntry(() => title+": "+ControlNumber(kind,confirmed()).ToString("0")+unit,() => ControlNumber(kind,confirmed()),v => ControlRoom(kind==DeveloperCommandKind.Regen ? new DeveloperCommand(kind,number:v) : new DeveloperCommand(kind,Mathf.RoundToInt(v))),min,max,step,CanControlRoom); }
        private MenuEntry PresetButton(int id)
        { return RoomButton(RoomRules.PresetName(id)+" preset",DeveloperCommandKind.Preset,id); }
        private MenuEntry PoseSlider(string title,int axis,bool rotation)
        {
            return new MenuEntry(() => title+": "+(rotation ? handRotation.Value[axis].ToString("0")+" deg" : (handPosition.Value[axis]*100).ToString("0.0")+" cm"),
                () => rotation ? handRotation.Value[axis] : handPosition.Value[axis],v => {
                    Vector3 next=rotation ? handRotation.Value : handPosition.Value; next[axis]=v;
                    if (rotation) handRotation.Value=next; else handPosition.Value=next; ChangedConfig();
                },rotation ? -180f : -0.1f,rotation ? 180f : 0.1f,rotation ? 5f : 0.005f);
        }
        private void RotateGrip(int axis,float amount)
        { Vector3 value=handRotation.Value; value[axis]=Mathf.Repeat(value[axis]+amount+180,360)-180; handRotation.Value=value; ChangedConfig(); }
        private void ResetPose()
        { handPosition.Value=new Vector3(0,0.005f,0); handRotation.Value=new Vector3(-90,-90,0); heldScale.Value=0.75f; ChangedConfig(); }
        private int EffectQuality { get { return !fx.Value ? 0 : particleCount.Value==0 ? 1 : particleCount.Value<=24 ? 2 : 3; } }
        private void CycleQuality(int d)
        { int n=(EffectQuality+d+4)%4; fx.Value=n>0; particleCount.Value=n<=1 ? 0 : n==2 ? 24 : 48; ChangedConfig(); }
        private int HudAnchor { get { return hudOffset.Value.x>0.1f ? 1 : hudOffset.Value.x< -0.1f ? 0 : 2; } }
        private void CycleHud(int d)
        { int n=(HudAnchor+d+3)%3; hudOffset.Value=new Vector3(n==0 ? -0.28f : n==1 ? 0.28f : 0,-0.22f,0.85f); ChangedConfig(); }
        private void RecenterMenu()
        { Transform head=GetHead(); Camera camera=head!=null ? head.GetComponent<Camera>() : null; if (menu!=null) menu.Recenter(camera); }
        private void ResetLocalLook()
        { ResetPose(); ResetMenuSettings(); ResetHudSettings(); SetEffectPreset(0); ResetBeta(); size.Value=1; showFriends.Value=true; friendDistance.Value=20; friendRenderScale.Value=1; ChangedConfig(); }
        private MenuEntry HudAxis(string title,int axis)
        {
            float min=axis==2 ? 0.4f : -0.6f,max=axis==2 ? 1.5f : 0.6f;
            return new MenuEntry(() => title+": "+hudOffset.Value[axis].ToString("0.00")+"m",() => hudOffset.Value[axis],v => { Vector3 next=hudOffset.Value; next[axis]=v; hudOffset.Value=next; ChangedConfig(); },min,max,0.01f);
        }
        private MenuEntry BetaFloat(string title,ConfigEntry<float> entry,float min,float max,float step,string unit="")
        { return new MenuEntry(() => title+": "+entry.Value.ToString("0.##")+unit,() => entry.Value,v => SetBetaFloat(entry,v,min,max),min,max,step); }
        private MenuEntry BetaVector(string title,int axis,bool rotation)
        {
            return new MenuEntry(() => title+": "+(rotation ? betaTilt.Value[axis].ToString("0")+" deg" : Mathf.RoundToInt(betaTint.Value[axis]*100)+"%"),
                () => rotation ? betaTilt.Value[axis] : betaTint.Value[axis],v => {
                    Vector3 next=rotation ? betaTilt.Value : betaTint.Value; next[axis]=v;
                    if (rotation) betaTilt.Value=next;
                    else { betaTint.Value=next; betaUsePlayerColor.Value=false; betaRainbow.Value=false; }
                    BetaChanged();
                },rotation ? -180f : 0,rotation ? 180f : 1,rotation ? 5f : 0.05f);
        }
        private MenuEntry BetaCheck(string title,ConfigEntry<bool> entry)
        { return new MenuEntry(() => title+": "+OnOff(entry.Value),d => { entry.Value=!entry.Value; if (entry==betaRainbow && entry.Value) betaUsePlayerColor.Value=false; BetaChanged(); }); }

        private MenuCategory[] MenuPages()
        {
            MenuCategory[] categories={
                new MenuCategory("Game",new[] {
                    new MenuPage("Presets",new[] {
                        PresetButton(0),PresetButton(1),PresetButton(2),PresetButton(3),PresetButton(4),
                        RoomButton("Start 3-minute Scrims",DeveloperCommandKind.StartScrim),
                        new MenuEntry(() => "Current: "+RoomRules.PresetName(rules.Preset)+" | "+StockText()+" stock",null),
                        new MenuEntry(() => CanControlRoom() ? "Room controls available" : "Following room host",null)
                    }),
                    new MenuPage("Rules",new[] {
                        RuleSlider("Totem limit",DeveloperCommandKind.Capacity,() => rules.Capacity,1,20,1),
                        RuleSlider("Recharge",DeveloperCommandKind.Regen,() => (float)rules.Regeneration,1,120,1," seconds"),
                        RuleSlider("Time cap",DeveloperCommandKind.RoundSeconds,() => rules.RoundSeconds,30,600,30," seconds"),
                        new MenuEntry(() => "Protection: "+OnOff(rules.Protection),d => ControlRoom(new DeveloperCommand(DeveloperCommandKind.Protection,rules.Protection ? 0 : 1)),false,CanControlRoom),
                        new MenuEntry(() => "Auto-start on first tag: "+OnOff(rules.AutoStart),d => ControlRoom(new DeveloperCommand(DeveloperCommandKind.AutoStart,rules.AutoStart ? 0 : 1)),false,CanControlRoom),
                        RoomButton("Reset default stock limit (5)",DeveloperCommandKind.Capacity,5),
                        RoomButton("Reset default recharge (40s)",DeveloperCommandKind.Regen,number:40),
                        RoomButton("Refill everyone's stock",DeveloperCommandKind.RefillAll)
                    }),
                    new MenuPage("Round",new[] {
                        new MenuEntry(() => rules.Phase==1 ? "End timer" : rules.RoundSeconds==180 ? "Start Scrims (3:00)" : "Start timer",d => ControlRoom(new DeveloperCommand(rules.Phase==1 ? DeveloperCommandKind.StopTimer : DeveloperCommandKind.StartTimer)),false,CanControlRoom),
                        RoomButton("Reset timer",DeveloperCommandKind.ResetTimer),
                        RoomButton("Start 3-minute Scrims",DeveloperCommandKind.StartScrim),
                        RoomButton("Set 1:30 time cap",DeveloperCommandKind.RoundSeconds,90),
                        RoomButton("Set 3:00 time cap",DeveloperCommandKind.RoundSeconds,180),
                        RoomButton("Set 5:00 time cap",DeveloperCommandKind.RoundSeconds,300),
                        RoomButton("Refill all players",DeveloperCommandKind.RefillAll),
                        new MenuEntry(() => "Remaining: "+ClockText(rules.Remaining(Photon.Pun.PhotonNetwork.Time)),null)
                    })
                }),
                new MenuCategory("Totem",new[] {
                    new MenuPage("Size and grip",new[] {
                        FloatSlider("Totem size",size,0.25f,3,0.05f),FloatSlider("Held size",heldScale,0.25f,1.5f,0.05f),
                        Button("Default palm grip",() => SetGrip(0)),Button("Flat grip",() => SetGrip(1)),
                        Button("Sideways grip",() => SetGrip(2)),Button("Small palm grip",() => SetGrip(3)),
                        Button("Save custom grip",SaveGrip),Button("Load custom grip",LoadGrip)
                    }),
                    new MenuPage("Palm position",new[] {
                        PoseSlider("Left / right",0,false),PoseSlider("Up / down",1,false),PoseSlider("Forward / back",2,false),
                        Button("Reset position",() => { handPosition.Value=new Vector3(0,0.005f,0); ChangedConfig(); }),
                        Button("Save custom grip",SaveGrip),Button("Load custom grip",LoadGrip),
                        Button("Reset full hand placement",ResetPose)
                    }),
                    new MenuPage("Palm rotation",new[] {
                        PoseSlider("Pitch",0,true),PoseSlider("Yaw",1,true),PoseSlider("Roll",2,true),
                        Button("Pitch +90 degrees",() => RotateGrip(0,90)),Button("Yaw +90 degrees",() => RotateGrip(1,90)),
                        Button("Roll +90 degrees",() => RotateGrip(2,90)),
                        Button("Reset rotation",() => { handRotation.Value=new Vector3(-90,-90,0); ChangedConfig(); }),
                        Button("Reset full hand placement",ResetPose)
                    })
                }),
                new MenuCategory("Effects",new[] {
                    new MenuPage("Particles",new[] {
                        Check("Pop effects",fx),
                        new MenuEntry(() => "Particle amount: "+particleCount.Value,() => particleCount.Value,v => { particleCount.Value=Mathf.RoundToInt(v); ChangedConfig(); },0,64,4),
                        FloatSlider("Particle size",particleSize,0.5f,3,0.1f),FloatSlider("Spread",particleSpread,0.5f,2,0.1f),
                        Select("Colors",particlePalette,PaletteNames),Select("Motion",particleMotion,new[]{"Burst","Swirl"}),
                        Select("Shape",particleShape,new[]{"Mixed","Sparkles","Flecks"}),FloatSlider("Brightness",particleBrightness,0.5f,1.5f,0.05f)
                    }),
                    new MenuPage("Sound and motion",new[] {
                        Check("Pop sound",popSound),FloatSlider("Volume",volume,0,1,0.05f),Slider("Pitch",popPitch,0.5f,1.5f,0.05f,"x"),
                        Slider("Animation speed",popSpeed,0.5f,2,0.1f,"x"),Select("Animation",popMotion,new[]{"Minecraft sway","Spin","Still","Minimal"}),
                        Slider("Pop distance",popDistance,0.4f,1.2f,0.05f,"m"),Slider("Pop height",popHeight,-0.2f,0.2f,0.01f,"m"),
                        Button("Preview pop (no stock spent)",PreviewPop,() => Allowed)
                    }),
                    new MenuPage("Friends and quality",new[] {
                        Check("Friend visuals",showFriends),Check("Friend sounds",friendPopSound),
                        Slider("Friend distance",friendDistance,5,50,1,"m"),FloatSlider("Friend render scale",friendRenderScale,0.5f,1.5f,0.05f),
                        Check("Controller vibration",haptics),FloatSlider("Particle spin",particleSpin,0,2,0.1f),
                        new MenuEntry(() => "Quality: "+QualityNames[EffectQuality],CycleQuality,true),
                        Button("Restore effect defaults",() => SetEffectPreset(0))
                    }),
                    new MenuPage("Effect presets",new[] {
                        Button("Minecraft classic",() => SetEffectPreset(0)),Button("Quiet",() => SetEffectPreset(1)),
                        Button("Cinematic swirl",() => SetEffectPreset(2)),Button("Frost sparkles",() => SetEffectPreset(3)),
                        Button("Golden burst",() => SetEffectPreset(4)),Button("Minimal pop",() => SetEffectPreset(5)),
                        Button("Preview current effects",PreviewPop,() => Allowed),Button("Clear current effects",ClearEffects)
                    })
                }),
                new MenuCategory("Interface",new[] {
                    new MenuPage("Menu and pointer",new[] {
                        FloatSlider("Menu size",menuScale,0.6f,1.6f,0.05f),Slider("Menu distance",menuDistance,0.65f,1.8f,0.05f,"m"),
                        FloatSlider("Click volume",menuClickVolume,0,1,0.05f),Select("Pointer color",pointerStyle,PointerNames),
                        FloatSlider("Pointer width",pointerWidth,0.5f,2,0.1f),FloatSlider("Aim smoothing",pointerSmoothing,0,1,0.05f),
                        Slider("Hold X duration",menuHoldSeconds,0.3f,1.5f,0.1f,"s"),Button("Recenter menu",RecenterMenu)
                    }),
                    new MenuPage("HUD",new[] {
                        Check("Notifications",hudEnabled),FloatSlider("Notice size",hudScale,0.6f,1.5f,0.05f),
                        Slider("Notice duration",hudDuration,2,10,1,"s"),
                        new MenuEntry(() => "Anchor: "+HudNames[HudAnchor],CycleHud,true),
                        Check("Round timer",hudShowTimer),Check("Persistent stock",hudPersistentStock),Check("Recharge countdown",hudCountdown),
                        Button("Test notice",() => ShowNotice("HUD preview | "+StockText()+" totems",false),() => Allowed)
                    }),
                    new MenuPage("HUD position",new[] {
                        HudAxis("Left / right",0),HudAxis("Up / down",1),HudAxis("Eye distance",2),
                        Button("Lower left",() => { hudOffset.Value=new Vector3(-0.28f,-0.22f,0.85f); ChangedConfig(); }),
                        Button("Lower right",() => { hudOffset.Value=new Vector3(0.28f,-0.22f,0.85f); ChangedConfig(); }),
                        Button("Bottom center",() => { hudOffset.Value=new Vector3(0,-0.22f,0.85f); ChangedConfig(); }),
                        Button("Reset HUD",ResetHudSettings)
                    }),
                    new MenuPage("Notices and controls",new[] {
                        Check("Equip notices",notifyEquip),Check("Regeneration notices",notifyRegen),Check("Pop notices",notifyPop),
                        Check("Keyboard Y",keyboard),Check("My totem mod",modEnabled),
                        Button("Reset menu",ResetMenuSettings),Button("Reset notices",ResetHudSettings),Button("Reset local appearance",ResetLocalLook)
                    }),
                    new MenuPage("Updates",new[] {
                        Check("Automatic updates",automaticUpdates),
                        Slider("Check interval",updateIntervalMinutes,15,720,15," minutes"),
                        Button("Check GitHub now",() => CheckForUpdates(),() => !UpdateBusy()),
                        new MenuEntry(() => "Installed: "+ModVersion,null),
                        new MenuEntry(() => updateStatus,null),
                        new MenuEntry(PendingUpdatesText,null),
                        Button("Cancel pending update",CancelPendingUpdate,() => !UpdateBusy()),
                        Button("Open GitHub releases",() => Application.OpenURL("https://github.com/itay123458/TotemMod/releases"))
                    })
                }),
                new MenuCategory("Beta",new[] {
                    new MenuPage("Totem model",new[] {
                        BetaCheck("Experimental appearance",betaEnabled),
                        new MenuEntry(() => "Model: "+BetaStyleNames[betaStyle.Value],d => { betaStyle.Value=(betaStyle.Value+d+5)%5; betaEnabled.Value=true; BetaChanged(); },true),
                        new MenuEntry(() => "Player: "+BetaSourceName(),CycleBetaSource,true),
                        BetaCheck("Original player colors",betaUsePlayerColor),BetaCheck("Use model during pop",betaInPop),
                        BetaFloat("Model scale",betaScale,0.5f,2,0.05f,"x"),
                        Button("Refresh player snapshot",() => BetaChanged(true)),
                        new MenuEntry(() => HasBetaAccess ? "Synced to players using the updated mod" : "Beta access is locked",null)
                    }),
                    new MenuPage("Tint and motion",new[] {
                        BetaVector("Red",0,false),BetaVector("Green",1,false),BetaVector("Blue",2,false),
                        BetaCheck("Rainbow tint",betaRainbow),BetaFloat("Rainbow speed",betaRainbowSpeed,0.05f,0.5f,0.05f," cycles/s"),
                        BetaFloat("Held spin",betaSpin,-120,120,5," deg/s"),BetaFloat("Held bob",betaBob,0,0.025f,0.0025f,"m"),
                        Button("Reset tint",() => { betaTint.Value=Vector3.one; betaRainbow.Value=false; betaUsePlayerColor.Value=true; BetaChanged(); })
                    }),
                    new MenuPage("Model orientation",new[] {
                        BetaVector("Extra pitch",0,true),BetaVector("Extra yaw",1,true),BetaVector("Extra roll",2,true),
                        Button("Reset model rotation",() => { betaTilt.Value=Vector3.zero; BetaChanged(); }),
                        Button("Stop spin and bob",() => { betaSpin.Value=0; betaBob.Value=0; BetaChanged(); }),
                        Button("Reset model scale",() => SetBetaFloat(betaScale,1,0.5f,2)),
                        Button("Reset Beta to classic",ResetBeta)
                    }),
                    new MenuPage("Experiments",new[] {
                        Button("Mini me",() => BetaPreset(0)),Button("Emerald crystal",() => BetaPreset(1)),
                        Button("Rainbow star",() => BetaPreset(2)),Button("Golden block",() => BetaPreset(3)),
                        Button("Preview model pop",PreviewPop,() => Allowed),Button("Refresh avatar pose",() => BetaChanged(true)),
                        Button("Return to classic totem",ResetBeta)
                    })
                }),
                new MenuCategory("Dev",new[] {
                    new MenuPage("Beta access",new[] {
                        new MenuEntry(() => "Player: "+BetaGrantName(),CycleBetaGrantPlayer,true,() => IsDeveloper),
                        DevButton("Grant Beta in this room",() => ChangeBetaGrant(true),() => betaGrantActor>0),
                        DevButton("Revoke Beta in this room",() => ChangeBetaGrant(false),() => betaGrantActor>0),
                        new MenuEntry(() => betaGrantActor>0 && betaSync.AccessFor(betaGrantActor) ? "Selected player: Beta allowed" : "Selected player: Beta locked",null),
                        new MenuEntry(() => "Your approved account list is built in",null),
                        new MenuEntry(() => "Temporary room grants end when you leave",null),
                        DevButton("Show Beta access status",() => ShowNotice(HasBetaAccess ? "Beta access active" : "Beta access locked",false)),
                        DevButton("Refresh local Beta model",() => BetaChanged(true))
                    }),
                    new MenuPage("Local tools",new[] {
                        DevButton("Equip / unequip my totem",ToggleFromMenu,() => Allowed),
                        DevButton("Test pop (no stock spent)",DemoPop,() => Allowed),
                        DevButton("Refill my stock",RefillMine,() => Allowed),
                        DevButton("Restore particles + test",RestoreParticles,() => Allowed),
                        DevButton("Clear active effects",ClearEffects),DevButton("Show stock / timer notice",() => ShowNotice(MenuStatus(),false),() => Allowed),
                        DevButton("Refresh Beta model",() => BetaChanged(true)),
                        new MenuEntry(() => "Room commands: "+(CanEditRules() ? "Host" : sync.CanRequestCommands ? commandStatus : "Updated host required"),null)
                    }),
                    new MenuPage("Room control",new[] {
                        RoomDevButton("Start 3-minute Scrims",DeveloperCommandKind.StartScrim),RoomDevButton("End timer",DeveloperCommandKind.StopTimer),
                        RoomDevButton("Reset timer",DeveloperCommandKind.ResetTimer),RoomDevButton("Refill everyone",DeveloperCommandKind.RefillAll),
                        RoomDevButton("Enable protection",DeveloperCommandKind.Protection,1),RoomDevButton("Disable protection",DeveloperCommandKind.Protection,0),
                        RoomDevButton("Auto-start on first tag",DeveloperCommandKind.AutoStart,1),RoomDevButton("Manual timer start",DeveloperCommandKind.AutoStart,0)
                    }),
                    new MenuPage("Practice shortcuts",new[] {
                        RoomDevButton("Default preset",DeveloperCommandKind.Preset,0),RoomDevButton("Scrims preset",DeveloperCommandKind.Preset,1),
                        RoomDevButton("Filming preset",DeveloperCommandKind.Preset,2),RoomDevButton("Sandbox preset",DeveloperCommandKind.Preset,4),
                        RoomDevButton("One-second recharge",DeveloperCommandKind.Regen,number:1),RoomDevButton("40-second recharge",DeveloperCommandKind.Regen,number:40),
                        RoomDevButton("20-totem limit",DeveloperCommandKind.Capacity,20),RoomDevButton("5-totem limit",DeveloperCommandKind.Capacity,5)
                    }),
                    new MenuPage("Reset and diagnostics",new[] {
                        DevButton("Reset menu",ResetMenuSettings),DevButton("Reset HUD",ResetHudSettings),
                        DevButton("Reset effects",() => SetEffectPreset(0)),DevButton("Reset grip",ResetPose),
                        DevButton("Reset Beta",ResetBeta),DevButton("Reset all local appearance",ResetLocalLook),
                        DevButton("Recenter menu",RecenterMenu),
                        new MenuEntry(() => "Developer account: "+(IsDeveloper ? "Confirmed" : "Locked"),null)
                    })
                })
            };
            System.Collections.Generic.List<MenuCategory> visible=new System.Collections.Generic.List<MenuCategory>(categories);
            if (!IsDeveloper) visible.RemoveAt(visible.Count-1);
            if (!IsDeveloper && !HasBetaAccess) visible.RemoveAt(4);
            return visible.ToArray();
        }
    }
}
