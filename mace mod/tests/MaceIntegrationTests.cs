using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MaceMod.Shared;

// Pure decisions plus static safety checks against the real compiled plugins.
// These checks establish call ordering and local-body ownership, not a live game test.
internal static class MaceIntegrationTests
{
    private static int checks;
    private const string Slot = "TotemMace.CooperativeRuntime.v1";

    private static void Check(bool condition, string label)
    { checks++; if (!condition) throw new Exception(label); }

    private static void CombatRules()
    {
        // Bits: private modded, participating, attacker tagged, target tagged, saved.
        int[] expected = { 0,0,0,2, 0,0,0,3, 0,0,0,2, 0,0,0,0,
                           0,0,0,2, 0,0,0,1, 0,0,0,2, 0,0,0,0 };
        for (int bits = 0; bits < 32; bits++)
        {
            bool allowed = (bits & 1) != 0, participants = (bits & 2) != 0;
            bool attackerTagged = (bits & 4) != 0, targetTagged = (bits & 8) != 0, saved = (bits & 16) != 0;
            Check(MaceCombatRules.Resolve(allowed, participants, attackerTagged, targetTagged, saved) == expected[bits],
                "Combat truth table case " + bits);
        }
        Check(MaceCombatRules.Resolve(true, true, true, false, true) == 1, "Totem save suppresses native tag");
        Check(MaceCombatRules.Resolve(true, true, true, false, false) == 3, "Unprotected tagged strike requests native tag");
        Check(MaceCombatRules.Resolve(true, true, false, false, false) == 2, "Untagged strike flings");
        Check(MaceCombatRules.Resolve(true, true, true, true, false) == 0, "Tagged attacker cannot tag an already tagged target");
    }

    private static IEnumerable<TypeDefinition> Types(TypeDefinition type)
    {
        yield return type;
        foreach (TypeDefinition child in type.NestedTypes)
            foreach (TypeDefinition descendant in Types(child)) yield return descendant;
    }

    private static IEnumerable<TypeDefinition> Types(ModuleDefinition module)
    {
        foreach (TypeDefinition type in module.Types)
            foreach (TypeDefinition descendant in Types(type)) yield return descendant;
    }

    private static TypeDefinition RequiredType(ModuleDefinition module, string name)
    {
        TypeDefinition found = module.GetType(name);
        Check(found != null, module.Name + " missing cooperative runtime type " + name);
        return found;
    }

    private static MethodDefinition Method(TypeDefinition type, string name)
    {
        foreach (MethodDefinition method in type.Methods) if (method.Name == name) return method;
        throw new Exception(type.FullName + " missing method " + name);
    }

    private static bool IsCall(Instruction instruction, string owner, string name)
    {
        MethodReference call = instruction.Operand as MethodReference;
        return call != null && call.Name == name && (owner == null || call.DeclaringType.FullName == owner);
    }

    private static int CallIndex(MethodDefinition method, string owner, string name)
    {
        for (int i = 0; i < method.Body.Instructions.Count; i++)
            if (IsCall(method.Body.Instructions[i], owner, name)) return i;
        return -1;
    }

    private static IEnumerable<int> Successors(MethodDefinition method, int index)
    {
        Mono.Collections.Generic.Collection<Instruction> instructions = method.Body.Instructions;
        Instruction instruction = instructions[index];
        if (instruction.OpCode.FlowControl == FlowControl.Return || instruction.OpCode.FlowControl == FlowControl.Throw) yield break;
        Instruction target = instruction.Operand as Instruction;
        if (target != null) yield return instructions.IndexOf(target);
        Instruction[] targets = instruction.Operand as Instruction[];
        if (targets != null) foreach (Instruction option in targets) yield return instructions.IndexOf(option);
        if (instruction.OpCode.FlowControl != FlowControl.Branch && index + 1 < instructions.Count) yield return index + 1;
    }

    private static bool Reachable(MethodDefinition method, int start, int target, int blocked)
    {
        HashSet<int> seen = new HashSet<int>();
        Stack<int> pending = new Stack<int>(); pending.Push(start);
        while (pending.Count > 0)
        {
            int index = pending.Pop();
            if (index < 0 || index >= method.Body.Instructions.Count || index == blocked || !seen.Add(index)) continue;
            if (index == target) return true;
            foreach (int next in Successors(method, index)) pending.Push(next);
        }
        return false;
    }

    private static void BooleanGuard(MethodDefinition method, int guard, int protectedCall, string label)
    {
        Check(guard >= 0 && protectedCall > guard, label + " has guard before effect");
        Check(!Reachable(method, 0, protectedCall, guard), label + " cannot bypass guard");
        int branch = guard + 1;
        while (branch < method.Body.Instructions.Count && method.Body.Instructions[branch].OpCode.Code == Code.Nop) branch++;
        Instruction conditional = method.Body.Instructions[branch];
        bool branchOnTrue = conditional.OpCode.Code == Code.Brtrue || conditional.OpCode.Code == Code.Brtrue_S;
        bool branchOnFalse = conditional.OpCode.Code == Code.Brfalse || conditional.OpCode.Code == Code.Brfalse_S;
        Check(branchOnTrue || branchOnFalse, label + " branches directly on boolean result");
        int jump = method.Body.Instructions.IndexOf((Instruction)conditional.Operand);
        int success = branchOnTrue ? jump : branch + 1, failure = branchOnTrue ? branch + 1 : jump;
        Check(Reachable(method, success, protectedCall, -1), label + " permits effect after success");
        Check(!Reachable(method, failure, protectedCall, -1), label + " failure cannot reach effect");
    }

    private static bool Bcl(TypeReference type)
    {
        GenericInstanceType generic = type as GenericInstanceType;
        if (generic != null)
        {
            if (!Bcl(generic.ElementType)) return false;
            foreach (TypeReference argument in generic.GenericArguments) if (!Bcl(argument)) return false;
            return true;
        }
        return type.Namespace == "System" || type.Namespace.StartsWith("System.", StringComparison.Ordinal);
    }

    private static void SharedBus(ModuleDefinition module, TypeDefinition runtime)
    {
        TypeDefinition client = RequiredType(module, "MaceMod.Shared.MaceClient");
        MethodDefinition connect = Method(client, "Connect"), attach = Method(runtime, "Attach");
        Check(CallIndex(connect, "System.AppDomain", "GetData") >= 0, "Connection reads process-wide AppDomain bus");
        Check(CallIndex(connect, "System.AppDomain", "SetData") >= 0, "Connection publishes process-wide AppDomain bus");
        Check(CallIndex(Method(runtime, "Detach"), "System.AppDomain", "GetData") >= 0, "Detach checks current bus owner");
        Check(CallIndex(Method(runtime, "Detach"), "System.AppDomain", "SetData") >= 0, "Last detach clears shared slot");
        bool slot = false, attachInvoke = false, dictionaryCast = false;
        foreach (Instruction instruction in connect.Body.Instructions)
        {
            if (instruction.OpCode.Code == Code.Ldstr && (string)instruction.Operand == Slot) slot = true;
            TypeReference cast = instruction.Operand as TypeReference;
            if (instruction.OpCode.Code == Code.Castclass || instruction.OpCode.Code == Code.Isinst)
            {
                Check(cast != null && Bcl(cast), "Shared bus casts use BCL types, avoiding plugin type identity");
                if (cast.FullName.StartsWith("System.Collections.Generic.IDictionary`2", StringComparison.Ordinal)) dictionaryCast = true;
            }
            MethodReference call = instruction.Operand as MethodReference;
            if (call != null && call.Name == "Invoke" && call.DeclaringType.FullName.StartsWith("System.Func`6", StringComparison.Ordinal))
            { attachInvoke = true; Check(Bcl(call.DeclaringType), "Attach callback contains only BCL parameter and return types"); }
        }
        Check(slot && dictionaryCast && attachInvoke, "Shared runtime uses stable slot plus dictionary/delegate bridge");
        Check(Bcl(attach.ReturnType), "Attach returns BCL dictionary");
        foreach (ParameterDefinition parameter in attach.Parameters) Check(Bcl(parameter.ParameterType), "Attach argument is BCL " + parameter.Name);
        foreach (ParameterDefinition parameter in connect.Parameters) Check(Bcl(parameter.ParameterType), "Connect argument is BCL " + parameter.Name);
        bool save = false, commit = false;
        foreach (TypeDefinition nested in runtime.NestedTypes)
        foreach (FieldDefinition field in nested.Fields)
        {
            if (field.Name == "Save") save = field.FieldType.FullName == "System.Func`1<System.Boolean>";
            if (field.Name == "Commit") commit = field.FieldType.FullName == "System.Action";
        }
        Check(save && commit, "Save is a nonmutating Func<bool> probe; Commit is a separate Action");
    }

    private static void LocalVelocity(ModuleDefinition module, TypeDefinition runtime)
    {
        MethodDefinition apply = Method(runtime, "ApplyVelocity");
        int writes = 0;
        foreach (TypeDefinition type in Types(module))
        foreach (MethodDefinition method in type.Methods)
        {
            if (!method.HasBody) continue;
            foreach (Instruction instruction in method.Body.Instructions)
            {
                MethodReference call = instruction.Operand as MethodReference;
                if (call != null && call.DeclaringType.FullName == "UnityEngine.Rigidbody" && call.Name == "set_linearVelocity")
                { writes++; Check(method == apply, "All mace velocity writes occur only in ApplyVelocity"); }
            }
        }
        Check(writes == 1, "Exactly one mace linearVelocity setter exists per plugin");
        int localGetter = CallIndex(apply, "GorillaLocomotion.GTPlayer", "get_Instance");
        Check(localGetter >= 0, "ApplyVelocity obtains the local GTPlayer singleton");
        int localStore = localGetter + 1;
        Check(apply.Body.Instructions[localStore].OpCode.Code == Code.Stloc_0, "Local GTPlayer is held in the first local");
        int bodyLoads = 0;
        for (int i = 0; i < apply.Body.Instructions.Count; i++)
        {
            Instruction instruction = apply.Body.Instructions[i];
            FieldReference field = instruction.Operand as FieldReference;
            MethodReference getter = instruction.Operand as MethodReference;
            bool fieldAccess = field != null && field.Name == "playerRigidBody";
            bool propertyAccess = getter != null && getter.Name == "get_playerRigidBody";
            if (!fieldAccess && !propertyAccess) continue;
            bodyLoads++;
            Check((fieldAccess ? field.DeclaringType.FullName == "GorillaLocomotion.GTPlayer" && instruction.OpCode.Code == Code.Ldfld :
                getter.DeclaringType.FullName == "GorillaLocomotion.GTPlayer") &&
                i > 0 && apply.Body.Instructions[i - 1].OpCode.Code == Code.Ldloc_0,
                "Every body reference is taken directly from the local GTPlayer");
        }
        Check(bodyLoads >= 2, "ApplyVelocity checks and updates local playerRigidBody");
        Check(CallIndex(apply, "UnityEngine.Vector3", "ClampMagnitude") >= 0, "Local movement remains magnitude bounded");
    }

    private static void CombatOrdering(TypeDefinition runtime)
    {
        MethodDefinition hit = Method(runtime, "ReceiveHit"), receipt = Method(runtime, "ReceiveReceipt");
        int validateHit = CallIndex(hit, runtime.FullName, "Validate"), send = CallIndex(hit, "MaceMod.Shared.MaceTransport", "SendReceipt");
        Check(send >= 0, "ReceiveHit publishes authenticated receipt");
        BooleanGuard(hit, validateHit, send, "ReceiveHit validation");
        int commit = -1, save = -1;
        for (int i = 1; i < hit.Body.Instructions.Count; i++)
        {
            MethodReference call = hit.Body.Instructions[i].Operand as MethodReference;
            FieldReference field = hit.Body.Instructions[i - 1].Operand as FieldReference;
            if (call == null || call.Name != "Invoke" || field == null) continue;
            if (field.Name == "Commit" && call.DeclaringType.FullName == "System.Action") commit = i;
            if (field.Name == "Save" && call.DeclaringType.FullName == "System.Func`1<System.Boolean>") save = i;
        }
        Check(save >= 0 && save < send && commit > send, "Totem probes before send and commits after send");
        BooleanGuard(hit, send, commit, "Receipt success gates Totem commit");
        int movement = CallIndex(hit, runtime.FullName, "ApplyVelocity");
        BooleanGuard(hit, send, movement, "Receipt success gates local knockback");
        int report = CallIndex(receipt, "GorillaGameManager", "ReportTag");
        if (report < 0) report = CallIndex(receipt, "GorillaTagManager", "ReportTag");
        Check(report >= 0, "Receipt outcome uses native GorillaTagManager.ReportTag");
        BooleanGuard(receipt, CallIndex(receipt, runtime.FullName, "Validate"), report, "Host revalidates hit before native tag");
        BooleanGuard(receipt, CallIndex(receipt, "Photon.Pun.PhotonNetwork", "get_IsMasterClient"), report, "Photon host guard");
        BooleanGuard(receipt, CallIndex(receipt, "NetworkSystem", "get_IsMasterClient"), report, "NetworkSystem host guard");
    }

    private static void ForbiddenCalls(ModuleDefinition module, TypeDefinition runtime)
    {
        int reports = 0;
        foreach (TypeDefinition type in Types(module))
        foreach (MethodDefinition method in type.Methods)
        {
            if (!method.HasBody) continue;
            foreach (Instruction instruction in method.Body.Instructions)
            {
                MethodReference call = instruction.Operand as MethodReference;
                if (call == null) continue;
                Check(call.Name != "RPC" && call.Name != "RpcSecure" && call.Name != "RaiseEvent" && call.Name != "OpRaiseEvent" && call.Name != "SetMasterClient",
                    "No RPC/custom-event/master reassignment: " + method.FullName + " -> " + call.Name);
                if ((call.DeclaringType.FullName == "GorillaTagManager" || call.DeclaringType.FullName == "GorillaGameManager") && call.Name == "ReportTag")
                { reports++; Check(type == runtime && method.Name == "ReceiveReceipt", "Native mace tag only occurs in ReceiveReceipt"); }
                if (type.Namespace == "MaceMod.Shared")
                {
                    bool foreignRig = call.Name == ".ctor" && (call.DeclaringType.FullName == "VRRig" || call.DeclaringType.FullName == "GorillaLocomotion.GTPlayer");
                    bool networkSpawn = call.DeclaringType.FullName.StartsWith("Photon.", StringComparison.Ordinal) && call.Name.StartsWith("Instantiate", StringComparison.Ordinal);
                    bool unityRigSpawn = call.DeclaringType.FullName == "UnityEngine.Object" && call.Name == "Instantiate" && call.FullName.IndexOf("VRRig", StringComparison.Ordinal) >= 0;
                    Check(!foreignRig && !networkSpawn && !unityRigSpawn, "No foreign/player rig or network instantiation");
                }
            }
        }
        Check(reports == 1, "Exactly one host-native mace tag call exists");
    }

    private static void Artifact(string path)
    {
        using (AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(path))
        {
            ModuleDefinition module = assembly.MainModule;
            TypeDefinition runtime = RequiredType(module, "MaceMod.Shared.MaceRuntime");
            SharedBus(module, runtime); LocalVelocity(module, runtime);
            CombatOrdering(runtime); ForbiddenCalls(module, runtime);
            Console.WriteLine("Verified cooperative runtime: " + path);
        }
    }

    public static int Main(string[] args)
    {
        try
        {
            Check(args.Length >= 1 && args.Length <= 2, "Usage: MaceIntegrationTests plugin.dll [TotemMod.dll]");
            CombatRules();
            foreach (string path in args) Artifact(path);
            Console.WriteLine("PASS MaceIntegrationTests (" + checks + " checks)"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL MaceIntegrationTests: " + error.Message); return 1; }
    }
}
