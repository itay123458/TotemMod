using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TotemMod
{
    internal sealed class TotemParticlePool
    {
        private const int Limit = 128;
        private readonly Stack<GameObject> idle = new Stack<GameObject>(Limit);
        private readonly HashSet<GameObject> active = new HashSet<GameObject>();
        private readonly HashSet<GameObject> all = new HashSet<GameObject>();
        private readonly MaterialPropertyBlock tint = new MaterialPropertyBlock();
        private GameObject root;
        private Mesh sparkle;
        private Mesh fleck;

        public Transform Rent(Transform parent, Material material, Color color, int layer, bool star)
        {
            if (root == null)
            {
                root = new GameObject("Totem particle pool");
                Object.DontDestroyOnLoad(root);
            }
            if (sparkle == null) sparkle = BuildPixels(new[] { "...##...", "...##...", "..####..", "########", "########", "..####..", "...##...", "...##..." }, "Totem pixel sparkle");
            if (fleck == null) fleck = BuildPixels(new[] { "##", "##" }, "Totem pixel fleck");
            GameObject item = null;
            while (idle.Count > 0 && item == null) item = idle.Pop();
            if (item == null)
            {
                // Effect parents can disappear during a scene unload. Unity's
                // destroyed references must not permanently exhaust the pool.
                all.RemoveWhere(IsDestroyed);
                active.RemoveWhere(IsDestroyed);
                if (all.Count >= Limit) return null;
                item = new GameObject("Totem spark");
                item.SetActive(false);
                item.AddComponent<MeshFilter>();
                MeshRenderer renderer = item.AddComponent<MeshRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                all.Add(item);
            }
            item.transform.SetParent(parent,false);
            item.transform.localPosition = Vector3.zero;
            item.transform.localRotation = Quaternion.identity;
            item.transform.localScale = Vector3.zero;
            item.layer = layer;
            item.GetComponent<MeshFilter>().sharedMesh = star ? sparkle : fleck;
            Renderer render = item.GetComponent<Renderer>();
            render.sharedMaterial = material;
            tint.Clear();
            tint.SetColor("_Color",color);
            tint.SetColor("_EmissionColor",color);
            render.SetPropertyBlock(tint);
            item.SetActive(true);
            active.Add(item);
            return item.transform;
        }

        private static bool IsDestroyed(GameObject item) { return item == null; }

        // Merge solid runs into double-sided quads once for the entire pool.
        private static Mesh BuildPixels(string[] rows, string name)
        {
            PixelMeshData data = PixelMeshBuilder.BuildBillboard(rows);
            Vector3[] vertices = new Vector3[data.Positions.Length];
            for (int i=0; i<vertices.Length; i++)
                vertices[i] = new Vector3(data.Positions[i].X,data.Positions[i].Y,data.Positions[i].Z);
            Mesh result = new Mesh();
            result.name = name;
            result.vertices = vertices;
            result.triangles = data.Triangles;
            Color32[] colors = new Color32[vertices.Length];
            for (int i=0; i<colors.Length; i++) colors[i] = new Color32(255,255,255,255);
            result.colors32 = colors;
            result.RecalculateNormals();
            result.RecalculateBounds();
            return result;
        }

        public void Return(Transform particle)
        {
            if (particle == null || !active.Remove(particle.gameObject)) return;
            if (root == null)
            {
                all.Remove(particle.gameObject);
                Object.Destroy(particle.gameObject);
                return;
            }
            particle.gameObject.SetActive(false);
            particle.SetParent(root.transform,false);
            idle.Push(particle.gameObject);
        }

        public void ReturnAll()
        {
            foreach (GameObject item in active)
            {
                if (item == null) continue;
                item.SetActive(false);
                if (root != null) item.transform.SetParent(root.transform,false);
                idle.Push(item);
            }
            active.Clear();
        }

        public void Dispose()
        {
            ReturnAll();
            foreach (GameObject item in all) if (item != null) Object.Destroy(item);
            Object.Destroy(root);
            Object.Destroy(sparkle); Object.Destroy(fleck);
            root = null; sparkle = null; fleck = null; idle.Clear(); all.Clear();
        }
    }
}
