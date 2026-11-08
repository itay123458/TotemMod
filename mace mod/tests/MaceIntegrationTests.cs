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
    private const string Slot = "TotemMace.CooperativeRuntime.v2";

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

    private static void BooleanGuard(MethodDefinition method, int guard, int protectedCall, string label, bool requiredValue = true, bool requireDominates = true)
    {
        Check(guard >= 0 && protectedCall > guard, label + " has guard before effect");
        if (requireDominates) Check(!Reachable(method, 0, protectedCall, guard), label + " cannot bypass guard");
        int branch = guard + 1;
        while (branch < method.Body.Instructions.Count && method.Body.Instructions[branch].OpCode.Code == Code.Nop) branch++;
        Instruction conditional = method.Body.Instructions[branch];
        bool branchOnTrue = conditional.OpCode.Code == Code.Brtrue || conditional.OpCode.Code == Code.Brtrue_S;
        bool branchOnFalse = conditional.OpCode.Code == Code.Brfalse || conditional.OpCode.Code == Code.Brfalse_S;
        Check(branchOnTrue || branchOnFalse, label + " branches directly on boolean result");
        int jump = method.Body.Instructions.IndexOf((Instruction)conditional.Operand);
        int success = branchOnTrue == requiredValue ? jump : branch + 1, failure = branchOnTrue == requiredValue ? branch + 1 : jump;
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
            if (call != null && call.Name == "Invoke" && call.DeclaringType.FullName.StartsWith("System.Func`", StringComparison.Ordinal) &&
                call.DeclaringType.FullName.IndexOf("System.Collections.Generic.IDictionary`2", StringComparison.Ordinal) >= 0)
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
        Check(CallIndex(apply,"GorillaLocomotion.GTPlayer","ApplyKnockback")>=0,"Knockback must release ground contacts using the local game's knockback API");
        Check(CallIndex(apply,"GorillaLocomotion.GTPlayer","SetPlayerVelocity")>=0,"Knockback must update locomotion velocity history");
        Check(CallIndex(apply,"MaceMod.Shared.MaceMechanics","ImpulseVertical")>=0,"A falling smash must rebound upward instead of merely slowing descent");
        int nativeKnockback=CallIndex(apply,"GorillaLocomotion.GTPlayer","ApplyKnockback");
        Check(nativeKnockback>0 && apply.Body.Instructions[nativeKnockback-1].OpCode.Code==Code.Ldc_I4_1,"Native knockback must force the local hands off the ground");
        Check(CallIndex(apply,"GorillaLocomotion.GTPlayer","SetPlayerVelocity")>nativeKnockback,"Commit bounded launch velocity after releasing ground contacts");
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
        Check(writes == 0, "Mace movement must use native locomotion APIs instead of raw Rigidbody velocity writes");
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
        Check(CallIndex(Method(runtime,"Tick"),runtime.FullName,"HitCenter")>=0,"Head and lower-body swings need the same target coverage as torso swings");
        Check(CallIndex(Method(runtime,"Validate"),runtime.FullName,"HitCenter")>=0,"Receiver must validate against the same body coverage as the attacker");
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
        int report = CallIndex(receipt, "GorillaGameModes.GameMode", "ReportTag");
        Check(report >= 0, "Receipt outcome uses the game's ordinary authenticated GameMode.ReportTag route");
        BooleanGuard(receipt, CallIndex(receipt, runtime.FullName, "Validate"), report, "Attacker revalidates hit before native tag request");
        List<int> tagged = new List<int>();
        for (int i = 0; i < report; i++) if (IsCall(receipt.Body.Instructions[i], runtime.FullName, "Tagged")) tagged.Add(i);
        Check(tagged.Count == 2, "Native tag checks both attacker and victim infection state");
        BooleanGuard(receipt, tagged[0], report, "Native tag requires tagged attacker");
        BooleanGuard(receipt, tagged[1], report, "Native tag requires untagged victim", false);
        LocalAttackerGuard(receipt, report);
    }

    private static void LocalAttackerGuard(MethodDefinition method, int protectedCall)
    {
        int comparison = -1;
        for (int i = 0; i < protectedCall; i++)
        {
            if (!IsCall(method.Body.Instructions[i], "MaceMod.Shared.MaceReceipt", "get_SourceActor")) continue;
            if (i + 3 >= method.Body.Instructions.Count ||
                !IsCall(method.Body.Instructions[i + 1], "Photon.Pun.PhotonNetwork", "get_LocalPlayer") ||
                !IsCall(method.Body.Instructions[i + 2], "Photon.Realtime.Player", "get_ActorNumber")) continue;
            Code opcode = method.Body.Instructions[i + 3].OpCode.Code;
            if (opcode == Code.Bne_Un || opcode == Code.Bne_Un_S || opcode == Code.Beq || opcode == Code.Beq_S)
                comparison = i + 3;
        }
        Check(comparison >= 0, "Native tag compares receipt source against local actor");
        Check(!Reachable(method, 0, protectedCall, comparison), "Native tag cannot bypass local attacker comparison");
        Instruction branch = method.Body.Instructions[comparison];
        int jump = method.Body.Instructions.IndexOf((Instruction)branch.Operand);
        bool equalsJump = branch.OpCode.Code == Code.Beq || branch.OpCode.Code == Code.Beq_S;
        int local = equalsJump ? jump : comparison + 1, remote = equalsJump ? comparison + 1 : jump;
        Check(Reachable(method, local, protectedCall, -1), "Local attacker may submit ordinary native tag request");
        Check(!Reachable(method, remote, protectedCall, -1), "Other clients cannot submit attacker's native tag request");
        Check(CallIndex(method, "Photon.Pun.PhotonNetwork", "get_IsMasterClient") < 0 &&
              CallIndex(method, "NetworkSystem", "get_IsMasterClient") < 0,
              "Ordinary native tag request works for an attacker who is not master");
    }

    private static List<int> EquippedWrites(MethodDefinition method)
    {
        List<int> writes = new List<int>();
        for (int i = 0; i < method.Body.Instructions.Count; i++)
        {
            FieldReference field = method.Body.Instructions[i].Operand as FieldReference;
            if (field != null && field.Name == "equipped" && method.Body.Instructions[i].OpCode.Code == Code.Stfld) writes.Add(i);
        }
        return writes;
    }

    private static void DeveloperAuthorization(ModuleDefinition module, TypeDefinition runtime)
    {
        MethodDefinition authorization = Method(runtime, "EquipAuthorized"), toggle = Method(runtime, "Toggle");
        Check(authorization.ReturnType.FullName == "System.Boolean" && authorization.Parameters.Count == 1,
            "Equipment authorization evaluates the owning client");
        bool ownerGate = false, ownerCallback = false, signer = false;
        foreach (Instruction instruction in authorization.Body.Instructions)
        {
            if (instruction.OpCode.Code == Code.Ldstr && (string)instruction.Operand == "totem-dev") ownerGate = true;
            FieldReference field = instruction.Operand as FieldReference;
            if (field != null && field.Name == "AuthorizeEquip" && field.FieldType.FullName == "System.Func`1<System.Boolean>") ownerCallback = true;
            if (field != null && field.Name == "Sign" && field.FieldType.FullName == "System.Func`2<System.Byte[],System.Byte[]>") signer = true;
        }
        Check(ownerGate && ownerCallback && signer && CallIndex(authorization, runtime.FullName, "Active") >= 0,
            "Equipment authorization requires active native developer owner, callback, and signer");
        Check(CallIndex(toggle, "TotemMod.DeveloperIdentity", "Verify") >= 0, "Equip toggle verifies developer signature before activation");
        Check(toggle.Parameters.Count == 1, "Toggle is bound to the requesting client");
        Check(EquippedWrites(Method(runtime, "Configure")).Count == 0, "Configuration cannot equip the mace");
        List<int> writes = EquippedWrites(toggle);
        Check(writes.Count > 0, "Authorized toggle updates equipped state");
        int guard = CallIndex(toggle, runtime.FullName, "EquipAuthorized");
        foreach (int write in writes) BooleanGuard(toggle, guard, write, "Toggle requires developer equipment authorization");
        MethodDefinition clear = Method(runtime, "ClearUnauthorized");
        List<int> clearWrites = EquippedWrites(clear);
        Check(clearWrites.Count > 0 && CallIndex(clear, runtime.FullName, "EquipAuthorized") >= 0,
            "Loss of all equipment authorizers clears global equipped state");
        foreach (int write in clearWrites)
        {
            Check(write > 0 && clear.Body.Instructions[write - 1].OpCode.Code == Code.Ldc_I4_0,
                "Revoked equipment authorization stores false");
            BooleanGuard(clear, CallIndex(clear, runtime.FullName, "EquipAuthorized"), write,
                "Any valid equipment authorizer prevents revocation", false, false);
        }
        MethodDefinition tick = Method(runtime, "Tick");
        int clearCall = CallIndex(tick, runtime.FullName, "ClearUnauthorized");
        int published = CallIndex(tick, "MaceMod.Shared.MaceState", ".ctor");
        Check(clearCall >= 0 && published > clearCall && !Reachable(tick, 0, published, clearCall),
            "Tick clears revoked authorization before publishing equipped state");

        TypeDefinition totem = module.GetType("TotemMod.TotemPlugin");
        if (totem != null)
        {
            Check(CallIndex(Method(totem, "TickMace"), "MaceMod.Shared.MaceClient", "Toggle") < 0,
                "Totem input tick cannot equip; developer menu owns the route");
            int toggles = 0;
            foreach (TypeDefinition nested in Types(totem))
            foreach (MethodDefinition method in nested.Methods)
            {
                if (!method.HasBody) continue;
                foreach (Instruction instruction in method.Body.Instructions)
                    if (IsCall(instruction, "MaceMod.Shared.MaceClient", "Toggle"))
                    {
                        toggles++;
                        Check(method.Name.IndexOf("BuildMenu", StringComparison.Ordinal) >= 0 ||
                            method.Name.IndexOf("Developer", StringComparison.Ordinal) >= 0 ||
                            method.Name.IndexOf("MenuPages", StringComparison.Ordinal) >= 0,
                            "Mace equip callback belongs to developer menu construction");
                    }
            }
            Check(toggles == 1, "Totem exposes one developer menu equip callback");
        }
        TypeDefinition standalone = module.GetType("MaceMod.MacePlugin");
        if (standalone != null)
        foreach (TypeDefinition nested in Types(standalone))
        foreach (MethodDefinition method in nested.Methods)
        {
            Check(method.Name != "Toggle" && method.Name != "BDown", "Standalone companion has no equip input methods");
            if (!method.HasBody) continue;
            Check(CallIndex(method, "MaceMod.Shared.MaceClient", "Toggle") < 0, "Standalone companion cannot equip mace");
        }
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
                    Check(false, "Plugin cannot invoke host-native tag validation directly");
                if (call.DeclaringType.FullName == "GorillaGameModes.GameMode" && call.Name == "ReportTag")
                { reports++; Check(type == runtime && method.Name == "ReceiveReceipt", "Ordinary native mace tag request only occurs in ReceiveReceipt"); }
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
            TypeDefinition totem=module.GetType("TotemMod.TotemPlugin");
            TypeDefinition standalone=module.GetType("MaceMod.MacePlugin");
            Check(CallIndex(Method(totem ?? standalone,totem!=null ? "get_RoomAllowed" : "Allowed"),"MaceMod.Shared.ModdedRoomMarker","Allows")>=0,
                "Updated clients recognize the explicit private Infection marker when Utilla callbacks are missing");
            DeveloperAuthorization(module, runtime); SharedBus(module, runtime); LocalVelocity(module, runtime);
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
