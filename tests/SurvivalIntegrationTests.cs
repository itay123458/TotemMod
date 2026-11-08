using System;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class SurvivalIntegrationTests
{
    private static int checks;
    private static void Check(bool value,string message) { checks++; if(!value) throw new Exception(message); }
    private static MethodDefinition Method(TypeDefinition type,string name)
    { foreach(MethodDefinition method in type.Methods) if(method.Name==name) return method; throw new Exception("Missing "+name); }
    private static bool Calls(MethodDefinition method,string type,string name)
    {
        foreach(Instruction instruction in method.Body.Instructions)
        { MethodReference call=instruction.Operand as MethodReference; if(call!=null && call.DeclaringType.FullName==type && call.Name==name) return true; }
        return false;
    }
    private static int Main(string[] args)
    {
        try
        {
            using(AssemblyDefinition assembly=AssemblyDefinition.ReadAssembly(args[0]))
            {
                ModuleDefinition module=assembly.MainModule;
                TypeDefinition plugin=module.GetType("TotemMod.TotemPlugin");
                Check(Calls(Method(plugin,"Update"),plugin.FullName,"TickSurvival"),"Minecraft survival must advance from the live plugin update");
                Check(Calls(Method(plugin,"LateUpdate"),plugin.FullName,"RenderSurvival"),"Minecraft HUD must follow the live VR camera");
                Check(Calls(Method(plugin,"OnDisable"),plugin.FullName,"HideSurvival"),"Disabling the mod must hide inventory, HUD and food");
                Check(Calls(Method(plugin,"OnDestroy"),plugin.FullName,"DisposeSurvival"),"Survival UI and assets must be disposed");
                Check(Calls(Method(plugin,"PopFromTag"),"TotemMod.SurvivalState","TotemSave"),"A spent totem must restore survival health");
                Check(Calls(Method(plugin,"ApplySurvivalDamage"),"TotemMod.SurvivalState","Damage"),"Damage must use the tested health model");
                Check(Calls(Method(plugin,"TickSurvivalEating"),"TotemMod.MinecraftInventory","ConsumeSelected"),"Eating must spend an actual selected inventory item");
                Check(Calls(Method(plugin,"TickSurvivalEating"),"TotemMod.SurvivalState","Eat"),"Eating must apply real hunger/health effects");
                Check(Calls(Method(plugin,"RespawnSurvival"),"GorillaLocomotion.GTPlayer","TeleportTo"),"Death must respawn through the game's local teleport API");
                Check(!Calls(Method(plugin,"Update"),plugin.FullName,"SurvivalTagHit"),"Round-start infection changes must not cause damage or repeat confirmed-tag damage");
                Check(Calls(Method(plugin,"OnConfirmedTag"),plugin.FullName,"SurvivalTagHit"),"Only confirmed native tags feed tag damage");
                Check(Calls(Method(plugin,"Update"),plugin.FullName,"CanEquipSurvivalTotem"),"Dead players cannot equip a late totem to undo a completed death");
                foreach(string name in new[] {"TickSurvival","RenderSurvival","ApplySurvivalDamage","RespawnSurvival","OpenMinecraftInventory"})
                    Check(Calls(Method(plugin,name),plugin.FullName,"get_SurvivalAllowed"),"Survival gameplay must require a private modded room: "+name);
                TypeDefinition hud=module.GetType("TotemMod.MinecraftHud");
                Check(Calls(Method(hud,"CanvasRoot"),"UnityEngine.Object","DontDestroyOnLoad"),"Both Minecraft canvases must survive map scene unloading");
                Check(Calls(Method(hud,"EnsureCreated"),"UnityEngine.Object","DontDestroyOnLoad"),"Inventory sound ownership must survive map scene unloading");
                foreach(string name in new[] {"MinecraftInventory","SurvivalState","SurvivalFall","MinecraftHud","SurvivalAssets","MinecraftHeldFood"})
                    Check(module.GetType("TotemMod."+name)!=null,"Missing Minecraft feature "+name);
                foreach(string name in new[] {"hotbar.png","hotbar_selection.png","heart_full.png","food_full.png","apple.png","bread.png","golden_apple.png"})
                {
                    bool embedded=false;
                    foreach(Resource resource in module.Resources) embedded|=resource.Name=="PrimalCraft.Survival."+name;
                    Check(embedded,"Missing original Minecraft UI/item resource "+name);
                }
                Check(module.GetType("MaceMod.Shared.MaceRuntime")==null,"Removed mace must stay removed");
            }
            Console.WriteLine("PASS "+checks+" survival lifecycle, real item use, health, respawn and Minecraft resource checks"); return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}
