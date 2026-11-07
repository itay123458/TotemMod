using System;
using System.Collections.Generic;

namespace TotemMod
{
    internal struct MeshPoint
    {
        public float X, Y, Z;
        public MeshPoint(float x, float y, float z) { X=x; Y=y; Z=z; }
    }
    internal struct MeshUv
    {
        public float U, V;
        public MeshUv(float u, float v) { U=u; V=v; }
    }
    internal sealed class PixelMeshData
    {
        public MeshPoint[] Positions;
        public MeshUv[] Uvs;
        public int[] Triangles;
    }
    internal static class PixelMeshBuilder
    {
        private struct PixelRect
        {
            public int Left, Bottom, Right, Top;
        }

        public static PixelMeshData BuildBillboard(string[] rows)
        {
            List<MeshPoint> vertices = new List<MeshPoint>();
            List<MeshUv> uvs = new List<MeshUv>();
            List<int> triangles = new List<int>();
            int height=rows.Length, width=rows[0].Length;
            byte[] alpha=new byte[width*height];
            for(int y=0;y<height;y++) for(int x=0;x<width;x++) alpha[y*width+x]=rows[y][x]=='#'?(byte)255:(byte)0;
            foreach(PixelRect rect in Rectangles(alpha,width,height))
            {
                float l=(float)rect.Left/width-0.5f,r=(float)rect.Right/width-0.5f;
                float b=0.5f-(float)rect.Top/height,t=0.5f-(float)rect.Bottom/height;
                int n=vertices.Count;
                AddFace(vertices,uvs,triangles,new MeshUv(),new MeshPoint(l,b,0),new MeshPoint(l,t,0),new MeshPoint(r,t,0),new MeshPoint(r,b,0));
                triangles.Add(n+2); triangles.Add(n+1); triangles.Add(n);
                triangles.Add(n+3); triangles.Add(n+2); triangles.Add(n);
            }
            return new PixelMeshData { Positions=vertices.ToArray(), Uvs=uvs.ToArray(), Triangles=triangles.ToArray() };
        }

        private static List<PixelRect> Rectangles(byte[] alpha, int width, int height)
        {
            List<PixelRect> result=new List<PixelRect>();
            bool[] covered=new bool[alpha.Length];
            for(int y=0;y<height;y++) for(int x=0;x<width;x++)
            {
                if(alpha[y*width+x]<128 || covered[y*width+x]) continue;
                int right=x+1,top=y+1;
                while(right<width && alpha[y*width+right]>=128 && !covered[y*width+right]) right++;
                while(top<height)
                {
                    bool full=true;
                    for(int column=x;column<right;column++)
                        if(alpha[top*width+column]<128 || covered[top*width+column]) { full=false; break; }
                    if(!full) break;
                    top++;
                }
                for(int row=y;row<top;row++) for(int column=x;column<right;column++) covered[row*width+column]=true;
                result.Add(new PixelRect { Left=x,Bottom=y,Right=right,Top=top });
            }
            return result;
        }

        public static PixelMeshData Build(byte[] alpha, int width, int height, float itemHeight, float halfDepthPixels)
        {
            List<MeshPoint> vertices = new List<MeshPoint>();
            List<MeshUv> uvs = new List<MeshUv>();
            List<int> triangles = new List<int>();
            float step = itemHeight / height;
            float depth = step * halfDepthPixels;
            // Caps can span differently colored pixels: their UVs still map
            // every point onto the original point-filtered texture. Only the
            // silhouette limits merging; side faces retain their pixel color.
            foreach (PixelRect rect in Rectangles(alpha,width,height))
            {
                float l = (rect.Left-width*0.5f)*step, r = (rect.Right-width*0.5f)*step;
                float b = (rect.Bottom-height*0.5f)*step, t = (rect.Top-height*0.5f)*step;
                AddFace(vertices,uvs,triangles,new MeshUv(),new MeshPoint(l,b,-depth),new MeshPoint(l,t,-depth),new MeshPoint(r,t,-depth),new MeshPoint(r,b,-depth));
                AddFace(vertices,uvs,triangles,new MeshUv(),new MeshPoint(r,b,depth),new MeshPoint(r,t,depth),new MeshPoint(l,t,depth),new MeshPoint(l,b,depth));
                for (int vertex = vertices.Count-8; vertex < vertices.Count; vertex++)
                    uvs[vertex] = new MeshUv((vertices[vertex].X/step+width*0.5f)/width,(vertices[vertex].Y/step+height*0.5f)/height);
            }
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                if (alpha[y * width + x] < 128) continue;
                // Calculate shared grid coordinates identically on every face,
                // avoiding small addition/rounding gaps at cap boundaries.
                float l = (x - width * 0.5f) * step, r = (x+1-width*0.5f)*step;
                float b = (y - height * 0.5f) * step, t = (y+1-height*0.5f)*step;
                MeshUv uv = new MeshUv((x + 0.5f) / width, (y + 0.5f) / height);
                if (x == 0 || alpha[y*width+x-1] < 128)
                    AddFace(vertices, uvs, triangles, uv, new MeshPoint(l,b,depth), new MeshPoint(l,t,depth), new MeshPoint(l,t,-depth), new MeshPoint(l,b,-depth));
                if (x == width-1 || alpha[y*width+x+1] < 128)
                    AddFace(vertices, uvs, triangles, uv, new MeshPoint(r,b,-depth), new MeshPoint(r,t,-depth), new MeshPoint(r,t,depth), new MeshPoint(r,b,depth));
                if (y == 0 || alpha[(y-1)*width+x] < 128)
                    AddFace(vertices, uvs, triangles, uv, new MeshPoint(l,b,depth), new MeshPoint(l,b,-depth), new MeshPoint(r,b,-depth), new MeshPoint(r,b,depth));
                if (y == height-1 || alpha[(y+1)*width+x] < 128)
                    AddFace(vertices, uvs, triangles, uv, new MeshPoint(l,t,-depth), new MeshPoint(l,t,depth), new MeshPoint(r,t,depth), new MeshPoint(r,t,-depth));
            }
            return new PixelMeshData { Positions=vertices.ToArray(), Uvs=uvs.ToArray(), Triangles=triangles.ToArray() };
        }
        private static void AddFace(List<MeshPoint> v, List<MeshUv> uv, List<int> ix, MeshUv color, MeshPoint a, MeshPoint b, MeshPoint c, MeshPoint d)
        {
            int n=v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            for(int i=0;i<4;i++) uv.Add(color);
            ix.Add(n); ix.Add(n+1); ix.Add(n+2); ix.Add(n); ix.Add(n+2); ix.Add(n+3);
        }
    }
}
