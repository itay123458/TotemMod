using System;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class FeatureRemovalTests
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

            }

        }
    }
    private static int Main(string[] args)
    {
        try
        {
            CheckAssembly(args[0],true);
            Console.WriteLine("PASS "+checks+" mace removal, retained totem/Beta and removed weapon checks"); return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}
