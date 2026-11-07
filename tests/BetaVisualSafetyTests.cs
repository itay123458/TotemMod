using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Verifies the compiled Beta renderer cannot accidentally acquire rig scripts,
// network objects, colliders or audio when avatar cosmetics change.
internal static class BetaVisualSafetyTests
{
    private static int checks;

    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static IEnumerable<TypeDefinition> Walk(TypeDefinition type)
    {
        yield return type;
        foreach (TypeDefinition child in type.NestedTypes)
            foreach (TypeDefinition descendant in Walk(child)) yield return descendant;
    }

    private static int Main(string[] args)
    {
        try
        {
            using (AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(args[0]))
            {
                TypeDefinition beta = assembly.MainModule.GetType("TotemMod.BetaTotemVisuals");
                Check(beta != null, "Missing Beta renderer.");
                bool bakesMesh = false, hasOwner = false;
                foreach (TypeDefinition top in assembly.MainModule.Types)
                {
                    if (top.FullName != beta.FullName && top.FullName != "TotemMod.BetaTotemVisualOwner") continue;
                    foreach (TypeDefinition type in Walk(top))
                    foreach (MethodDefinition method in type.Methods)
                    {
                        if (!method.HasBody) continue;
                        foreach (Instruction instruction in method.Body.Instructions)
                        {
                            MethodReference called = instruction.Operand as MethodReference;
                            if (called == null) continue;
                            string declaring = called.DeclaringType.FullName;
                            Check(!declaring.StartsWith("Photon", StringComparison.Ordinal), "Beta visual calls network API: " + called);
                            Check(called.Name != "CreatePrimitive", "CreatePrimitive silently adds colliders.");
                            if (declaring == "UnityEngine.SkinnedMeshRenderer" && called.Name == "BakeMesh") bakesMesh = true;
                            if (called.Name == "Instantiate")
                            {
                                GenericInstanceMethod instance = called as GenericInstanceMethod;
                                Check(instance != null && instance.GenericArguments.Count == 1 && instance.GenericArguments[0].FullName == "UnityEngine.Mesh",
                                    "Beta visual may only instantiate mesh data: " + called);
                            }
                            if (called.Name == "AddComponent")
                            {
                                GenericInstanceMethod component = called as GenericInstanceMethod;
                                Check(component != null && component.GenericArguments.Count == 1, "Non-generic component creation.");
                                string name = component.GenericArguments[0].FullName;
                                Check(name == "UnityEngine.MeshFilter" || name == "UnityEngine.MeshRenderer" || name == "TotemMod.BetaTotemVisualOwner",
                                    "Unexpected avatar component: " + name);
                                if (name == "TotemMod.BetaTotemVisualOwner") hasOwner = true;
                            }
                        }
                    }
                }
                Check(bakesMesh, "Avatar must capture the actual player mesh.");
                Check(hasOwner, "Visual must release its retained snapshot when destroyed.");
                Console.WriteLine("Beta visual safety: " + checks + " checks passed.");
                return 0;
            }
        }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}
