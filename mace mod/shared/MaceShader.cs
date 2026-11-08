using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace MaceMod.Shared
{
    internal static class MaceShader
    {
        internal static Shader Select()
        {
            // Match the visible totem. Legacy unlit works with the game's
            // original renderer and URP; Standard's lit passes do not.
            Shader shader=Supported("Unlit/Texture") ?? Supported("Universal Render Pipeline/Unlit");
            if(shader!=null) return shader;
            if(GraphicsSettings.currentRenderPipeline==null)
            {
                shader=Supported("Standard");
                if(shader!=null) return shader;
            }
            throw new InvalidOperationException("No compatible opaque mace shader is available for the active render pipeline.");
        }

        private static Shader Supported(string name)
        {
            Shader shader=Shader.Find(name);
            return shader!=null && shader.isSupported ? shader : null;
        }
    }
}
