using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class VerifyAssembly
{
    private static int failures;
    private static void Check(bool passed, string label)
    {
        Console.WriteLine((passed ? "PASS " : "FAIL ") + label);
        if (!passed) failures++;
    }
    private static bool Calls(ModuleDefinition module, string declaringType, string name)
    {
        return module.Types.SelectMany(AllTypes).SelectMany(t => t.Methods).Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions).Any(i => {
                MemberReference member = i.Operand as MemberReference;
                return member != null && member.DeclaringType != null && member.DeclaringType.FullName == declaringType && member.Name == name;
            });
    }
    private static IEnumerable<TypeDefinition> AllTypes(TypeDefinition type)
    {
        yield return type;
        foreach (TypeDefinition nested in type.NestedTypes)
            foreach (TypeDefinition item in AllTypes(nested)) yield return item;
    }
    public static int Main(string[] args)
    {
        using (ModuleDefinition module = ModuleDefinition.ReadModule(args[0]))
        {
            Check(Calls(module, "ControllerInputPoller", "leftControllerSecondaryButton"), "Y uses the left secondary button");
            MethodDefinition totemInput=module.GetType("TotemMod.TotemPlugin").Methods.First(m=>m.Name=="YDown");
            Check(!totemInput.Body.Instructions.Any(i=>i.Operand is MemberReference && ((MemberReference)i.Operand).Name=="rightControllerSecondaryButton"), "B remains separate from Totem equip input");
            Check(Calls(module, "GorillaLocomotion.GTPlayer", "GetHandFollower"), "attachment uses the game's Transform API");
            Check(module.Resources.Any(r => r.Name == "TotemMod.totem.png"), "Minecraft totem texture is embedded");
            Check(module.Resources.Any(r => r.Name == "TotemMod.pop.pcm"), "Minecraft pop audio is embedded");
            Check(module.Types.All(t => t.Name != "AntiTag") && !Calls(module, "Photon.Pun.PhotonNetwork", "RaiseEvent"), "no tag cancellation or custom Photon event codes");
            TypeDefinition hook = module.Types.FirstOrDefault(t => t.Name == "TagObserver");
            Check(hook != null && hook.Methods.Any(m => m.Name == "AfterTag" && m.ReturnType.FullName == "System.Void"), "tag observer is a void postfix");
            Check(Calls(module, "GorillaTagCompetitiveManager", "add_onTagOccurred") && Calls(module, "GorillaTagCompetitiveManager", "remove_onTagOccurred"), "competitive mode uses the validated tag event and unsubscribes");
            Check(Calls(module, "TotemMod.RoomAccess", "Allows"), "all effects require an active modded room");
            Check(Calls(module, "Photon.Realtime.Player", "SetCustomProperties"), "friend state uses Photon player properties");
            TypeDefinition sync = module.Types.FirstOrDefault(t => t.Name == "PhotonSync");
            Check(sync != null && sync.Interfaces.Any(i => i.InterfaceType.FullName == "Photon.Realtime.IInRoomCallbacks"), "friend property changes are received");
            Check(module.Types.SelectMany(t => t.Methods).All(m => m.Name != "Preview"), "offline preview bypass is removed");
            Check(module.Resources.Any(r => r.Name == "TotemMod.gnotifications"), "GorillaNotifications template is embedded");
            Check(Calls(module, "TotemMod.TotemInventory", "Consume") && Calls(module, "TotemMod.TotemInventory", "Advance"), "pops spend limited charges and update recharge time");
            Check(Calls(module, "UnityEngine.Quaternion", "LookRotation") && module.Types.Any(t => t.Name == "TotemNotice"), "VR notification follows an eye-facing canvas");
            Check(module.Types.Any(t => t.Name == "TotemMenu") && Calls(module,"ControllerInputPoller","leftControllerPrimaryButton"),"Minecraft settings menu is bound to X input");
            Check(Calls(module,"TotemMod.PhotonSync","PublishRules") && Calls(module,"TotemMod.TotemInventory","Configure"),"host presets update shared rules and stock limits");
            Check(Calls(module,"TotemMod.PopParticleBatch","Tick") && Calls(module,"TotemMod.PopParticleBatch","Dispose"),"pop particles use one bounded batched renderer per burst");
            Check(Calls(module,"TotemMod.PopAnimationStyle","Evaluate") && Calls(module,"TotemMod.PopAnimationStyle","Duration") && Calls(module,"TotemMod.PopAnimationStyle","ParticleCount"),"compact icon and particle timing are wired into the pop animation");
            string[] menuAssets = {"background.png","button.png","button-highlighted.png","button-disabled.png","slider.png","slider-highlighted.png","handle.png","handle-highlighted.png","click.pcm"};
            Check(menuAssets.All(name => module.Resources.Any(r => r.Name == "TotemMod.Menu." + name)),"Minecraft menu textures and click are embedded");
            Check(Calls(module,"TotemMod.PhotonSync","RequestRefill") && Calls(module,"TotemMod.PhotonSync","ReplyRefill"),"guest Dev refills use a cooperative host handshake");
            Check(Calls(module,"TotemMod.EquipState","Toggle") && module.Types.Any(t => t.Name == "MenuSkin"),"Dev equip shares stock and room-gated equip state");
            Check(Calls(module,"TotemMod.DeveloperAccess","Allows") && Calls(module,"Steamworks.SteamUser","BLoggedOn") && Calls(module,"Steamworks.SteamUser","GetSteamID"),"Dev access uses logged-in Steam identity");
            Check(Calls(module,"TotemMod.DeveloperIdentity","Sign") && Calls(module,"TotemMod.DeveloperIdentity","Verify") && !Calls(module,"Photon.Pun.PhotonNetwork","SetMasterClient"),"developer room controls sign and verify without taking host authority");
            Check(Calls(module,"TotemMod.RoomRules","StartConfigured"),"configured timer start selects three-minute Scrims rules");
            Check(Calls(module,"TotemMod.PopParticleStyle","Evaluate") && Calls(module,"UnityEngine.Renderer","SetPropertyBlock"),"pixel spark motion and palette tint are used by pop effects");
            MethodDefinition categories=module.Types.First(t => t.Name=="TotemPlugin").Methods.First(m => m.Name=="MenuPages");
            Check(categories.Body.Instructions.Count(i => i.OpCode==OpCodes.Ldstr && object.Equals(i.Operand,"Dev"))==1 &&
                !categories.Body.Instructions.Any(i => i.OpCode==OpCodes.Ldstr && object.Equals(i.Operand,"Dev Room")) &&
                Calls(module,"TotemMod.MenuNavigation","MovePage") && Calls(module,"TotemMod.MenuNavigation","MoveCategory"),"one Dev category uses category and subpage navigation");
            Check(Calls(module,"TotemMod.BetaTotemVisuals","Create") && Calls(module,"TotemMod.BetaTotemVisuals","ClearCache"),"Beta model selection creates render-only snapshots and releases cached data");
        }
        Console.WriteLine(failures + " failures");
        return failures == 0 ? 0 : 1;
    }
}
