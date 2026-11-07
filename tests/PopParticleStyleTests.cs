using System;
using TotemMod;

internal static class PopParticleStyleTests
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }

    public static int Main()
    {
        PopSparkPose visible = PopParticleStyle.Evaluate(0,32,0.5f,0.2f,1,1,0);
        Check(visible.Size >= 0.028f,"Default sparks need a readable pixel silhouette, larger than the old 1.2 cm cubes.");
        PopSparkPose normal = PopParticleStyle.Evaluate(8,32,0.3f,0.5f,1,1,0);
        PopSparkPose large = PopParticleStyle.Evaluate(8,32,0.3f,0.5f,2,1,0);
        Check(Math.Abs(large.Size - normal.Size*2) < 0.0001f,"Particle size controls scale independently of spread.");
        PopSparkPose wide = PopParticleStyle.Evaluate(8,32,0.3f,0.5f,1,2,0);
        Check(Math.Abs(wide.X-normal.X*2) < 0.0001f && Math.Abs(wide.Y-normal.Y*2) < 0.0001f,"Spread controls both screen-plane axes.");
        PopSparkPose swirl = PopParticleStyle.Evaluate(8,32,0.3f,0.5f,1,1,1);
        Check(Math.Abs(swirl.X-normal.X) + Math.Abs(swirl.Y-normal.Y) > 0.04f,"Swirl follows a visibly different path from burst.");
        Check(PopParticleStyle.Evaluate(0,32,0.5f,PopParticleStyle.Duration,1,1,0).Size == 0,"Sparks fully disappear at animation end.");
        for (int mode=0; mode<2; mode++)
        for (int i=0; i<64; i++)
        for (int frame=0; frame<=100; frame++)
        {
            PopSparkPose p = PopParticleStyle.Evaluate(i,64,i/64f,frame*PopParticleStyle.Duration/100f,3,2,mode);
            Check(Finite(p.X) && Finite(p.Y) && Finite(p.Z) && Finite(p.Size) && Finite(p.Rotation),"Every bounded animation frame stays finite.");
            Check(Math.Abs(p.X)<1.5f && Math.Abs(p.Y)<1.5f && Math.Abs(p.Z)<0.1f && p.Size>=0 && p.Size<=0.18f,"Maximum settings keep the local effect bounded and in front of its origin.");
        }
        PopSparkPose invalid = PopParticleStyle.Evaluate(0,0,float.NaN,float.PositiveInfinity,float.NaN,float.NegativeInfinity,99);
        Check(Finite(invalid.X) && Finite(invalid.Y) && Finite(invalid.Z) && Finite(invalid.Size),"Malformed external config cannot poison Unity transforms.");
        Console.WriteLine("PASS " + checks + " pop particle visibility, motion, lifetime and bounds checks");
        return 0;
    }
}
