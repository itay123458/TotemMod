using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace TotemMod
{
    internal sealed class MinecraftMenuAssets : IDisposable
    {
        private readonly List<Texture2D> textures = new List<Texture2D>();
        private readonly List<Sprite> sprites = new List<Sprite>();
        internal MenuSkin Skin { get; private set; }
        internal Sprite Dirt { get; private set; }

        private static byte[] Read(string name)
        {
            using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("TotemMod.Menu." + name))
            {
                if (source == null) throw new InvalidDataException("Missing Minecraft menu asset: " + name);
                using (MemoryStream data = new MemoryStream()) { source.CopyTo(data); return data.ToArray(); }
            }
        }

        private Sprite Image(string name,bool sliced)
        {
            Texture2D texture = new Texture2D(2,2,TextureFormat.RGBA32,false);
            textures.Add(texture);
            if (!ImageConversion.LoadImage(texture,Read(name),false)) throw new InvalidDataException("Cannot decode menu asset: " + name);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = sliced ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            Sprite sprite = Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(0.5f,0.5f),100,0,SpriteMeshType.FullRect,
                sliced ? new Vector4(2,2,2,2) : Vector4.zero);
            sprites.Add(sprite);
            return sprite;
        }

        public void Load()
        {
            if (Skin != null) return;
            MenuSkin next = new MenuSkin();
            try
            {
                next.Background = Image("background.png",false);
                Dirt = Image("dirt.png",false);
                next.Button = Image("button.png",true);
                next.ButtonHighlighted = Image("button-highlighted.png",true);
                next.ButtonDisabled = Image("button-disabled.png",true);
                next.Slider = Image("slider.png",true);
                next.SliderHighlighted = Image("slider-highlighted.png",true);
                next.Handle = Image("handle.png",true);
                next.HandleHighlighted = Image("handle-highlighted.png",true);
                byte[] data = Read("click.pcm");
                if (data.Length == 0 || data.Length % 4 != 0) throw new InvalidDataException("Invalid menu click sound.");
                float[] samples = new float[data.Length/4];
                Buffer.BlockCopy(data,0,samples,0,data.Length);
                next.Click = AudioClip.Create("Minecraft ui.button.click",samples.Length,1,22050,false);
                if (!next.Click.SetData(samples,0)) throw new InvalidOperationException("Cannot load menu click audio.");
                Skin = next;
            }
            catch
            {
                UnityEngine.Object.Destroy(next.Click);
                Dispose(); throw;
            }
        }

        public void Dispose()
        {
            if (Skin != null) UnityEngine.Object.Destroy(Skin.Click);
            foreach (Sprite sprite in sprites) UnityEngine.Object.Destroy(sprite);
            foreach (Texture2D texture in textures) UnityEngine.Object.Destroy(texture);
            sprites.Clear(); textures.Clear(); Skin = null; Dirt = null;
        }
    }
}
