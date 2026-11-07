using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace TotemMod
{
    internal sealed class TotemAssets : IDisposable
    {
        public Mesh Mesh { get; private set; }
        public Material Material { get; private set; }
        public AudioClip Pop { get; private set; }
        private Texture2D texture;

        private static byte[] ReadResource(string name)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (stream == null) throw new InvalidDataException("Missing embedded resource: " + name);
                using (MemoryStream copy = new MemoryStream()) { stream.CopyTo(copy); return copy.ToArray(); }
            }
        }

        public void Load()
        {
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.name = "Minecraft Totem of Undying";
            if (!ImageConversion.LoadImage(texture, ReadResource("TotemMod.totem.png"), false))
                throw new InvalidDataException("Unable to decode the totem texture.");
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            Shader shader = Shader.Find("Unlit/Texture") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("No compatible totem shader is available.");
            Material = new Material(shader);
            Material.name = "Totem pixel material";
            Material.mainTexture = texture;
            Mesh = BuildMesh(texture);

            byte[] data = ReadResource("TotemMod.pop.pcm");
            if (data.Length == 0 || data.Length % 4 != 0) throw new InvalidDataException("Invalid totem sound data.");
            float[] samples = new float[data.Length / 4];
            Buffer.BlockCopy(data, 0, samples, 0, data.Length);
            Pop = AudioClip.Create("Minecraft item.totem.use", samples.Length, 1, 44100, false);
            if (!Pop.SetData(samples, 0)) throw new InvalidOperationException("Unable to load the totem sound.");
        }

        public GameObject Create(Transform parent, string name)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = Mesh;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            go.AddComponent<TotemVisualMarker>();
            renderer.sharedMaterial = Material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        // One combined mesh: opaque pixels have front/back faces and exposed edges.
        // This preserves Minecraft's exact silhouette from both eyes and both sides.
        private static Mesh BuildMesh(Texture2D image)
        {
            int width = image.width, height = image.height;
            Color32[] pixels = image.GetPixels32();
            List<Vector3> vertices = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> triangles = new List<int>();
            float step = 0.30f / height;
            float depth = step * 0.7f;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                if (pixels[y * width + x].a < 128) continue;
                float l = (x - width * 0.5f) * step, r = l + step;
                float b = (y - height * 0.5f) * step, t = b + step;
                Vector2 uv = new Vector2((x + 0.5f) / width, (y + 0.5f) / height);
                AddFace(vertices, uvs, triangles, uv, new Vector3(l,b,-depth), new Vector3(l,t,-depth), new Vector3(r,t,-depth), new Vector3(r,b,-depth));
                AddFace(vertices, uvs, triangles, uv, new Vector3(r,b,depth), new Vector3(r,t,depth), new Vector3(l,t,depth), new Vector3(l,b,depth));
                if (x == 0 || pixels[y*width+x-1].a < 128)
                    AddFace(vertices, uvs, triangles, uv, new Vector3(l,b,depth), new Vector3(l,t,depth), new Vector3(l,t,-depth), new Vector3(l,b,-depth));
                if (x == width-1 || pixels[y*width+x+1].a < 128)
                    AddFace(vertices, uvs, triangles, uv, new Vector3(r,b,-depth), new Vector3(r,t,-depth), new Vector3(r,t,depth), new Vector3(r,b,depth));
                if (y == 0 || pixels[(y-1)*width+x].a < 128)
                    AddFace(vertices, uvs, triangles, uv, new Vector3(l,b,depth), new Vector3(l,b,-depth), new Vector3(r,b,-depth), new Vector3(r,b,depth));
                if (y == height-1 || pixels[(y+1)*width+x].a < 128)
                    AddFace(vertices, uvs, triangles, uv, new Vector3(l,t,-depth), new Vector3(l,t,depth), new Vector3(r,t,depth), new Vector3(r,t,-depth));
            }
            Mesh mesh = new Mesh();
            mesh.name = "Extruded Minecraft totem pixels";
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddFace(List<Vector3> v, List<Vector2> uv, List<int> ix, Vector2 color, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int n = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            for (int i = 0; i < 4; i++) uv.Add(color);
            ix.Add(n); ix.Add(n+1); ix.Add(n+2); ix.Add(n); ix.Add(n+2); ix.Add(n+3);
        }

        public void Dispose()
        {
            UnityEngine.Object.Destroy(Mesh);
            UnityEngine.Object.Destroy(Material);
            UnityEngine.Object.Destroy(texture);
            UnityEngine.Object.Destroy(Pop);
        }
    }
}
