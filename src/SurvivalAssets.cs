using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace TotemMod
{
    // All UI sprites share one point-filtered atlas. Food keeps its original
    // readable 16px texture separately for the held pixel meshes.
    internal sealed class SurvivalAssets : IDisposable
    {
        private readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        private readonly Dictionary<CraftItem, Texture2D> food = new Dictionary<CraftItem, Texture2D>();
        private Texture2D atlas;
        private bool loaded;
        internal AudioClip EatSound { get; private set; }
        internal AudioClip BurpSound { get; private set; }
        internal AudioClip HurtSound { get; private set; }
        internal AudioClip ClickSound { get; private set; }

        internal void Load()
        {
            if (loaded) return;
            string[] names = { "heart-container", "heart-full", "heart-half", "food-empty", "food-full", "food-half",
                "hotbar", "hotbar-selection", "offhand", "inventory", "apple", "bread", "golden-apple", "totem" };
            Texture2D[] sources = new Texture2D[names.Length];
            try
            {
                for (int i = 0; i < names.Length; i++)
                {
                    sources[i] = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    byte[] bytes = Read(names[i].Replace('-', '_') + ".png", names[i] == "totem" ? "TotemMod." : "PrimalCraft.Survival.", true);
                    if (!ImageConversion.LoadImage(sources[i], bytes, false)) throw new InvalidDataException("Invalid survival image: " + names[i]);
                    sources[i].filterMode = FilterMode.Point;
                    sources[i].wrapMode = TextureWrapMode.Clamp;
                }
                atlas = new Texture2D(512, 512, TextureFormat.RGBA32, false);
                Rect[] packed = atlas.PackTextures(sources, 2, 1024, false);
                atlas.name = "PrimalCraft survival UI atlas";
                atlas.filterMode = FilterMode.Point;
                atlas.wrapMode = TextureWrapMode.Clamp;
                for (int i = 0; i < names.Length; i++)
                {
                    Rect rect = new Rect(Mathf.Round(packed[i].x * atlas.width), Mathf.Round(packed[i].y * atlas.height),
                        Mathf.Round(packed[i].width * atlas.width), Mathf.Round(packed[i].height * atlas.height));
                    Add(names[i], rect);
                    if (names[i] == "inventory")
                    {
                        // Only the storage/hotbar and outer border of the original
                        // texture: no non-functional crafting or armor controls.
                        Add("inventory-bottom", new Rect(rect.x, rect.y + 90, 176, 86));
                        Add("inventory-top", new Rect(rect.x, rect.y + 250, 176, 6));
                    }
                }
                food.Add(CraftItem.Apple, sources[10]);
                food.Add(CraftItem.Bread, sources[11]);
                food.Add(CraftItem.GoldenApple, sources[12]);
                EatSound = Sound("eat", false);
                BurpSound = Sound("burp", false);
                HurtSound = Sound("hurt", false);
                ClickSound = Sound("click", true);
                loaded = true;
            }
            catch { Dispose(); throw; }
            finally
            {
                for (int i = 0; i < sources.Length; i++)
                    if (sources[i] != null && !food.ContainsValue(sources[i])) UnityEngine.Object.Destroy(sources[i]);
            }
        }

        private void Add(string name, Rect rect)
        {
            Sprite sprite = Sprite.Create(atlas, rect, new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect);
            sprite.name = "Minecraft " + name;
            sprites.Add(name, sprite);
        }

        internal Sprite Get(string name)
        {
            Sprite sprite;
            return name != null && sprites.TryGetValue(name, out sprite) ? sprite : null;
        }

        internal Sprite ItemSprite(CraftItem item)
        {
            switch (item)
            {
                case CraftItem.Apple: return Get("apple");
                case CraftItem.Bread: return Get("bread");
                case CraftItem.GoldenApple: return Get("golden-apple");
                default: return null;
            }
        }

        internal Texture2D ItemTexture(CraftItem item)
        {
            Texture2D texture;
            return food.TryGetValue(item, out texture) ? texture : null;
        }

        private static byte[] Read(string name, string prefix, bool required)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(prefix + name))
            {
                if (stream == null)
                {
                    if (required) throw new InvalidDataException("Missing local Minecraft survival asset: " + name);
                    return null;
                }
                using (MemoryStream data = new MemoryStream()) { stream.CopyTo(data); return data.ToArray(); }
            }
        }

        private static AudioClip Sound(string name, bool menuFallback)
        {
            byte[] data = Read(name + ".pcm", "PrimalCraft.Survival.", false);
            if (data == null && menuFallback) data = Read("click.pcm", "TotemMod.Menu.", false);
            if (data == null) return null;
            if (data.Length == 0 || data.Length % 4 != 0) throw new InvalidDataException("Invalid survival PCM: " + name);
            float[] samples = new float[data.Length / 4];
            Buffer.BlockCopy(data, 0, samples, 0, data.Length);
            AudioClip clip = AudioClip.Create("Minecraft survival " + name, samples.Length, 1, 22050, false);
            if (!clip.SetData(samples, 0)) { UnityEngine.Object.Destroy(clip); throw new InvalidOperationException("Cannot decode survival PCM: " + name); }
            return clip;
        }

        public void Dispose()
        {
            foreach (Sprite sprite in sprites.Values) UnityEngine.Object.Destroy(sprite);
            foreach (Texture2D texture in food.Values) UnityEngine.Object.Destroy(texture);
            sprites.Clear(); food.Clear();
            UnityEngine.Object.Destroy(atlas); atlas = null;
            UnityEngine.Object.Destroy(EatSound); EatSound = null;
            UnityEngine.Object.Destroy(BurpSound); BurpSound = null;
            UnityEngine.Object.Destroy(HurtSound); HurtSound = null;
            UnityEngine.Object.Destroy(ClickSound); ClickSound = null;
            loaded = false;
        }
    }
}
