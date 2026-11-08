using System;
using System.Collections.Generic;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Compile with the pure MacePresentation.cs and Cecil. Artifact checks run first
// so an old plugin produces a meaningful missing-presentation regression failure.
internal static class MaceVisibilityTests
{
    private const string Presentation = "MaceMod.Shared.MacePresentation";
    private static int checks;
    private static void Check(bool condition,string label)
    { checks++; if(!condition) throw new Exception(label); }

    private static MethodDefinition Method(TypeDefinition type,string name)
    {
        foreach(MethodDefinition method in type.Methods) if(method.Name==name) return method;
        throw new Exception(type.FullName+" missing method "+name);
    }
    private static bool Call(Instruction instruction,string owner,string name)
    {
        MethodReference method=instruction.Operand as MethodReference;
        return method!=null && method.DeclaringType.FullName==owner && method.Name==name;
    }
    private static int CallIndex(MethodDefinition method,string owner,string name)
    {
        for(int i=0;i<method.Body.Instructions.Count;i++) if(Call(method.Body.Instructions[i],owner,name)) return i;
        return -1;
    }
    private static int Local(Instruction instruction,bool store)
    {
        switch(instruction.OpCode.Code)
        {
            case Code.Ldloc_0: return store?-1:0;
            case Code.Ldloc_1: return store?-1:1;
            case Code.Ldloc_2: return store?-1:2;
            case Code.Ldloc_3: return store?-1:3;
            case Code.Stloc_0: return store?0:-1;
            case Code.Stloc_1: return store?1:-1;
            case Code.Stloc_2: return store?2:-1;
            case Code.Stloc_3: return store?3:-1;
            case Code.Ldloc: case Code.Ldloc_S:
                return store?-1:((VariableDefinition)instruction.Operand).Index;
            case Code.Stloc: case Code.Stloc_S:
                return store?((VariableDefinition)instruction.Operand).Index:-1;
            default: return -1;
        }
    }
    private static IEnumerable<int> Successors(MethodDefinition method,int index)
    {
        Instruction instruction=method.Body.Instructions[index];
        if(instruction.OpCode.FlowControl==FlowControl.Return || instruction.OpCode.FlowControl==FlowControl.Throw) yield break;
        Instruction target=instruction.Operand as Instruction;
        if(target!=null) yield return method.Body.Instructions.IndexOf(target);
        Instruction[] targets=instruction.Operand as Instruction[];
        if(targets!=null) foreach(Instruction option in targets) yield return method.Body.Instructions.IndexOf(option);
        if(instruction.OpCode.FlowControl!=FlowControl.Branch && index+1<method.Body.Instructions.Count) yield return index+1;
    }
    private static bool Reachable(MethodDefinition method,int start,int target,int blocked)
    {
        HashSet<int> seen=new HashSet<int>(); Stack<int> pending=new Stack<int>(); pending.Push(start);
        while(pending.Count>0)
        {
            int index=pending.Pop();
            if(index<0 || index>=method.Body.Instructions.Count || index==blocked || !seen.Add(index)) continue;
            if(index==target) return true;
            foreach(int next in Successors(method,index)) pending.Push(next);
        }
        return false;
    }
    private static int ResultLocal(MethodDefinition method,string helper)
    {
        int call=CallIndex(method,Presentation,helper);
        Check(call>=0,method.Name+" calls "+helper);
        int next=call+1;
        while(next<method.Body.Instructions.Count && method.Body.Instructions[next].OpCode.Code==Code.Nop) next++;
        int local=Local(method.Body.Instructions[next],true);
        Check(local>=0,helper+" result is retained for the entire frame");
        int stores=0;
        foreach(Instruction instruction in method.Body.Instructions) if(Local(instruction,true)==local) stores++;
        Check(stores==1,helper+" state cannot be overwritten by another frame decision");
        return local;
    }
    private static void GatedByLocal(MethodDefinition method,int local,int protectedCall,string label)
    {
        Check(protectedCall>=0,label+" exists");
        bool guarded=false;
        for(int i=0;i+1<protectedCall;i++)
        {
            if(Local(method.Body.Instructions[i],false)!=local) continue;
            Instruction branch=method.Body.Instructions[i+1];
            bool trueJump=branch.OpCode.Code==Code.Brtrue || branch.OpCode.Code==Code.Brtrue_S;
            bool falseJump=branch.OpCode.Code==Code.Brfalse || branch.OpCode.Code==Code.Brfalse_S;
            if(!trueJump && !falseJump) continue;
            int jump=method.Body.Instructions.IndexOf((Instruction)branch.Operand);
            int allowed=trueJump?jump:i+2,denied=trueJump?i+2:jump;
            if(!Reachable(method,0,protectedCall,i+1) && Reachable(method,allowed,protectedCall,-1) && !Reachable(method,denied,protectedCall,-1))
            { guarded=true; break; }
        }
        Check(guarded,label+" is reachable only when its presentation state is true");
    }
    private static bool CallsReachable(MethodDefinition start,string owner,string name)
    {
        HashSet<MethodDefinition> seen=new HashSet<MethodDefinition>(); Stack<MethodDefinition> pending=new Stack<MethodDefinition>(); pending.Push(start);
        while(pending.Count>0)
        {
            MethodDefinition method=pending.Pop(); if(!method.HasBody || !seen.Add(method)) continue;
            foreach(Instruction instruction in method.Body.Instructions)
            {
                if(Call(instruction,owner,name)) return true;
                MethodReference called=instruction.Operand as MethodReference;
                if(called==null || called.DeclaringType.FullName!=start.DeclaringType.FullName) continue;
                foreach(MethodDefinition candidate in start.DeclaringType.Methods)
                    if(candidate.FullName==called.FullName) pending.Push(candidate);
            }
        }
        return false;
    }
    private static void NoAllocations(TypeDefinition presentation)
    {
        foreach(string name in new string[] {"Visible","CanAttack","Layer"})
        {
            MethodDefinition method=Method(presentation,name);
            foreach(Instruction instruction in method.Body.Instructions)
                Check(instruction.OpCode.Code!=Code.Newobj && instruction.OpCode.Code!=Code.Newarr && instruction.OpCode.Code!=Code.Box,
                    name+" has no per-frame allocation instructions");
        }
    }
    private static void Artifact(string path)
    {
        using(AssemblyDefinition assembly=AssemblyDefinition.ReadAssembly(path))
        {
            TypeDefinition presentation=assembly.MainModule.GetType(Presentation);
            Check(presentation!=null,assembly.Name.Name+" missing independent mace presentation decisions");
            NoAllocations(presentation);
            TypeDefinition runtime=assembly.MainModule.GetType("MaceMod.Shared.MaceRuntime");
            Check(runtime!=null,"compiled artifact includes cooperative mace runtime");
            MethodDefinition tick=Method(runtime,"Tick");
            int visible=ResultLocal(tick,"Visible"),attack=ResultLocal(tick,"CanAttack");
            Check(visible!=attack,"visual presence and attack permission use separate frame state");
            GatedByLocal(tick,visible,CallIndex(tick,"MaceMod.Shared.MaceVisuals","Create"),"Held mace creation");
            int sends=0,history=0;
            for(int i=0;i<tick.Body.Instructions.Count;i++)
            {
                Instruction instruction=tick.Body.Instructions[i];
                if(Call(instruction,"MaceMod.Shared.MaceTransport","SendHit"))
                { sends++; GatedByLocal(tick,attack,i,"Gameplay hit transmission"); }
                FieldReference field=instruction.Operand as FieldReference;
                if(field==null || field.Name!="hadTip" || instruction.OpCode.Code!=Code.Stfld) continue;
                if(i>0 && Local(tick.Body.Instructions[i-1],false)==attack) history++;
                else Check(i>0 && tick.Body.Instructions[i-1].OpCode.Code==Code.Ldc_I4_0,
                    "Swing history is cleared or derived from CanAttack, never visual presence");
            }
            Check(sends>0 && history==1,"CanAttack gates hits and rearms history after closing menu");
            TypeDefinition visuals=assembly.MainModule.GetType("MaceMod.Shared.MaceVisuals");
            Check(visuals!=null,"compiled mace visuals present");
            MethodDefinition create=Method(visuals,"Create");
            Check(CallsReachable(create,Presentation,"Layer"),"Mace creation selects a presentation layer");
            Check(CallsReachable(create,"UnityEngine.Camera","get_cullingMask"),"Mace presentation layer comes from the active camera culling mask");
            Check(!CallsReachable(create,"UnityEngine.GameObject","get_layer"),"Mace creation cannot inherit the potentially culled offline-hand layer");
            Check(CallIndex(create,"UnityEngine.GameObject","set_layer")>=0,"Created mace receives its selected visible layer");
            Console.WriteLine("Verified mace visibility: "+path);
        }
    }
    private static void Decisions()
    {
        Type presentation=typeof(MaceVisibilityTests).Assembly.GetType(Presentation);
        Check(presentation!=null,"Compile MaceVisibilityTests with shared/MacePresentation.cs for pure decision tests");
        BindingFlags flags=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        Func<bool,bool> visible=(Func<bool,bool>)Delegate.CreateDelegate(typeof(Func<bool,bool>),presentation.GetMethod("Visible",flags));
        Func<bool,bool,bool> attack=(Func<bool,bool,bool>)Delegate.CreateDelegate(typeof(Func<bool,bool,bool>),presentation.GetMethod("CanAttack",flags));
        Func<int,int> layer=(Func<int,int>)Delegate.CreateDelegate(typeof(Func<int,int>),presentation.GetMethod("Layer",flags));
        foreach(bool equipped in new bool[] {false,true})
        foreach(bool menuOpen in new bool[] {false,true})
        {
            Check(visible(equipped)==equipped,"An equipped mace stays visible with either menu state");
            Check(attack(equipped,menuOpen)==(equipped && !menuOpen),"Menu suspends attacks while equipment controls visibility");
        }
        Check(layer(0)==0,"empty camera mask has a stable default fallback");
        Check(layer(-1)==0,"default layer is preferred whenever the camera sees it");
        Check(layer(-2)==1,"camera mask with default hidden selects the first visible layer");
        Check(layer(int.MinValue)==31,"sign-bit-only camera mask selects layer31");
        Check(layer(unchecked((int)0xA0000000))==29,"multiple upper mask bits select the first visible layer");
        for(int bit=0;bit<32;bit++)
        {
            int mask=unchecked(1<<bit);
            Check(layer(mask)==bit,"single camera mask bit selects layer"+bit);
            Check(layer(mask|1)==0,"default-visible camera mask prefers layer0 over layer"+bit);
            if(bit<31) Check(layer(mask|int.MinValue)==bit,"camera mask selects lower layer before sign bit"+bit);
        }
    }
    public static int Main(string[] args)
    {
        try
        {
            Check(args.Length>=1 && args.Length<=2,"Usage: MaceVisibilityTests MaceMod.dll [TotemMod.dll]");
            foreach(string path in args) Artifact(path);
            Decisions(); Console.WriteLine("PASS MaceVisibilityTests ("+checks+" checks)"); return 0;
        }
        catch(Exception error) { Console.Error.WriteLine("FAIL MaceVisibilityTests: "+error.Message); return 1; }
    }
}
