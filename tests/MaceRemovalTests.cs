using System;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class MaceRemovalTests
{
    private static int checks;
    private static void Check(bool value,string message) { checks++; if(!value) throw new Exception(message); }
    private static void CheckAssembly(string path,bool main)
    {
        using(AssemblyDefinition assembly=AssemblyDefinition.ReadAssembly(path))
        {
            ModuleDefinition module=assembly.MainModule;
            foreach(string name in new[] {"MaceRuntime","MaceClient","MaceVisuals","MaceTransport","MaceFall"})
                Check(module.GetType("MaceMod.Shared."+name)==null,"Removed mace gameplay must not be compiled: "+name);
            foreach(Resource resource in module.Resources)
                Check(!resource.Name.StartsWith("MaceMod.",StringComparison.Ordinal),"Removed mace assets must not be embedded");
            foreach(TypeDefinition type in module.Types)
            foreach(MethodDefinition method in type.Methods)
            {
                if(!method.HasBody) continue;
                foreach(Instruction instruction in method.Body.Instructions)
                {
                    string label=instruction.Operand as string;
                    Check(label!="Equip mace" && label!="Unequip mace" && label!="Mace and enchantments" && label!="Mace effects","Removed mace controls must not remain reachable");
                    MethodReference call=instruction.Operand as MethodReference;
                    Check(call==null || !call.DeclaringType.FullName.StartsWith("MaceMod.Shared.Mace",StringComparison.Ordinal),"No mace gameplay calls may remain");
                }
            }
            if(main)
            {
                Check(module.GetType("TotemMod.TotemPlugin")!=null,"Totem plugin must remain");
                Check(module.GetType("TotemMod.TotemInventory")!=null,"Totem inventory must remain");
                Check(module.GetType("TotemMod.BetaRoomSync")!=null,"Beta access must remain");
                TypeDefinition plugin=module.GetType("TotemMod.TotemPlugin");
                bool disablesLegacy=false,checksIdentity=false;
                foreach(MethodDefinition method in plugin.Methods)
                {
                    if(method.Name!="RetireLegacyMace") continue;
                    foreach(Instruction instruction in method.Body.Instructions)
                    {
                        checksIdentity|=(instruction.Operand as string)=="macemod.gtag.rhys";
                        MethodReference call=instruction.Operand as MethodReference;
                        if(call!=null && call.Name=="set_enabled")
                            disablesLegacy|=instruction.Previous!=null && instruction.Previous.OpCode.Code==Code.Ldc_I4_0;
                    }
                }
                Check(checksIdentity && disablesLegacy,"A main-only update must disable the older mace companion by its own plugin identity");
            }
            else
            {
                TypeDefinition plugin=module.GetType("MaceMod.MacePlugin");
                Check(plugin!=null,"Legacy updater needs a compatible retirement plugin identity");
                bool identity=false;
                foreach(CustomAttribute attribute in plugin.CustomAttributes)
                    if(attribute.AttributeType.FullName=="BepInEx.BepInPlugin") identity=(string)attribute.ConstructorArguments[0].Value=="macemod.gtag.rhys";
                Check(identity,"Retirement update preserves the old Mace GUID");
                foreach(MethodDefinition method in plugin.Methods)
                    Check(method.Name==".ctor" || method.Name=="Awake","Retired companion must not tick, render, receive hits or offer controls");
                foreach(AssemblyNameReference reference in module.AssemblyReferences)
                    Check(!reference.Name.StartsWith("Photon",StringComparison.Ordinal) && reference.Name!="UnityEngine.PhysicsModule","Retired companion must not depend on networking or physics");
            }
        }
    }
    private static int Main(string[] args)
    {
        try
        {
            CheckAssembly(args[0],true); CheckAssembly(args[1],false);
            Console.WriteLine("PASS "+checks+" mace removal, inactive companion, retained totem/Beta and updater identity checks"); return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}
