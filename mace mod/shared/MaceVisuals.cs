using System;
using System.IO;
using System.Reflection;
using TotemMod;
using UnityEngine;
using UnityEngine.Rendering;

namespace MaceMod.Shared
{
    // Render-only meshes: never clone rigs, add colliders, or change hand hierarchy.
    internal sealed class MaceVisuals : IDisposable
    {
        private const float ItemHeight = 0.56f;
        private const int SparkLimit = 64;
        private const int SparksPerHit = 16;
        private static readonly Vector3 GripOffset = new Vector3(0f,-0.015f,0.035f);
        private static readonly Quaternion GripRotation = Quaternion.identity;
        private static readonly Vector3 PixelGrip = new Vector3(1.5f,1.5f,0f);
        private static readonly Quaternion Upright = Quaternion.Euler(0f,0f,45f);
        private static readonly Vector3 HeadLocal = Upright*((new Vector3(11f,11f,0f)-PixelGrip)*(ItemHeight/16f));
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        private readonly MaterialPropertyBlock tint = new MaterialPropertyBlock();
        private Texture2D texture, sparkTexture;
        private Material material, sparkMaterial;
        private Mesh mesh, sparkMesh;
        private Vector3[] sparkShape, sparkVertices;
        private readonly Spark[] sparks = new Spark[SparkLimit];
        private GameObject effects;
        private MeshRenderer effectRenderer;
        private readonly AudioSource[] voices = new AudioSource[4];
        private AudioClip hit, smash, burst;
        private int nextSpark, nextVoice;
        private float lastTick;
        private bool loaded, disposed;
        public bool ParticlesEnabled { get; set; }
        public bool SoundEnabled { get; set; }
        private struct Spark { internal Vector3 Position, Velocity; internal float Born, Life, Size; }

        public MaceVisuals() { ParticlesEnabled=true; SoundEnabled=true; }

        public void Load()
        {
            if (disposed) throw new ObjectDisposedException("MaceVisuals");
            if (loaded) return;
            try
            {
                texture=LoadTexture("mace.png");
                sparkTexture=LoadTexture("critical_hit.png");
                material=OpaqueMaterial(texture,"Minecraft mace opaque");
                sparkMaterial=OpaqueMaterial(sparkTexture,"Minecraft critical-hit opaque pixels");
                mesh=BuildMesh(texture,ItemHeight,0.7f,true);
                Mesh source=BuildMesh(sparkTexture,1f,0.01f,false);
                sparkShape=source.vertices;
                Vector2[] sourceUv=source.uv;
                int[] sourceIndices=source.triangles;
                sparkVertices=new Vector3[sparkShape.Length*SparkLimit];
                Vector2[] uv=new Vector2[sparkVertices.Length];
                int[] indices=new int[sourceIndices.Length*SparkLimit];
                for(int i=0;i<SparkLimit;i++)
                {
                    Array.Copy(sourceUv,0,uv,i*sourceUv.Length,sourceUv.Length);
                    for(int j=0;j<sourceIndices.Length;j++) indices[i*sourceIndices.Length+j]=sourceIndices[j]+i*sparkShape.Length;
                }
                UnityEngine.Object.Destroy(source);
                sparkMesh=new Mesh(); sparkMesh.name="Bounded Minecraft critical-hit batch"; sparkMesh.MarkDynamic();
                sparkMesh.vertices=sparkVertices; sparkMesh.uv=uv; sparkMesh.triangles=indices;
                effects=new GameObject("Mace render effects"); UnityEngine.Object.DontDestroyOnLoad(effects);
                effects.AddComponent<MeshFilter>().sharedMesh=sparkMesh;
                effectRenderer=effects.AddComponent<MeshRenderer>();
                SetRenderer(effectRenderer,sparkMaterial); effectRenderer.enabled=false;
                for(int i=0;i<voices.Length;i++)
                {
                    GameObject voice=new GameObject("Mace impact voice "+i); voice.transform.SetParent(effects.transform,false);
                    voices[i]=voice.AddComponent<AudioSource>(); voices[i].playOnAwake=false;
                    voices[i].spatialBlend=1f; voices[i].rolloffMode=AudioRolloffMode.Linear;
                    voices[i].minDistance=1f; voices[i].maxDistance=18f; voices[i].dopplerLevel=0f;
                }
                hit=LoadClip("hit.pcm"); smash=LoadClip("smash.pcm"); burst=LoadClip("burst.pcm");
                loaded=true;
            }
            catch { Release(); throw; }
        }

        public GameObject Create(Transform parent,string name)
        {
            Load();
            GameObject item=new GameObject(name);
            item.transform.SetParent(parent,false);
            Camera camera=GorillaTagger.Instance!=null && GorillaTagger.Instance.mainCamera!=null ? GorillaTagger.Instance.mainCamera.GetComponent<Camera>() : null;
            if(camera==null) camera=Camera.main;
            item.layer=MacePresentation.Layer(camera!=null ? camera.cullingMask : -1);
            item.AddComponent<MeshFilter>().sharedMesh=mesh;
            SetRenderer(item.AddComponent<MeshRenderer>(),material);
            return item;
        }

        public void Pose(GameObject item,Transform rightHand,float scale,bool glint,float time)
        {
            if(item==null || rightHand==null || disposed) return;
            scale=SafeScale(scale);
            if(item.transform.parent!=rightHand) item.transform.SetParent(rightHand,false);
            item.transform.localPosition=GripOffset*scale;
            item.transform.localRotation=GripRotation;
            item.transform.localScale=Vector3.one*scale;
            // An opaque property tint preserves every original surface and depth write.
            // No transparent shell or second pass can reveal inner/back surfaces.
            float wave=glint?0.5f+0.5f*Mathf.Sin(time*2.5f):0f;
            Color color=glint?Color.Lerp(Color.white,new Color(0.82f,0.65f,1f),0.22f+wave*0.12f):Color.white;
            tint.Clear(); tint.SetColor(ColorId,color); tint.SetColor(BaseColorId,color);
            tint.SetColor(EmissionId,glint?new Color(0.7f+wave*0.1f,0.65f,0.8f+wave*0.15f):Color.white*0.7f);
            item.GetComponent<MeshRenderer>().SetPropertyBlock(tint);
        }

        public static Vector3 HeadPosition(Transform rightHand,float scale)
        {
            if(rightHand==null) return Vector3.zero;
            return rightHand.TransformPoint((GripOffset+GripRotation*HeadLocal)*SafeScale(scale));
        }

        public void Impact(Vector3 position,bool blocked,float blocks)
        {
            if(!loaded || disposed) return;
            float now=Time.unscaledTime;
            if(SoundEnabled)
            {
                Play(position,blocked?burst:(blocks>=3f?smash:hit),blocked?0.6f:0.8f);
                if(!blocked && blocks>=6f) Play(position,burst,0.35f);
            }
            if(!ParticlesEnabled) return;
            int count=blocked?8:SparksPerHit;
            for(int i=0;i<count;i++)
            {
                float angle=i*Mathf.PI*2f/count;
                Spark spark=new Spark(); spark.Position=position;
                spark.Velocity=new Vector3(Mathf.Cos(angle),0.5f+(i%3)*0.2f,Mathf.Sin(angle))*(blocked?0.65f:1f);
                spark.Born=now; spark.Life=0.25f+(i%4)*0.035f; spark.Size=0.045f+(i%3)*0.008f;
                sparks[nextSpark]=spark; nextSpark=(nextSpark+1)%SparkLimit;
            }
        }

        public void Tick(float time)
        {
            if(!loaded || disposed) return;
            float dt=Mathf.Clamp(time-lastTick,0f,0.05f); lastTick=time;
            Camera camera=Camera.main;
            Quaternion facing=camera==null?Quaternion.identity:camera.transform.rotation;
            bool active=false;
            for(int i=0;i<SparkLimit;i++)
            {
                Spark spark=sparks[i]; float age=time-spark.Born;
                bool alive=ParticlesEnabled && spark.Life>0 && age>=0 && age<spark.Life;
                float size=alive?spark.Size*(1f-age/spark.Life):0f;
                if(alive) { active=true; spark.Velocity+=Vector3.down*(2f*dt); spark.Position+=spark.Velocity*dt; sparks[i]=spark; }
                Vector3 position=alive?spark.Position:Vector3.zero;
                for(int j=0;j<sparkShape.Length;j++) sparkVertices[i*sparkShape.Length+j]=position+facing*(sparkShape[j]*size);
            }
            effectRenderer.enabled=active;
            if(active) { sparkMesh.vertices=sparkVertices; sparkMesh.RecalculateBounds(); }
        }

        public void ClearEffects()
        {
            for(int i=0;i<SparkLimit;i++) sparks[i]=new Spark();
            if(effectRenderer!=null) effectRenderer.enabled=false;
            for(int i=0;i<voices.Length;i++) if(voices[i]!=null) voices[i].Stop();
        }
        private void Play(Vector3 position,AudioClip clip,float volume)
        {
            AudioSource voice=voices[nextVoice]; nextVoice=(nextVoice+1)%voices.Length;
            voice.Stop(); voice.transform.position=position; voice.clip=clip; voice.volume=volume; voice.Play();
        }
        private static float SafeScale(float scale) { return float.IsNaN(scale)||float.IsInfinity(scale)?1f:Mathf.Clamp(scale,0.5f,1.5f); }
        private static void SetRenderer(MeshRenderer renderer,Material value)
        {
            renderer.sharedMaterial=value; renderer.shadowCastingMode=ShadowCastingMode.Off;
            renderer.receiveShadows=false; renderer.lightProbeUsage=LightProbeUsage.Off; renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
        }
        private static Material OpaqueMaterial(Texture2D texture,string name)
        {
            Shader shader=Shader.Find("Standard")??Shader.Find("Universal Render Pipeline/Unlit")??Shader.Find("Unlit/Texture");
            if(shader==null) throw new InvalidOperationException("An opaque mace shader is unavailable.");
            Material result=new Material(shader); result.name=name;
            result.mainTexture=texture;
            if(result.HasProperty("_BaseMap")) result.SetTexture("_BaseMap",texture);
            if(result.HasProperty("_Color")) result.SetColor("_Color",Color.white);
            if(result.HasProperty("_BaseColor")) result.SetColor("_BaseColor",Color.white);
            if(result.HasProperty("_Glossiness")) result.SetFloat("_Glossiness",0f);
            if(result.HasProperty("_Surface")) result.SetFloat("_Surface",0f);
            if(result.HasProperty("_ZWrite")) result.SetFloat("_ZWrite",1f);
            if(result.HasProperty("_EmissionMap")) result.SetTexture("_EmissionMap",texture);
            if(result.HasProperty("_EmissionColor")) { result.EnableKeyword("_EMISSION"); result.SetColor("_EmissionColor",Color.white*0.7f); }
            result.renderQueue=2000;
            return result;
        }
        private static Mesh BuildMesh(Texture2D texture,float height,float depth,bool straighten)
        {
            Color32[] colors=texture.GetPixels32(); byte[] alpha=new byte[colors.Length];
            for(int i=0;i<alpha.Length;i++) alpha[i]=colors[i].a;
            PixelMeshData data=PixelMeshBuilder.Build(alpha,texture.width,texture.height,height,depth);
            Vector3[] points=new Vector3[data.Positions.Length]; Vector2[] uv=new Vector2[points.Length];
            Vector3 grip=(PixelGrip-new Vector3(8f,8f,0f))*(height/16f);
            Quaternion upright=Upright;
            for(int i=0;i<points.Length;i++)
            {
                MeshPoint p=data.Positions[i]; Vector3 point=new Vector3(p.X,p.Y,p.Z);
                points[i]=straighten?upright*(point-grip):point;
                uv[i]=new Vector2(data.Uvs[i].U,data.Uvs[i].V);
            }
            Mesh mesh=new Mesh(); mesh.name=straighten?"Minecraft solid extruded mace":"Minecraft critical-hit silhouette";
            mesh.vertices=points; mesh.uv=uv; mesh.triangles=data.Triangles; mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        private static byte[] Read(string suffix)
        {
            using(Stream stream=typeof(MaceVisuals).Assembly.GetManifestResourceStream("MaceMod."+suffix))
            {
                if(stream==null) throw new InvalidOperationException("Missing embedded Minecraft mace resource: "+suffix);
                byte[] bytes=new byte[stream.Length]; int at=0, read;
                while(at<bytes.Length && (read=stream.Read(bytes,at,bytes.Length-at))>0) at+=read;
                if(at!=bytes.Length) throw new EndOfStreamException(suffix);
                return bytes;
            }
        }
        private static Texture2D LoadTexture(string name)
        {
            Texture2D result=new Texture2D(2,2,TextureFormat.RGBA32,false);
            if(!ImageConversion.LoadImage(result,Read(name),false)) { UnityEngine.Object.Destroy(result); throw new InvalidDataException(name); }
            result.name="Minecraft "+name; result.filterMode=FilterMode.Point; result.wrapMode=TextureWrapMode.Clamp; return result;
        }
        private static AudioClip LoadClip(string name)
        {
            byte[] bytes=Read(name); float[] samples=new float[bytes.Length/4]; Buffer.BlockCopy(bytes,0,samples,0,bytes.Length);
            AudioClip clip=AudioClip.Create("Minecraft "+name,samples.Length,1,44100,false); clip.SetData(samples,0); return clip;
        }
        public void Dispose()
        {
            if(disposed) return; disposed=true; Release();
        }
        private void Release()
        {
            loaded=false;
            UnityEngine.Object.Destroy(effects); UnityEngine.Object.Destroy(mesh); UnityEngine.Object.Destroy(sparkMesh);
            UnityEngine.Object.Destroy(material); UnityEngine.Object.Destroy(sparkMaterial);
            UnityEngine.Object.Destroy(texture); UnityEngine.Object.Destroy(sparkTexture);
            UnityEngine.Object.Destroy(hit); UnityEngine.Object.Destroy(smash); UnityEngine.Object.Destroy(burst);
        }
    }
}
