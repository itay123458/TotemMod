using System;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Compiled architecture checks only: actual Unity/GPU visibility still requires
// the game's one-shot diagnostic log and a headset/render observation.
internal static class MaceWorldPoseTests
{
    private static int checks;
    private static void Check(bool value,string message) { checks++; if(!value) throw new Exception(message); }
    private static MethodDefinition Method(TypeDefinition type,string name)
    { foreach(MethodDefinition method in type.Methods) if(method.Name==name) return method; throw new Exception("Missing "+name); }
    private static bool Calls(MethodDefinition method,string type,string name)
    {
        foreach(Instruction instruction in method.Body.Instructions)
        {
            MethodReference called=instruction.Operand as MethodReference;
            if(called!=null && called.DeclaringType.FullName==type && called.Name==name) return true;
        }
        return false;
    }
    private static int Main(string[] args)
    {
        try
        {
            using(AssemblyDefinition assembly=AssemblyDefinition.ReadAssembly(args[0]))
            {
                TypeDefinition type=assembly.MainModule.GetType("MaceMod.Shared.MaceVisuals");
                Check(type!=null,"Missing mace renderer");
                Check(Calls(Method(type,"OpaqueMaterial"),"MaceMod.Shared.MaceShader","Select"),"Materials must use pipeline-compatible shader selection instead of Standard-first lookup");
                MethodDefinition create=Method(type,"Create"),pose=Method(type,"Pose"),head=Method(type,"HeadPosition");
                Check(pose.Parameters.Count==6,"World pose requires explicit player scale");
                Check(head.Parameters.Count==3,"World head position requires explicit player scale");
                Check(pose.Parameters[5].IsOptional && Convert.ToSingle(pose.Parameters[5].Constant)==1f,"Pose player scale must default to one");
                Check(head.Parameters[2].IsOptional && Convert.ToSingle(head.Parameters[2].Constant)==1f,"Head player scale must default to one");
                bool ownedRoot=false;
                foreach(Instruction instruction in create.Body.Instructions)
                {
                    Check(instruction.OpCode!=OpCodes.Ldarg_1,"Mace creation must not use the input hand as a render parent");
                    FieldReference field=instruction.Operand as FieldReference;
                    if(field!=null && field.Name=="effects") ownedRoot=true;
                }
                Check(ownedRoot && Calls(create,"UnityEngine.Transform","SetParent"),"Mace must attach only to its owned effects root");
                foreach(MethodDefinition method in new[] {pose,head})
                {
                    Check(!Calls(method,"UnityEngine.Transform","TransformPoint"),"Hand inherited scale must not determine rendered or collision head pose");
                    Check(!Calls(method,"UnityEngine.Transform","get_lossyScale"),"World pose must use explicit player scale");
                    Check(!Calls(method,"UnityEngine.Transform","SetParent"),"Pose must never reparent the renderer to a hand");
                    Check(Calls(method,"UnityEngine.Transform","get_position") && Calls(method,"UnityEngine.Transform","get_rotation"),"Pose and head must use the same world hand transform");
                    Check(Calls(method,"MaceMod.Shared.MaceVisuals","SafeScale") && Calls(method,"MaceMod.Shared.MaceVisuals","SafePlayerScale"),"Pose and head must apply identical validated scales");
                }
                Check(Calls(pose,"UnityEngine.Transform","set_position") && Calls(pose,"UnityEngine.Transform","set_rotation") && Calls(pose,"UnityEngine.Transform","set_localScale"),"Item must receive world position/rotation and explicit scale");
                Check(!Calls(pose,"UnityEngine.Transform","set_localPosition") && !Calls(pose,"UnityEngine.Transform","set_localRotation"),"Pose must not depend on inherited hand hierarchy");
                MethodDefinition diagnostics=Method(type,"Diagnostics");
                Check(diagnostics.IsPublic && diagnostics.ReturnType.FullName=="System.String","Main-thread diagnostics must return a string");
                Check(Calls(diagnostics,"UnityEngine.Camera","WorldToViewportPoint"),"Diagnostic log must expose actual viewport position");
                Check(Calls(diagnostics,"UnityEngine.Camera","get_cullingMask"),"Diagnostic log must expose actual camera mask");
                Check(Calls(diagnostics,"UnityEngine.Shader","get_isSupported"),"Diagnostic log must expose shader compatibility");
                foreach(string hot in new[] {"Pose","Tick","Impact"})
                {
                    MethodDefinition method=Method(type,hot);
                    Check(!Calls(method,"MaceMod.Shared.MaceVisuals","Diagnostics"),"Diagnostics must never run in a hot path");
                    foreach(Instruction instruction in method.Body.Instructions)
                    {
                        Check(instruction.OpCode!=OpCodes.Newarr,"Mace hot path must not allocate arrays");
                        if(instruction.OpCode!=OpCodes.Newobj) continue;
                        MethodReference called=(MethodReference)instruction.Operand;
                        Check(called.DeclaringType.FullName=="UnityEngine.Color" || called.DeclaringType.FullName=="UnityEngine.Vector3","Mace hot path must not allocate reference objects");
                    }
                }
                TypeDefinition runtime=assembly.MainModule.GetType("MaceMod.Shared.MaceRuntime");
                Check(Calls(Method(runtime,".ctor"),"UnityEngine.Application","add_onBeforeRender"),"VR pose must follow immediately before rendering");
                Check(Calls(Method(runtime,"Detach"),"UnityEngine.Application","remove_onBeforeRender"),"Last detach must remove the render callback");
                Check(Calls(Method(runtime,"Tick"),"MaceMod.Shared.MaceHandAnchor","Resolve"),"Collision sampling must use the palm anchor");
                Check(Calls(Method(runtime,"Render"),"MaceMod.Shared.MaceHandAnchor","Resolve"),"Rendering must use the same palm anchor");
                Check(!Calls(Method(runtime,"Render"),"MaceMod.Shared.MaceVisuals","Diagnostics"),"Diagnostics must not run on the render callback");
            }
            Console.WriteLine("PASS "+checks+" owned-root/world-pose/diagnostic architecture checks; Unity/GPU visibility remains unverified");
            return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}
