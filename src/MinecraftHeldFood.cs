using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace TotemMod
{
    internal sealed class MinecraftHeldFood : IDisposable
    {
        private readonly SurvivalAssets assets;
        private readonly Mesh[] meshes=new Mesh[4];
        private readonly Material[] materials=new Material[4];
        private readonly PalmAnchor rightPalm=new PalmAnchor(false);
        private GameObject root;
        private MeshFilter filter;
        private MeshRenderer renderer;
        private CraftItem shown;
        private bool disposed;
        private static readonly Quaternion Rotation=Quaternion.Euler(90,90,0);

        internal MinecraftHeldFood(SurvivalAssets assets) { this.assets=assets; }

        internal void Pose(VRRig rig,Transform fallback,Camera camera,CraftItem item,float playerScale,float eating,double now)
        {
            if(disposed) return;
            Transform hand=rightPalm.Resolve(rig!=null ? rig.transform : null,fallback);
            if(item==CraftItem.None || hand==null || camera==null) { Hide(); return; }
            if(root==null)
            {
                root=new GameObject("PrimalCraft main-hand food"); UnityEngine.Object.DontDestroyOnLoad(root);
                filter=root.AddComponent<MeshFilter>(); renderer=root.AddComponent<MeshRenderer>();
                renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
            }
            if(shown!=item || filter.sharedMesh==null)
            {
                Build(item); shown=item;
                filter.sharedMesh=meshes[(int)item]; renderer.sharedMaterial=materials[(int)item];
            }
            int layer=(int)UnityLayer.FirstPersonOnly;
            if((camera.cullingMask & (1<<layer))==0)
                for(int i=0;i<32;i++) if((camera.cullingMask & (1<<i))!=0) { layer=i; break; }
            root.layer=layer;
            float scale=float.IsNaN(playerScale)||float.IsInfinity(playerScale) ? 1 : Mathf.Clamp(playerScale,0.2f,4);
            float bite=eating>0 ? Mathf.Sin((float)now*16)*7 : 0;
            root.transform.position=hand.position+hand.rotation*(new Vector3(0,0.015f,0.015f)*scale);
            root.transform.rotation=hand.rotation*Rotation*Quaternion.Euler(0,0,bite);
            root.transform.localScale=Vector3.one*scale;
            if(!root.activeSelf) root.SetActive(true);
        }

        private void Build(CraftItem item)
        {
            int index=(int)item;
            if(index<1 || index>=meshes.Length) throw new ArgumentOutOfRangeException("item");
            if(meshes[index]!=null) return;
            Texture2D texture=assets.ItemTexture(item);
            if(texture==null) throw new InvalidOperationException("The selected food texture is missing.");
            Color32[] pixels=texture.GetPixels32(); byte[] alpha=new byte[pixels.Length];
            for(int i=0;i<pixels.Length;i++) alpha[i]=pixels[i].a;
            PixelMeshData data=PixelMeshBuilder.Build(alpha,texture.width,texture.height,0.18f,0.7f);
            Vector3[] vertices=new Vector3[data.Positions.Length]; Vector2[] uv=new Vector2[vertices.Length];
            for(int i=0;i<vertices.Length;i++)
            { MeshPoint point=data.Positions[i]; vertices[i]=new Vector3(point.X,point.Y,point.Z); uv[i]=new Vector2(data.Uvs[i].U,data.Uvs[i].V); }
            Mesh mesh=new Mesh(); mesh.name="Minecraft "+item+" solid pixels";
            mesh.vertices=vertices; mesh.uv=uv; mesh.triangles=data.Triangles; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            Shader shader=Shader.Find("Unlit/Texture") ?? Shader.Find("Universal Render Pipeline/Unlit");
            if(shader==null) { UnityEngine.Object.Destroy(mesh); throw new InvalidOperationException("No compatible food texture shader is available."); }
            Material material=new Material(shader); material.name="Minecraft "+item+" opaque"; material.mainTexture=texture;
            if(material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap",texture);
            material.renderQueue=2000; meshes[index]=mesh; materials[index]=material;
        }

        internal void Hide() { if(root!=null && root.activeSelf) root.SetActive(false); }
        public void Dispose()
        {
            if(disposed) return; disposed=true;
            UnityEngine.Object.Destroy(root);
            for(int i=0;i<meshes.Length;i++) { UnityEngine.Object.Destroy(meshes[i]); UnityEngine.Object.Destroy(materials[i]); }
        }
    }
}
