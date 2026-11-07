using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace TotemMod
{
    // Each burst owns one dynamic mesh. The small immutable source silhouettes
    // are shared; palette and working vertex arrays are allocated only at birth.
    internal sealed class PopParticleBatch : IDisposable
    {
        private static readonly PixelMeshData Sparkle = PixelMeshBuilder.BuildBillboard(new[]
        {
            "...##...", "...##...", "..####..", "########",
            "########", "..####..", "...##...", "...##..."
        });
        private static readonly PixelMeshData Fleck = PixelMeshBuilder.BuildBillboard(new[] { "##", "##" });
        private readonly int count;
        private readonly float[] seeds;
        private readonly PixelMeshData[] sources;
        private readonly Vector3[] vertices;
        private GameObject root;
        private Mesh mesh;
        private Material material;
        private bool disposed;

        internal PopParticleBatch(Transform parent, int count, float[] seeds, Color[] colors, int shape, int layer)
        {
            this.count = Math.Max(0,Math.Min(64,count));
            if (this.count == 0) return;
            this.seeds = new float[this.count];
            sources = new PixelMeshData[this.count];
            int vertexCount = 0, indexCount = 0;
            for (int i = 0; i < this.count; i++)
            {
                this.seeds[i] = seeds != null && i < seeds.Length ? seeds[i] : 0.5f;
                sources[i] = shape == 1 || shape != 2 && i%3 != 2 ? Sparkle : Fleck;
                vertexCount += sources[i].Positions.Length;
                indexCount += sources[i].Triangles.Length;
            }
            vertices = new Vector3[vertexCount];
            Color32[] vertexColors = new Color32[vertexCount];
            Vector2[] uvs = new Vector2[vertexCount];
            int[] indices = new int[indexCount];
            int vertexOffset = 0, indexOffset = 0;
            for (int i = 0; i < this.count; i++)
            {
                PixelMeshData source = sources[i];
                Color32 color = colors != null && i < colors.Length ? (Color32)colors[i] : (Color32)Color.white;
                for (int j = 0; j < source.Positions.Length; j++) vertexColors[vertexOffset+j] = color;
                for (int j = 0; j < source.Triangles.Length; j++) indices[indexOffset+j] = vertexOffset+source.Triangles[j];
                vertexOffset += source.Positions.Length;
                indexOffset += source.Triangles.Length;
            }
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) throw new InvalidOperationException("No vertex-color particle shader is available.");
            try
            {
                root = new GameObject("Totem particle burst");
                root.layer = Math.Max(0,Math.Min(31,layer));
                root.transform.SetParent(parent,false);
                mesh = new Mesh();
                mesh.name = "Combined totem pixel particles";
                mesh.MarkDynamic();
                mesh.vertices = vertices;
                mesh.uv = uvs;
                mesh.triangles = indices;
                mesh.colors32 = vertexColors;
                material = new Material(shader);
                material.name = "Totem particle vertex colors";
                material.mainTexture = Texture2D.whiteTexture;
                material.color = Color.white;
                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer renderer = root.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                Tick(0,1,1,0,1);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal void Tick(float elapsed, float size, float spread, int motion, float spin)
        {
            if (disposed || count == 0 || mesh == null) return;
            spin = float.IsNaN(spin) || float.IsInfinity(spin) ? 1 : Math.Max(0,Math.Min(2,spin));
            Vector3 minimum = new Vector3(float.PositiveInfinity,float.PositiveInfinity,float.PositiveInfinity);
            Vector3 maximum = new Vector3(float.NegativeInfinity,float.NegativeInfinity,float.NegativeInfinity);
            int offset = 0;
            for (int i = 0; i < count; i++)
            {
                PopSparkPose pose = PopParticleStyle.Evaluate(i,count,seeds[i],elapsed,size,spread,motion);
                float angle = pose.Rotation*spin*Mathf.Deg2Rad;
                float cosine = Mathf.Cos(angle)*pose.Size, sine = Mathf.Sin(angle)*pose.Size;
                MeshPoint[] points = sources[i].Positions;
                for (int j = 0; j < points.Length; j++)
                {
                    MeshPoint point = points[j];
                    Vector3 vertex = new Vector3(pose.X+point.X*cosine-point.Y*sine,pose.Y+point.X*sine+point.Y*cosine,pose.Z);
                    vertices[offset+j] = vertex;
                    minimum = Vector3.Min(minimum,vertex);
                    maximum = Vector3.Max(maximum,vertex);
                }
                offset += points.Length;
            }
            mesh.vertices = vertices;
            mesh.bounds = new Bounds((minimum+maximum)*0.5f,maximum-minimum);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (root != null) UnityEngine.Object.Destroy(root);
            if (mesh != null) UnityEngine.Object.Destroy(mesh);
            if (material != null) UnityEngine.Object.Destroy(material);
            root = null;
            mesh = null;
            material = null;
        }
    }
}
