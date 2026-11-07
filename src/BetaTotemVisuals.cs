using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TotemMod
{
    // Captures render data only. A VRRig, its hierarchy and its behaviours are
    // never instantiated, enabled, reparented or otherwise changed.
    internal sealed class BetaTotemVisuals : IDisposable
    {
        public const int PlayerModel = 1;
        public const int Crystal = 2;
        public const int Block = 3;
        public const int Star = 4;
        private const int RendererLimit = 6;
        private const int VertexLimit = 100000;
        private const float SnapshotSeconds = 3f;
        private const float ModelSize = 0.22f;
        private readonly Dictionary<int, Snapshot> cache = new Dictionary<int, Snapshot>();
        private readonly Dictionary<int, Snapshot> shapes = new Dictionary<int, Snapshot>();
        private readonly HashSet<GameObject> instances = new HashSet<GameObject>();
        private int unavailableSource;
        private float retryAt;
        private bool disposed;

        public GameObject Create(Transform parent, string name, VRRig source, int style, Color tint, bool usePlayerColor)
        {
            if (disposed || style < PlayerModel || style > Star) return null;
            Snapshot snapshot = style == PlayerModel ? GetAvatar(source) : GetShape(style);
            if (snapshot == null) return null;
            GameObject result = new GameObject(name);
            result.transform.SetParent(parent, false);
            instances.Add(result);
            snapshot.Retain();
            BetaTotemVisualOwner owner = result.AddComponent<BetaTotemVisualOwner>();
            owner.OnRelease = delegate { instances.Remove(result); snapshot.Release(); };
            try
            {
                for (int i = 0; i < snapshot.Parts.Count; i++)
                {
                    Part part = snapshot.Parts[i];
                    GameObject child = new GameObject("Totem render mesh");
                    child.transform.SetParent(result.transform, false);
                    child.layer = parent == null ? 0 : parent.gameObject.layer;
                    child.AddComponent<MeshFilter>().sharedMesh = part.Mesh;
                    MeshRenderer renderer = child.AddComponent<MeshRenderer>();
                    renderer.sharedMaterials = part.Materials;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    if (usePlayerColor && style == PlayerModel)
                        renderer.SetPropertyBlock(part.Properties);
                    else
                    {
                        MaterialPropertyBlock colors = new MaterialPropertyBlock();
                        colors.SetColor("_Color", tint);
                        colors.SetColor("_BaseColor", tint);
                        renderer.SetPropertyBlock(colors);
                    }
                }
                return result;
            }
            catch
            {
                UnityEngine.Object.Destroy(result);
                return null;
            }
        }

        private Snapshot GetAvatar(VRRig source)
        {
            if (source == null) return null;
            int key = source.GetInstanceID();
            if (key == unavailableSource && Time.unscaledTime < retryAt) return null;
            Snapshot existing;
            if (cache.TryGetValue(key, out existing))
            {
                if (existing.Source == source && Time.unscaledTime - existing.CreatedAt < SnapshotSeconds) return existing;
                cache.Remove(key);
                existing.Release();
            }
            // Only one selected avatar is retained by the cache. Live effects
            // retain their own reference until their visual is destroyed.
            ClearCache();
            Snapshot captured = Capture(source);
            if (captured != null) cache.Add(key, captured);
            else { unavailableSource = key; retryAt = Time.unscaledTime + SnapshotSeconds; }
            return captured;
        }

        private static Snapshot Capture(VRRig source)
        {
            Snapshot snapshot = new Snapshot();
            snapshot.Source = source;
            snapshot.CreatedAt = Time.unscaledTime;
            try
            {
                int vertices = 0;
                // The local first-person rig can hide its body and face. Read
                // those renderers directly without changing their active state,
                // and reserve the renderer budget for them before cosmetics.
                SkinnedMeshRenderer body = source.mainSkin;
                if (body != null)
                    CapturePart(snapshot, source.transform, body, body.sharedMesh, ref vertices, true);
                MeshRenderer face = source.faceSkin;
                if (face != null)
                {
                    MeshFilter faceFilter = face.GetComponent<MeshFilter>();
                    if (faceFilter != null)
                        CapturePart(snapshot, source.transform, face, faceFilter.sharedMesh, ref vertices, true);
                }
                SkinnedMeshRenderer[] skins = source.GetComponentsInChildren<SkinnedMeshRenderer>(false);
                for (int i = 0; i < skins.Length && snapshot.Parts.Count < RendererLimit; i++)
                    if (skins[i] != body)
                        CapturePart(snapshot, source.transform, skins[i], skins[i].sharedMesh, ref vertices);
                MeshRenderer[] solids = source.GetComponentsInChildren<MeshRenderer>(false);
                for (int i = 0; i < solids.Length && snapshot.Parts.Count < RendererLimit; i++)
                {
                    if (solids[i] == face) continue;
                    MeshFilter filter = solids[i].GetComponent<MeshFilter>();
                    if (filter != null) CapturePart(snapshot, source.transform, solids[i], filter.sharedMesh, ref vertices);
                }
                if (snapshot.Parts.Count == 0) { snapshot.Release(); return null; }
                Bounds bounds = snapshot.Parts[0].Mesh.bounds;
                for (int i = 1; i < snapshot.Parts.Count; i++) bounds.Encapsulate(snapshot.Parts[i].Mesh.bounds);
                float extent = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                if (float.IsNaN(extent) || float.IsInfinity(extent) || extent < 0.001f)
                { snapshot.Release(); return null; }
                for (int i = 0; i < snapshot.Parts.Count; i++)
                {
                    Mesh mesh = snapshot.Parts[i].Mesh;
                    Vector3[] points = mesh.vertices;
                    for (int j = 0; j < points.Length; j++) points[j] = (points[j] - bounds.center) * (ModelSize / extent);
                    mesh.vertices = points;
                    mesh.RecalculateBounds();
                }
                return snapshot;
            }
            catch { snapshot.Release(); return null; }
        }

        private static void CapturePart(Snapshot snapshot, Transform root, Renderer renderer, Mesh source, ref int vertexCount, bool includeHidden = false)
        {
            if (renderer.GetComponentInParent<TotemVisualMarker>()!=null || renderer.GetComponentInParent<BetaTotemVisualOwner>()!=null) return;
            if (source == null || (!includeHidden && (!renderer.enabled || !renderer.gameObject.activeInHierarchy)) ||
                source.vertexCount == 0 || source.vertexCount > VertexLimit - vertexCount) return;
            Part part = new Part();
            try
            {
                SkinnedMeshRenderer skin = renderer as SkinnedMeshRenderer;
                if (skin != null)
                {
                    part.Mesh = new Mesh();
                    part.Mesh.indexFormat = source.indexFormat;
                    skin.BakeMesh(part.Mesh);
                }
                else part.Mesh = UnityEngine.Object.Instantiate(source);
                part.Mesh.name = "Beta miniature avatar mesh";
                Matrix4x4 matrix = root.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                Vector3[] points = part.Mesh.vertices;
                for (int i = 0; i < points.Length; i++)
                {
                    points[i] = matrix.MultiplyPoint3x4(points[i]);
                    if (!Finite(points[i])) throw new InvalidOperationException("Invalid avatar mesh bounds.");
                }
                part.Mesh.vertices = points;
                Vector3[] normals = part.Mesh.normals;
                Matrix4x4 normalMatrix = matrix.inverse.transpose;
                for (int i = 0; i < normals.Length; i++) normals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
                part.Mesh.normals = normals;
                Vector4[] tangents = part.Mesh.tangents;
                for (int i = 0; i < tangents.Length; i++)
                {
                    Vector3 direction = matrix.MultiplyVector(new Vector3(tangents[i].x, tangents[i].y, tangents[i].z)).normalized;
                    tangents[i] = new Vector4(direction.x, direction.y, direction.z, tangents[i].w);
                }
                part.Mesh.tangents = tangents;
                part.Mesh.RecalculateBounds();
                Material[] originals = renderer.sharedMaterials;
                if (originals.Length > 16) throw new InvalidOperationException("Too many avatar materials.");
                part.Materials = new Material[originals.Length];
                for (int i = 0; i < originals.Length; i++)
                    if (originals[i] != null) part.Materials[i] = new Material(originals[i]);
                part.Properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(part.Properties);
                snapshot.Parts.Add(part);
                vertexCount += points.Length;
            }
            // Some cosmetics deliberately have unreadable meshes. Keep the
            // successful body capture and omit only that unsupported renderer.
            catch { part.Dispose(); }
        }

        private static bool Finite(Vector3 point)
        {
            return !float.IsNaN(point.x) && !float.IsInfinity(point.x) &&
                !float.IsNaN(point.y) && !float.IsInfinity(point.y) &&
                !float.IsNaN(point.z) && !float.IsInfinity(point.z);
        }

        private Snapshot GetShape(int style)
        {
            Snapshot result;
            if (shapes.TryGetValue(style, out result)) return result;
            Shader shader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Standard");
            if (shader == null) return null;
            result = new Snapshot();
            Part part = new Part();
            result.Parts.Add(part);
            part.Mesh = BuildShape(style);
            part.Materials = new[] { new Material(shader) };
            part.Materials[0].name = "Beta totem shape color";
            part.Materials[0].color = Color.white;
            shapes.Add(style, result);
            return result;
        }

        private static Mesh BuildShape(int style)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            if (style == Crystal)
            {
                Vector3 top = new Vector3(0, 0.11f, 0), bottom = new Vector3(0, -0.11f, 0);
                Vector3[] ring = { new Vector3(-0.065f,0,0), new Vector3(0,0,0.065f), new Vector3(0.065f,0,0), new Vector3(0,0,-0.065f) };
                for (int i = 0; i < 4; i++)
                {
                    Triangle(vertices, triangles, top, ring[i], ring[(i+1)%4]);
                    Triangle(vertices, triangles, bottom, ring[(i+1)%4], ring[i]);
                }
            }
            else
            {
                int count = style == Star ? 10 : 4;
                Vector3[] outline = new Vector3[count];
                for (int i = 0; i < count; i++)
                {
                    float angle = (style == Star ? Mathf.PI * 0.5f : Mathf.PI * 0.25f) + i * Mathf.PI * 2 / count;
                    float radius = style == Star ? (i % 2 == 0 ? 0.11f : 0.048f) : 0.11f;
                    outline[i] = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0);
                }
                float depth = style == Star ? 0.025f : 0.075f;
                Vector3 offset = new Vector3(0, 0, depth);
                for (int i = 0; i < count; i++)
                {
                    Vector3 a = outline[i], b = outline[(i+1)%count];
                    Triangle(vertices, triangles, offset, a+offset, b+offset);
                    Triangle(vertices, triangles, -offset, b-offset, a-offset);
                    Triangle(vertices, triangles, a-offset, b-offset, b+offset);
                    Triangle(vertices, triangles, a-offset, b+offset, a+offset);
                }
            }
            Mesh result = new Mesh();
            result.name = "Beta totem shape " + style;
            result.SetVertices(vertices);
            result.SetTriangles(triangles, 0);
            result.RecalculateNormals();
            result.RecalculateBounds();
            return result;
        }

        private static void Triangle(List<Vector3> vertices, List<int> indices, Vector3 a, Vector3 b, Vector3 c)
        {
            int start = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            indices.Add(start); indices.Add(start+1); indices.Add(start+2);
        }

        public void ClearCache()
        {
            foreach (Snapshot item in cache.Values) item.Release();
            cache.Clear();
            unavailableSource = 0;
            retryAt = 0;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (GameObject item in new List<GameObject>(instances))
                if (item != null) UnityEngine.Object.Destroy(item);
            instances.Clear();
            ClearCache();
            foreach (Snapshot item in shapes.Values) item.Release();
            shapes.Clear();
        }

        private sealed class Part : IDisposable
        {
            public Mesh Mesh;
            public Material[] Materials;
            public MaterialPropertyBlock Properties;
            public void Dispose()
            {
                if (Mesh != null) UnityEngine.Object.Destroy(Mesh);
                if (Materials != null)
                    foreach (Material material in Materials)
                        if (material != null) UnityEngine.Object.Destroy(material);
            }
        }

        private sealed class Snapshot
        {
            public readonly List<Part> Parts = new List<Part>();
            public VRRig Source;
            public float CreatedAt;
            private int references = 1;
            public void Retain() { references++; }
            public void Release()
            {
                if (--references != 0) return;
                foreach (Part part in Parts) part.Dispose();
                Parts.Clear();
                Source = null;
            }
        }
    }

    internal sealed class BetaTotemVisualOwner : MonoBehaviour
    {
        internal Action OnRelease;
        private void OnDestroy()
        {
            Action release = OnRelease;
            OnRelease = null;
            if (release != null) release();
        }
    }
}
