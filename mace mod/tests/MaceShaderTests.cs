using System;
using System.Collections.Generic;
using MaceMod.Shared;

// Test doubles exercise selection only. They do not simulate GPU rendering.
namespace UnityEngine
{
    internal sealed class Shader
    {
        internal readonly string Name;
        internal readonly bool isSupported;
        internal static readonly Dictionary<string,Shader> Available=new Dictionary<string,Shader>();
        internal Shader(string name,bool supported) { Name=name; isSupported=supported; Available[name]=this; }
        internal static Shader Find(string name) { Shader value; return Available.TryGetValue(name,out value)?value:null; }
    }
}
namespace UnityEngine.Rendering
{
    internal static class GraphicsSettings { internal static object currentRenderPipeline; }
}
internal static class MaceShaderTests
{
    private static int checks;
    private static void Check(bool value,string message) { checks++; if(!value) throw new Exception(message); }
    private static bool Rejects()
    { try { MaceShader.Select(); return false; } catch(InvalidOperationException) { return true; } }
    private static int Main()
    {
        try
        {
            for(int srp=0;srp<2;srp++)
            for(int unlit=0;unlit<3;unlit++)
            for(int urp=0;urp<3;urp++)
            for(int standard=0;standard<3;standard++)
            {
                UnityEngine.Shader.Available.Clear();
                UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline=srp==0?null:new object();
                if(unlit>0) new UnityEngine.Shader("Unlit/Texture",unlit==2);
                if(urp>0) new UnityEngine.Shader("Universal Render Pipeline/Unlit",urp==2);
                if(standard>0) new UnityEngine.Shader("Standard",standard==2);
                string expected=unlit==2?"Unlit/Texture":urp==2?"Universal Render Pipeline/Unlit":srp==0 && standard==2?"Standard":null;
                if(expected==null) Check(Rejects(),"Never select an unsupported or SRP-incompatible shader");
                else Check(MaceShader.Select().Name==expected,"Prefer the visible totem's unlit shader, then a supported fallback");
            }
            Console.WriteLine("PASS "+checks+" shader availability/support/pipeline selection checks; GPU rendering requires in-game verification");
            return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}
