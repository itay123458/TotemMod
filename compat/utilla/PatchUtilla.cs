using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class PatchUtilla
{
    private const string ExpectedSha = "4c64753e6f3609a40ca5d86e6033b134f915474ec7387d3fd517a95f255de813";
    private static ModuleDefinition target;
    private static Dictionary<string, TypeDefinition> types = new Dictionary<string, TypeDefinition>();
    private static Dictionary<string, MethodDefinition> methods = new Dictionary<string, MethodDefinition>();
    private static Dictionary<string, FieldDefinition> fields = new Dictionary<string, FieldDefinition>();

    private static TypeReference NormalizeCore(TypeReference type)
    {
        TypeSpecification specification = type as TypeSpecification;
        if (specification != null) NormalizeCore(specification.ElementType);
        GenericInstanceType generic = type as GenericInstanceType;
        if (generic != null) foreach (TypeReference argument in generic.GenericArguments) NormalizeCore(argument);
        AssemblyNameReference scope = type.Scope as AssemblyNameReference;
        if (scope != null && scope.Name == "mscorlib")
            type.Scope = target.AssemblyReferences.Single(a => a.Name == "netstandard");
        return type;
    }

    private static TypeReference ImportType(TypeReference type)
    {
        TypeDefinition found;
        if (types.TryGetValue(type.FullName, out found)) return found;
        ArrayType array = type as ArrayType;
        if (array != null) return new ArrayType(ImportType(array.ElementType), array.Rank);
        ByReferenceType byref = type as ByReferenceType;
        if (byref != null) return new ByReferenceType(ImportType(byref.ElementType));
        GenericInstanceType generic = type as GenericInstanceType;
        if (generic != null)
        {
            GenericInstanceType result = new GenericInstanceType(ImportType(generic.ElementType));
            foreach (TypeReference argument in generic.GenericArguments) result.GenericArguments.Add(ImportType(argument));
            return result;
        }
        return NormalizeCore(target.ImportReference(type));
    }

    private static MethodReference ImportMethod(MethodReference method)
    {
        MethodDefinition found;
        if (methods.TryGetValue(method.FullName, out found)) return found;
        MethodReference imported = target.ImportReference(method);
        NormalizeCore(imported.DeclaringType);
        NormalizeCore(imported.ReturnType);
        foreach (ParameterDefinition parameter in imported.Parameters) NormalizeCore(parameter.ParameterType);
        return imported;
    }

    private static CustomAttribute CloneAttribute(CustomAttribute source)
    {
        CustomAttribute result = new CustomAttribute(ImportMethod(source.Constructor));
        foreach (CustomAttributeArgument argument in source.ConstructorArguments)
        {
            object value = argument.Value;
            if (value is TypeReference) value = ImportType((TypeReference)value);
            result.ConstructorArguments.Add(new CustomAttributeArgument(ImportType(argument.Type), value));
        }
        if (source.HasFields || source.HasProperties) throw new Exception("Unexpected named attribute arguments");
        return result;
    }

    private static void Merge(ModuleDefinition payload)
    {
        foreach (TypeDefinition source in payload.Types.Where(t => t.Namespace == "Utilla.Compat"))
        {
            if (source.HasNestedTypes || source.HasGenericParameters || source.HasProperties || source.HasEvents)
                throw new Exception("Unsupported payload type shape " + source.FullName);
            TypeDefinition copy = new TypeDefinition(source.Namespace, source.Name, source.Attributes, ImportType(source.BaseType));
            target.Types.Add(copy);
            types.Add(source.FullName, copy);
        }
        foreach (TypeDefinition source in payload.Types.Where(t => types.ContainsKey(t.FullName)))
        {
            TypeDefinition copy = types[source.FullName];
            foreach (FieldDefinition field in source.Fields)
            {
                FieldDefinition cloned = new FieldDefinition(field.Name, field.Attributes, ImportType(field.FieldType));
                if (field.HasConstant) cloned.Constant = field.Constant;
                copy.Fields.Add(cloned);
                fields.Add(field.FullName, cloned);
            }
            foreach (MethodDefinition method in source.Methods)
            {
                MethodDefinition cloned = new MethodDefinition(method.Name, method.Attributes, ImportType(method.ReturnType));
                cloned.ImplAttributes = method.ImplAttributes;
                foreach (ParameterDefinition parameter in method.Parameters)
                    cloned.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes, ImportType(parameter.ParameterType)));
                copy.Methods.Add(cloned);
                methods.Add(method.FullName, cloned);
            }
        }
        foreach (TypeDefinition source in payload.Types.Where(t => types.ContainsKey(t.FullName)))
        {
            TypeDefinition copy = types[source.FullName];
            foreach (CustomAttribute attribute in source.CustomAttributes) copy.CustomAttributes.Add(CloneAttribute(attribute));
            foreach (MethodDefinition method in source.Methods)
            {
                MethodDefinition cloned = methods[method.FullName];
                foreach (CustomAttribute attribute in method.CustomAttributes) cloned.CustomAttributes.Add(CloneAttribute(attribute));
                if (!method.HasBody) continue;
                if (method.Body.HasExceptionHandlers) throw new Exception("Unsupported payload exception handlers");
                cloned.Body.InitLocals = method.Body.InitLocals;
                cloned.Body.MaxStackSize = method.Body.MaxStackSize;
                foreach (VariableDefinition variable in method.Body.Variables) cloned.Body.Variables.Add(new VariableDefinition(ImportType(variable.VariableType)));
                Dictionary<Instruction, Instruction> instructions = new Dictionary<Instruction, Instruction>();
                foreach (Instruction instruction in method.Body.Instructions)
                {
                    Instruction item = Instruction.Create(OpCodes.Nop);
                    item.OpCode = instruction.OpCode;
                    item.Operand = instruction.Operand;
                    cloned.Body.Instructions.Add(item);
                    instructions.Add(instruction, item);
                }
                foreach (Instruction instruction in method.Body.Instructions)
                {
                    Instruction item = instructions[instruction];
                    object operand = item.Operand;
                    if (operand is Instruction) item.Operand = instructions[(Instruction)operand];
                    else if (operand is Instruction[]) item.Operand = ((Instruction[])operand).Select(i => instructions[i]).ToArray();
                    else if (operand is VariableDefinition) item.Operand = cloned.Body.Variables[((VariableDefinition)operand).Index];
                    else if (operand is ParameterDefinition) item.Operand = cloned.Parameters[((ParameterDefinition)operand).Index];
                    else if (operand is MethodReference) item.Operand = ImportMethod((MethodReference)operand);
                    else if (operand is FieldReference)
                    {
                        FieldDefinition found;
                        item.Operand = fields.TryGetValue(((FieldReference)operand).FullName, out found) ? found : target.ImportReference((FieldReference)operand);
                    }
                    else if (operand is TypeReference) item.Operand = ImportType((TypeReference)operand);
                }
            }
        }
    }

    private static void InjectProjection()
    {
        MethodDefinition project = types["Utilla.Compat.InfectionCompatibility"].Methods.Single(m => m.Name == "ProjectLocal");
        MethodDefinition lookup = target.GetType("Utilla.Utils.GameModeUtils").Methods.Single(m => m.Name == "FindGamemodeInString");
        ILProcessor il = lookup.Body.GetILProcessor();
        Instruction first = lookup.Body.Instructions[0];
        il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, Instruction.Create(OpCodes.Call, project));
        il.InsertBefore(first, Instruction.Create(OpCodes.Starg, lookup.Parameters[0]));
        int eventReads = 0;
        foreach (MethodDefinition method in target.GetType("Utilla.Behaviours.UtillaNetworkController").Methods)
        {
            if (!method.HasBody) continue;
            foreach (Instruction instruction in method.Body.Instructions.ToArray())
            {
                MethodReference call = instruction.Operand as MethodReference;
                if (call != null && call.DeclaringType.FullName == "NetworkSystem" && call.Name == "get_GameModeString")
                {
                    method.Body.GetILProcessor().InsertAfter(instruction, Instruction.Create(OpCodes.Call, project));
                    eventReads++;
                }
            }
        }
        if (eventReads != 2) throw new Exception("Expected exactly two event GameModeString reads, got " + eventReads);
        Console.WriteLine("Patched mode lookup and " + eventReads + " event reads; added " + types.Count + " compatibility types");
    }

    public static int Main(string[] args)
    {
        if (args.Length != 4) throw new Exception("Usage: PatchUtilla original payload output managedDirectory");
        File.WriteAllBytes(args[2], Apply(File.ReadAllBytes(args[0]), File.ReadAllBytes(args[1]), args[3]));
        return 0;
    }

    internal static byte[] Apply(byte[] originalBytes, byte[] payloadBytes, string managedDirectory)
    {
        lock (types)
        {
        try
        {
        types.Clear(); methods.Clear(); fields.Clear();
        string hash;
        using (SHA256 sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(originalBytes)).Replace("-", "").ToLowerInvariant();
        if (hash != ExpectedSha) throw new Exception("Unsupported original Utilla digest: " + hash);
        using (DefaultAssemblyResolver resolver = new DefaultAssemblyResolver())
        {
        resolver.AddSearchDirectory(managedDirectory);
        resolver.AddSearchDirectory(Path.GetFullPath(Path.Combine(managedDirectory, "..", "..", "BepInEx", "core")));
        resolver.AddSearchDirectory(AppDomain.CurrentDomain.BaseDirectory);
        ReaderParameters options = new ReaderParameters { AssemblyResolver = resolver };
        using (MemoryStream originalStream = new MemoryStream(originalBytes, false))
        using (MemoryStream payloadStream = new MemoryStream(payloadBytes, false))
        using (AssemblyDefinition original = AssemblyDefinition.ReadAssembly(originalStream, options))
        using (AssemblyDefinition payload = AssemblyDefinition.ReadAssembly(payloadStream, options))
        {
            target = original.MainModule;
            Merge(payload.MainModule);
            InjectProjection();
            foreach (AssemblyNameReference unusedCore in target.AssemblyReferences.Where(a => a.Name == "mscorlib").ToArray())
                target.AssemblyReferences.Remove(unusedCore);
            if (target.AssemblyReferences.Any(a => a.Name == payload.Name.Name)) throw new Exception("Payload dependency leaked");
            using (MemoryStream output = new MemoryStream())
            {
                original.Write(output);
                return output.ToArray();
            }
        }
        }
        }
        finally { target = null; types.Clear(); methods.Clear(); fields.Clear(); }
        }
    }
}
