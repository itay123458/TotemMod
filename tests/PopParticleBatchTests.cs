using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class PopParticleBatchTests
{
    private static int checks;
    private static void Check(bool condition,string message) { checks++; if(!condition) throw new Exception(message); }
    private static int Calls(MethodDefinition method,string type,string name)
    {
        int result=0;
        foreach(Instruction instruction in method.Body.Instructions)
        {
            MethodReference called=instruction.Operand as MethodReference;
            if(called!=null && called.DeclaringType.FullName==type && called.Name==name) result++;
        }
        return result;
    }
    private static MethodDefinition Method(TypeDefinition type,string name)
    {
        foreach(MethodDefinition method in type.Methods) if(method.Name==name) return method;
        throw new Exception("Missing method: "+name);
    }
    public static int Main(string[] args)
    {
        try
        {
            using(AssemblyDefinition assembly=AssemblyDefinition.ReadAssembly(args[0]))
            {
                TypeDefinition type=assembly.MainModule.GetType("TotemMod.PopParticleBatch");
                Check(type!=null,"Pop effects need a single combined particle renderer.");
                MethodDefinition constructor=Method(type,".ctor"),tick=Method(type,"Tick"),dispose=Method(type,"Dispose");
                Dictionary<string,int> components=new Dictionary<string,int>();
                foreach(MethodDefinition method in type.Methods)
                {
                    if(!method.HasBody) continue;
                    foreach(Instruction instruction in method.Body.Instructions)
                    {
                        MethodReference called=instruction.Operand as MethodReference;
                        if(called==null) continue;
                        Check(called.Name!="CreatePrimitive","Particle meshes must not acquire primitive colliders.");
                        if(called.Name!="AddComponent") continue;
                        GenericInstanceMethod component=called as GenericInstanceMethod;
                        Check(component!=null && component.GenericArguments.Count==1,"Particle components must have explicit types.");
                        string name=component.GenericArguments[0].FullName;
                        Check(name=="UnityEngine.MeshRenderer" || name=="UnityEngine.MeshFilter","Only a mesh renderer/filter may be added to a particle burst.");
                        Check(method==constructor,"Particle render components are created once per burst.");
                        if(!components.ContainsKey(name)) components[name]=0;
                        components[name]++;
                    }
                }
                Check(components.ContainsKey("UnityEngine.MeshRenderer") && components["UnityEngine.MeshRenderer"]==1,"A burst has exactly one MeshRenderer creation site.");
                Check(components.ContainsKey("UnityEngine.MeshFilter") && components["UnityEngine.MeshFilter"]==1,"A burst has exactly one MeshFilter creation site.");
                Check(Calls(constructor,"UnityEngine.GameObject",".ctor")==1,"A burst uses one render object.");
                Check(Calls(constructor,"UnityEngine.Mesh",".ctor")==1,"A burst combines its particles into one mesh.");
                Check(Calls(constructor,"UnityEngine.Material",".ctor")==1,"A burst uses one vertex-color material.");
                foreach(Instruction instruction in tick.Body.Instructions)
                {
                    // Value-type construction (Bounds/Vector3) is stack data;
                    // heap object/array construction in the frame loop is a regression.
                    Check(instruction.OpCode!=OpCodes.Newarr,"Tick must not allocate arrays.");
                    MethodReference called=instruction.Operand as MethodReference;
                    if(called==null) continue;
                    Check(instruction.OpCode!=OpCodes.Newobj || called.DeclaringType.IsValueType,"Tick must not allocate managed objects.");
                    Check(called.DeclaringType.FullName!="UnityEngine.Transform","Particle frames update combined mesh vertices, without per-particle Transform writes.");
                }
                Check(Calls(tick,"TotemMod.PopParticleStyle","Evaluate")==1,"The batch preserves the existing motion evaluator.");
                Check(Calls(tick,"UnityEngine.Mesh","set_vertices")==1,"All particle vertices upload in one mesh update.");
                Check(Calls(constructor,"UnityEngine.Mesh","set_colors32")==1,"Particle palette colors are stored in the combined mesh.");
                Check(Calls(dispose,"UnityEngine.Object","Destroy")==3,"Disposal releases the render object, mesh, and owned material.");
                Console.WriteLine("PASS "+checks+" single-renderer particle batching, allocation and cleanup checks");
            }
            return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}
