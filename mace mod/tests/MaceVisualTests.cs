using System;
using System.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class MaceVisualTests
{
    private static int checks;
    private static void Check(bool value,string message) { checks++; if(!value) throw new Exception(message); }
    private static MethodDefinition Method(TypeDefinition type,string name)
    { foreach(MethodDefinition method in type.Methods) if(method.Name==name) return method; throw new Exception("Missing "+name); }
    private static int Constant(TypeDefinition type,string name)
    { foreach(FieldDefinition field in type.Fields) if(field.Name==name) return Convert.ToInt32(field.Constant); throw new Exception("Missing "+name); }
    private static bool Calls(MethodDefinition method,string name)
    { foreach(Instruction instruction in method.Body.Instructions) { MethodReference called=instruction.Operand as MethodReference; if(called!=null&&called.Name==name) return true; } return false; }
    private static int Main(string[] args)
    {
        try
        {
            using(AssemblyDefinition assembly=AssemblyDefinition.ReadAssembly(args[0]))
            {
                TypeDefinition visuals=assembly.MainModule.GetType("MaceMod.Shared.MaceVisuals");
                Check(visuals!=null,"Missing mace render class");
                TypeDefinition plugin=assembly.MainModule.GetType("MaceMod.MacePlugin");
                Check(plugin!=null,"Missing standalone plugin");
                Check(Calls(Method(plugin,"OnDisable"),"DisconnectClient"),"Disabling standalone must release the runtime client");
                Check(Calls(Method(plugin,"DisconnectClient"),"Dispose"),"Disconnect must dispose runtime ownership");
                Check(Calls(Method(plugin,"OnEnable"),"ConnectClient"),"Re-enabling standalone must reconnect runtime ownership");
                Check(Calls(Method(plugin,"Awake"),"EnsureInstalled"),"Standalone must bootstrap canonical Utilla compatibility");
                Check(Constant(visuals,"SparkLimit")<=64,"Impact spark pool must be bounded to 64 total");
                Check(Constant(visuals,"SparksPerHit")<=16,"One impact must emit at most 16 sparks");
                foreach(MethodDefinition method in visuals.Methods)
                {
                    if(!method.HasBody) continue;
                    foreach(Instruction instruction in method.Body.Instructions)
                    {
                        MethodReference called=instruction.Operand as MethodReference;
                        if(called!=null)
                        {
                            Check(called.Name!="CreatePrimitive","Primitive creation adds a hidden collider");
                            Check(called.Name!="BakeMesh" && called.Name!="Instantiate","Render-only mace must not clone/bake rigs");
                            GenericInstanceMethod generic=called as GenericInstanceMethod;
                            if(called.Name=="AddComponent" && generic!=null)
                                foreach(TypeReference argument in generic.GenericArguments)
                                    Check(argument.FullName=="UnityEngine.MeshFilter"||argument.FullName=="UnityEngine.MeshRenderer"||argument.FullName=="UnityEngine.AudioSource","Unexpected mace behaviour "+argument);
                        }
                        if(method.Name=="Tick"||method.Name=="Pose"||method.Name=="Impact")
                            Check(instruction.OpCode!=OpCodes.Newarr && (instruction.OpCode!=OpCodes.Newobj || (called!=null && (called.DeclaringType.FullName=="UnityEngine.Color" || called.DeclaringType.FullName=="UnityEngine.Vector3"))),"Mace hot path must not allocate reference objects or arrays");
                    }
                }
                Check(Calls(Method(visuals,"Pose"),"SetPropertyBlock"),"Glint should tint the opaque mesh without extra transparent shells");
                Check(Calls(Method(visuals,"BuildMesh"),"Build"),"Mace must use the tested closed pixel extrusion");
                Check(!Calls(Method(visuals,"Tick"),"GetPixels32"),"Particles must cache pixel geometry");
                foreach(string name in new[] {"TotemMod.UtillaFix.dll","TotemMod.UtillaFix.sha256"})
                {
                    EmbeddedResource found=null;
                    foreach(Resource resource in assembly.MainModule.Resources) if(resource.Name==name) found=resource as EmbeddedResource;
                    Check(found!=null,"Missing standalone Utilla repair resource "+name);
                    Check(found.GetResourceData().Length>0,"Empty standalone Utilla repair resource "+name);
                }
                foreach(string name in new[] {"mace.png","critical_hit.png","hit.pcm","smash.pcm","burst.pcm"})
                {
                    EmbeddedResource found=null;
                    foreach(Resource resource in assembly.MainModule.Resources) if(resource.Name=="MaceMod."+name) found=resource as EmbeddedResource;
                    Check(found!=null,"Missing Minecraft resource "+name);
                    byte[] data=found.GetResourceData(); Check(data.Length>64,"Empty Minecraft resource "+name);
                    if(name.EndsWith(".pcm",StringComparison.Ordinal)) Check(data.Length%4==0,"PCM must contain complete float32 samples");
                }
            }
            Console.WriteLine("PASS "+checks+" mace bounded effects, render safety, opaque shimmer, allocation and embedded asset checks");
            return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}
