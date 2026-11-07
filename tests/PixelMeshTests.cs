using System;
using System.Collections.Generic;
using System.Drawing;
using TotemMod;

internal static class PixelMeshTests
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static MeshPoint Sub(MeshPoint a, MeshPoint b) { return new MeshPoint(a.X-b.X,a.Y-b.Y,a.Z-b.Z); }
    private static MeshPoint Cross(MeshPoint a, MeshPoint b) { return new MeshPoint(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X); }
    private static float Dot(MeshPoint a, MeshPoint b) { return a.X*b.X+a.Y*b.Y+a.Z*b.Z; }
    private static MeshPoint At(MeshPoint a, MeshPoint b, float t) { return new MeshPoint(a.X+b.X*t,a.Y+b.Y*t,a.Z+b.Z*t); }
    private static bool Inside(byte[] alpha, int width, int height, MeshPoint p)
    {
        int x=(int)Math.Floor(p.X+width*0.5f), y=(int)Math.Floor(p.Y+height*0.5f);
        return x>=0 && x<width && y>=0 && y<height && Math.Abs(p.Z)<0.7f && alpha[y*width+x]>=128;
    }

    private static float Intersect(PixelMeshData mesh, MeshPoint origin, MeshPoint direction, bool cull, out MeshUv uv)
    {
        float nearest=float.PositiveInfinity;
        uv=new MeshUv();
        for(int i=0;i<mesh.Triangles.Length;i+=3)
        {
            int ia=mesh.Triangles[i], ib=mesh.Triangles[i+1], ic=mesh.Triangles[i+2];
            MeshPoint a=mesh.Positions[ia], e1=Sub(mesh.Positions[ib],a), e2=Sub(mesh.Positions[ic],a);
            MeshPoint p=Cross(direction,e2);
            float determinant=Dot(e1,p);
            if(cull ? determinant<0.000001f : Math.Abs(determinant)<0.000001f) continue;
            float inverse=1/determinant;
            MeshPoint t=Sub(origin,a);
            float u=Dot(t,p)*inverse;
            if(u<-0.00001f || u>1.00001f) continue;
            MeshPoint q=Cross(t,e1);
            float v=Dot(direction,q)*inverse;
            if(v<-0.00001f || u+v>1.00001f) continue;
            float distance=Dot(e2,q)*inverse;
            if(distance<=0 || distance>=nearest) continue;
            nearest=distance;
            uv=new MeshUv(mesh.Uvs[ia].U*(1-u-v)+mesh.Uvs[ib].U*u+mesh.Uvs[ic].U*v,
                mesh.Uvs[ia].V*(1-u-v)+mesh.Uvs[ib].V*u+mesh.Uvs[ic].V*v);
        }
        return nearest;
    }

    // Independent oracle: intersect each occupied pixel's solid axis-aligned box.
    private static float SolidDistance(byte[] alpha, int width, int height, MeshPoint origin, MeshPoint direction)
    {
        float result=float.PositiveInfinity;
        for(int y=0;y<height;y++) for(int x=0;x<width;x++)
        {
            if(alpha[y*width+x]<128) continue;
            float near=0, far=float.PositiveInfinity;
            float[] min={x-width*0.5f,y-height*0.5f,-0.7f};
            float[] max={min[0]+1,min[1]+1,0.7f};
            float[] o={origin.X,origin.Y,origin.Z}, d={direction.X,direction.Y,direction.Z};
            for(int axis=0;axis<3;axis++)
            {
                if(Math.Abs(d[axis])<0.0000001f) { if(o[axis]<min[axis] || o[axis]>max[axis]) far=-1; }
                else
                {
                    float a=(min[axis]-o[axis])/d[axis], b=(max[axis]-o[axis])/d[axis];
                    near=Math.Max(near,Math.Min(a,b)); far=Math.Min(far,Math.Max(a,b));
                }
            }
            if(near<=far && far>0) result=Math.Min(result,near);
        }
        return result;
    }

    private static void SurfaceChecks(byte[] alpha, int width, int height, PixelMeshData mesh)
    {
        for(int i=0;i<mesh.Triangles.Length;i+=3)
        {
            MeshPoint a=mesh.Positions[mesh.Triangles[i]], b=mesh.Positions[mesh.Triangles[i+1]], c=mesh.Positions[mesh.Triangles[i+2]];
            MeshPoint n=Cross(Sub(b,a),Sub(c,a));
            float length=(float)Math.Sqrt(Dot(n,n));
            Check(length>0,"No degenerate surface triangles.");
            MeshPoint midpoint=new MeshPoint((a.X+b.X+c.X)/3,(a.Y+b.Y+c.Y)/3,(a.Z+b.Z+c.Z)/3);
            Check(Inside(alpha,width,height,At(midpoint,n,-0.001f/length)),"Triangle must face out of solid pixels.");
            Check(!Inside(alpha,width,height,At(midpoint,n,0.001f/length)),"No hidden internal faces or inward normals.");
        }
        for(int azimuth=0;azimuth<24;azimuth++) for(int elevation=-2;elevation<=2;elevation++)
        {
            double angle=azimuth*Math.PI/12;
            MeshPoint origin=new MeshPoint((float)Math.Cos(angle)*40,elevation*12.7f,(float)Math.Sin(angle)*40);
            // Jittered pixel centers cover all exterior directions, including edge-on views.
            for(int y=0;y<height;y++) for(int x=0;x<width;x++)
            {
                MeshPoint target=new MeshPoint(x-width*0.5f+0.417f,y-height*0.5f+0.583f,0.113f);
                MeshPoint direction=Sub(target,origin);
                float expected=SolidDistance(alpha,width,height,origin,direction);
                MeshUv ignored;
                float actual=Intersect(mesh,origin,direction,true,out ignored);
                Check(float.IsPositiveInfinity(expected)==float.IsPositiveInfinity(actual),"Backface culling exposes a hole or fills a transparent pixel.");
                if(!float.IsPositiveInfinity(expected)) Check(Math.Abs(actual-expected)<0.00002f,"Nearest culled surface must match the closed solid, not its far interior wall.");
            }
        }
    }

    private static string PointKey(MeshPoint p) { return Math.Round(p.X*10000)+","+Math.Round(p.Y*10000)+","+Math.Round(p.Z*10000); }
    private static void ClosedEdges(PixelMeshData mesh)
    {
        Dictionary<string,MeshPoint> points=new Dictionary<string,MeshPoint>();
        foreach(MeshPoint p in mesh.Positions) points[PointKey(p)]=p;
        Dictionary<string,int> counts=new Dictionary<string,int>();
        Dictionary<string,int> winding=new Dictionary<string,int>();
        for(int i=0;i<mesh.Triangles.Length;i+=3) for(int edge=0;edge<3;edge++)
        {
            MeshPoint a=mesh.Positions[mesh.Triangles[i+edge]], b=mesh.Positions[mesh.Triangles[i+(edge+1)%3]], d=Sub(b,a);
            float length=Dot(d,d);
            List<KeyValuePair<float,MeshPoint>> splits=new List<KeyValuePair<float,MeshPoint>>();
            foreach(MeshPoint p in points.Values)
            {
                MeshPoint delta=Sub(p,a), cross=Cross(delta,d);
                float t=Dot(delta,d)/length;
                if(t>=-0.00001f && t<=1.00001f && Dot(cross,cross)<0.0000001f) splits.Add(new KeyValuePair<float,MeshPoint>(t,p));
            }
            splits.Sort(delegate(KeyValuePair<float,MeshPoint> x,KeyValuePair<float,MeshPoint> y) { return x.Key.CompareTo(y.Key); });
            for(int j=1;j<splits.Count;j++)
            {
                string left=PointKey(splits[j-1].Value),right=PointKey(splits[j].Value);
                if(left==right) continue;
                bool forward=string.CompareOrdinal(left,right)<0;
                string key=forward?left+"|"+right:right+"|"+left;
                if(!counts.ContainsKey(key)) { counts[key]=0; winding[key]=0; }
                counts[key]++; winding[key]+=forward?1:-1;
            }
        }
        foreach(string key in counts.Keys) Check(counts[key]==2 && winding[key]==0,"Every geometric surface edge must have two opposite uses: "+key);
    }

    private static void PixelColors(byte[] alpha, int[] colors, int width, int height, PixelMeshData mesh)
    {
        for(int side=-1;side<=1;side+=2) for(int y=0;y<height;y++) for(int x=0;x<width;x++)
        for(int sample=0;sample<3;sample++)
        {
            MeshUv uv;
            MeshPoint origin=new MeshPoint(x-width*0.5f+0.13f+sample*0.31f,y-height*0.5f+0.23f+sample*0.27f,side*10);
            float hit=Intersect(mesh,origin,new MeshPoint(0,0,-side),true,out uv);
            Check(float.IsPositiveInfinity(hit)==(alpha[y*width+x]<128),"Both caps preserve exact opaque pixel coverage.");
            if(float.IsPositiveInfinity(hit)) continue;
            int sx=Math.Min(width-1,Math.Max(0,(int)(uv.U*width))), sy=Math.Min(height-1,Math.Max(0,(int)(uv.V*height)));
            Check(colors[sy*width+sx]==colors[y*width+x],"Point-sampled texture color must match every original pixel from either side.");
        }
    }

    private static void BillboardChecks(string[] rows, int triangleBudget)
    {
        PixelMeshData mesh=PixelMeshBuilder.BuildBillboard(rows);
        int height=rows.Length,width=rows[0].Length;
        for(int side=-1;side<=1;side+=2)
        for(int y=0;y<height;y++) for(int x=0;x<width;x++)
        for(int sample=0;sample<3;sample++)
        {
            MeshPoint target=new MeshPoint((x+0.13f+sample*0.31f)/width-0.5f,0.5f-(y+0.23f+sample*0.27f)/height,side*2);
            MeshUv ignored;
            float hit=Intersect(mesh,target,new MeshPoint(0,0,-side),true,out ignored);
            Check(float.IsPositiveInfinity(hit)==(rows[y][x]!='#'),"Particle billboard keeps every lit and transparent pixel from either side.");
            if(!float.IsPositiveInfinity(hit)) Check(Math.Abs(hit-2)<0.00001f,"Particle quads stay on their original plane.");
        }
        Console.WriteLine("Billboard "+width+"x"+height+": "+mesh.Positions.Length+" vertices, "+mesh.Triangles.Length/3+" triangles.");
        Check(mesh.Triangles.Length/3<=triangleBudget,"Particle billboards merge adjacent lit pixels to reduce geometry.");
    }

    public static int Main(string[] args)
    {
        try
        {
            byte[] alpha; int[] colors; int width,height;
            using(Bitmap image=new Bitmap(args[0]))
            {
                width=image.Width; height=image.Height; alpha=new byte[width*height]; colors=new int[alpha.Length];
                for(int y=0;y<height;y++) for(int x=0;x<width;x++)
                {
                    Color color=image.GetPixel(x,height-1-y); alpha[y*width+x]=color.A; colors[y*width+x]=color.ToArgb();
                }
            }
            PixelMeshData mesh=PixelMeshBuilder.Build(alpha,width,height,height,0.7f);
            SurfaceChecks(alpha,width,height,mesh);
            ClosedEdges(mesh);
            PixelColors(alpha,colors,width,height,mesh);
            // Exercise the actual 30 cm float coordinates too, then normalize
            // only positions into pixel units for the independent solid oracle.
            PixelMeshData runtimeMesh=PixelMeshBuilder.Build(alpha,width,height,0.30f,0.7f);
            float runtimeStep=0.30f/height;
            for(int i=0;i<runtimeMesh.Positions.Length;i++)
            {
                MeshPoint p=runtimeMesh.Positions[i];
                runtimeMesh.Positions[i]=new MeshPoint(p.X/runtimeStep,p.Y/runtimeStep,p.Z/runtimeStep);
            }
            SurfaceChecks(alpha,width,height,runtimeMesh);
            ClosedEdges(runtimeMesh);
            PixelColors(alpha,colors,width,height,runtimeMesh);
            Console.WriteLine("Asset surface/culling/color checks passed: "+mesh.Positions.Length+" vertices, "+mesh.Triangles.Length/3+" triangles.");
            Check(mesh.Triangles.Length/3<250,"The shared totem mesh must merge opaque caps instead of drawing two quads per pixel.");
            byte[] hole={255,255,255,255,0,255,255,255,255};
            PixelMeshData ring=PixelMeshBuilder.Build(hole,3,3,3,0.7f);
            SurfaceChecks(hole,3,3,ring); ClosedEdges(ring);
            byte[] threshold={127,128};
            PixelMeshData cut=PixelMeshBuilder.Build(threshold,2,1,1,0.7f);
            SurfaceChecks(threshold,2,1,cut); ClosedEdges(cut);
            Check(PixelMeshBuilder.Build(new byte[4],2,2,2,0.7f).Triangles.Length==0,"Fully transparent texture has no surfaces.");
            BillboardChecks(new[] { "...##...", "...##...", "..####..", "########", "########", "..####..", "...##...", "...##..." },24);
            BillboardChecks(new[] { "##", "##" },4);
            Console.WriteLine("PASS "+checks+" closed extrusion, outward winding, camera culling, pixel-color and geometry-budget checks");
            return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}
