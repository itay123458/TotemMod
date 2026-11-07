using UnityEngine;

namespace TotemMod
{
    // Tint a tiny pixel texture while keeping the solid, depth-writing shader.
    internal sealed class TotemTintTexture : MonoBehaviour
    {
        private Texture2D texture;
        private Color32[] source,pixels;
        private Renderer target;
        private readonly MaterialPropertyBlock properties=new MaterialPropertyBlock();
        private Color32 last;
        private bool applied;
        internal void Initialize(Texture2D original,Renderer renderer)
        {
            target=renderer; source=original.GetPixels32(); pixels=new Color32[source.Length];
            texture=new Texture2D(original.width,original.height,TextureFormat.RGBA32,false);
            texture.filterMode=FilterMode.Point; texture.wrapMode=TextureWrapMode.Clamp;
        }
        internal void Apply(Color color)
        {
            Color32 tint=color;
            if (applied && tint.r==last.r && tint.g==last.g && tint.b==last.b && tint.a==last.a) return;
            last=tint; applied=true;
            for (int i=0;i<pixels.Length;i++) pixels[i]=new Color32((byte)(source[i].r*tint.r/255),(byte)(source[i].g*tint.g/255),(byte)(source[i].b*tint.b/255),source[i].a);
            texture.SetPixels32(pixels); texture.Apply(false,false);
            properties.Clear(); properties.SetTexture("_MainTex",texture); target.SetPropertyBlock(properties);
        }
        private void OnDestroy() { UnityEngine.Object.Destroy(texture); }
    }
}
