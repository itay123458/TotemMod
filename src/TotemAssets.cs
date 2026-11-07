using System;
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
            Shader shader = Shader.Find("Unlit/Texture") ?? Shader.Find("Standard");
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

        // One shared, closed mesh. Merged cap UVs preserve the exact texture;
        // exposed edges keep the source pixel's color and outward winding.
        private static Mesh BuildMesh(Texture2D image)
        {
            int width = image.width, height = image.height;
            Color32[] pixels = image.GetPixels32();
            byte[] alpha = new byte[pixels.Length];
            for (int i = 0; i < pixels.Length; i++) alpha[i] = pixels[i].a;
            PixelMeshData data = PixelMeshBuilder.Build(alpha,width,height,0.30f,0.7f);
            Vector3[] vertices = new Vector3[data.Positions.Length];
            Vector2[] uvs = new Vector2[data.Uvs.Length];
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = new Vector3(data.Positions[i].X,data.Positions[i].Y,data.Positions[i].Z);
            for (int i = 0; i < uvs.Length; i++) uvs[i] = new Vector2(data.Uvs[i].U,data.Uvs[i].V);
            Mesh mesh = new Mesh();
            mesh.name = "Extruded Minecraft totem pixels";
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = data.Triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
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
