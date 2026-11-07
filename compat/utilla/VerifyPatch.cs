using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class VerifyPatch
{
    private static int checks;
    private static void Require(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
    private static string Operand(Instruction instruction)
    {
        object value = instruction.Operand;
        if (value is Instruction) return "branch:" + ((Instruction)value).Offset;
        if (value is Instruction[]) return string.Join(",", ((Instruction[])value).Select(i => i.Offset.ToString()).ToArray());
        return value == null ? "" : value.ToString();
    }
    public static void Main(string[] args)
    {
        using (AssemblyDefinition original = AssemblyDefinition.ReadAssembly(args[0]))
        using (AssemblyDefinition patched = AssemblyDefinition.ReadAssembly(args[1]))
        {
            Require(original.Name.FullName == patched.Name.FullName, "Assembly identity changed");
            Require(original.MainModule.Mvid == patched.MainModule.Mvid, "Original MVID changed");
            Require(original.MainModule.Architecture == patched.MainModule.Architecture, "Architecture changed");
            Require(original.CustomAttributes.Count == patched.CustomAttributes.Count, "Assembly attributes changed");
            foreach (CustomAttribute attribute in original.CustomAttributes)
                Require(patched.CustomAttributes.Any(a => a.AttributeType.FullName == attribute.AttributeType.FullName &&
                    string.Join("|", a.ConstructorArguments.Select(x => Convert.ToString(x.Value))) ==
                    string.Join("|", attribute.ConstructorArguments.Select(x => Convert.ToString(x.Value)))), "Assembly attribute changed " + attribute.AttributeType);
            HashSet<string> edited = new HashSet<string>();
            foreach (TypeDefinition type in original.MainModule.Types)
            {
                TypeDefinition copy = patched.MainModule.Types.Single(t => t.FullName == type.FullName);
                Require(type.Fields.Count == copy.Fields.Count && type.Methods.Count == copy.Methods.Count, "Original type layout changed " + type.FullName);
                Require(type.CustomAttributes.Count == copy.CustomAttributes.Count, "Type attributes changed " + type.FullName);
                foreach (MethodDefinition method in type.Methods)
                {
                    MethodDefinition changed = copy.Methods.Single(m => m.FullName == method.FullName);
                    Require(method.CustomAttributes.Count == changed.CustomAttributes.Count, "Method attributes changed " + method.FullName);
                    if (!method.HasBody) continue;
                    string before = string.Join("\n", method.Body.Instructions.Select(i => i.OpCode + " " + Operand(i)));
                    string after = string.Join("\n", changed.Body.Instructions.Select(i => i.OpCode + " " + Operand(i)));
                    if (before != after) edited.Add(method.DeclaringType.FullName + "::" + method.Name);
                }
            }
            string[] allowed = { "Utilla.Utils.GameModeUtils::FindGamemodeInString", "Utilla.Behaviours.UtillaNetworkController::OnJoinedRoom", "Utilla.Behaviours.UtillaNetworkController::OnRoomPropertiesUpdate" };
            Require(edited.SetEquals(allowed), "Unexpected modified method set: " + string.Join(", ", edited));
            Require(patched.MainModule.Types.Count == original.MainModule.Types.Count + 4, "Unexpected added type count");
            Require(!patched.MainModule.AssemblyReferences.Any(r => r.Name.Contains("Payload")), "Temporary payload dependency");
            Require(patched.MainModule.AssemblyReferences.All(r => original.MainModule.AssemblyReferences.Any(o => o.FullName == r.FullName)), "Unexpected new assembly dependency: " + string.Join(", ", patched.MainModule.AssemblyReferences.Where(r => !original.MainModule.AssemblyReferences.Any(o => o.FullName == r.FullName)).Select(r => r.FullName)));
            foreach (TypeDefinition type in patched.MainModule.Types)
            foreach (MethodDefinition method in type.Methods.Where(m => m.HasBody))
            {
                HashSet<Instruction> instructions = new HashSet<Instruction>(method.Body.Instructions);
                foreach (Instruction instruction in method.Body.Instructions)
                {
                    if (instruction.Operand is Instruction)
                    {
                        Instruction destination = (Instruction)instruction.Operand;
                        Require(instructions.Contains(destination), "Foreign branch destination");
                        if (instruction.OpCode.OperandType == OperandType.ShortInlineBrTarget)
                        {
                            int delta = destination.Offset - (instruction.Offset + instruction.GetSize());
                            Require(delta >= -128 && delta <= 127, "Short branch overflow " + method.FullName);
                        }
                    }
                    MethodReference call = instruction.Operand as MethodReference;
                    if (call != null && call.DeclaringType.Namespace == "Utilla.Compat")
                        Require(call.DeclaringType.Scope == patched.MainModule, "Compatibility call escaped module " + call.FullName);
                }
            }
            foreach (string name in new[] { "PrivateRoomCreationPatch", "PrivateRoomModeUpdatePatch" })
            {
                TypeDefinition patch = patched.MainModule.GetType("Utilla.Compat." + name);
                Require(patch.CustomAttributes.Any(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch"), "Missing patch attribute");
                Require(patch.Methods.Single(m => m.Name == "Prefix").CustomAttributes.Any(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPrefix"), "Missing prefix attribute");
            }
            Console.WriteLine("PASS: " + checks + " assembly identity, unchanged-method, dependency, Harmony and IL checks");
        }
    }
}
