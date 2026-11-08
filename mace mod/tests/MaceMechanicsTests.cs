using System;
using MaceMod.Shared;

internal static class MaceMechanicsTests
{
    private static int assertions;

    private static void Check(bool condition, string description)
    {
        assertions++;
        if (!condition) throw new Exception(description);
    }

    private static void Near(float actual, float expected, float tolerance, string description)
    {
        Check(!float.IsNaN(actual) && Math.Abs(actual - expected) <= tolerance,
            description + ": expected " + expected + ", got " + actual);
    }

    private static MaceFall Drop(float scale, int hz)
    {
        MaceFall fall = new MaceFall();
        fall.Update(12f * scale, 0f, true, scale);
        for (int i = 1; i <= hz; i++)
            fall.Update((12f - 6f * i / hz) * scale, (float)i / hz, false, scale);
        return fall;
    }

    private static void FallDistanceAndFrameRate()
    {
        foreach (int hz in new int[] { 45, 72, 90 })
        {
            MaceFall fall = Drop(1f, hz);
            Near(fall.Blocks, 6f, 0.002f, "drop from ground counts meters at " + hz + " Hz");
            Check(fall.Falling, "active drop is falling");
            Near(fall.VelocityY, -6f, 0.002f, "vertical velocity uses elapsed time");
            Near(Drop(0.5f, hz).Blocks, 6f, 0.002f, "small player scaled meters are blocks");
            Near(Drop(2f, hz).Blocks, 6f, 0.002f, "large player scaled meters are blocks");
        }
        MaceFall unobservedStart = new MaceFall();
        unobservedStart.Update(12, 1, false);
        unobservedStart.Update(11, 1.1f, false);
        Near(unobservedStart.Blocks, 1, 0.001f, "airborne first observation establishes drop origin");
    }

    private static void GroundAndConsume()
    {
        MaceFall fall = Drop(1, 90);
        fall.Update(5.9f, 1.02f, true);
        Near(fall.Blocks, 6.1f, 0.002f, "landing sample includes final descent");
        Check(!fall.Falling, "ground contact stops Falling");
        fall.Update(5.9f, 1.10f, true);
        Near(fall.Blocks, 6.1f, 0.002f, "landing credit survives collision update order");
        fall.Update(5.9f, 1.24f, true);
        Near(fall.Blocks,6.1f,0.002f,"smash credit survives a brief landing before the swing connects");
        fall.Update(5.9f, 1.30f, true);
        Near(fall.Blocks, 0, 0, "standing cannot retain landing credit");
        fall = Drop(1, 72);
        fall.Consume();
        Near(fall.Blocks, 0, 0, "successful hit consumes all credit");
        fall.Consume();
        Near(fall.Blocks, 0, 0, "repeated hit cannot reuse credit");
        fall.Update(5.9f, 1.02f, false);
        Near(fall.Blocks, 0.1f, 0.002f, "only new descent can accrue after hit");
        fall = Drop(1, 45);
        fall.Update(6, 1.05f, false);
        fall.Update(6, 1.14f, false);
        Near(fall.Blocks, 0, 0, "stationary airborne or walking sample cannot bank old credit");
        fall.Update(6, 1.16f, true);
        Near(fall.Blocks, 0, 0, "grounding cannot resurrect expired credit");
    }

    private static void AscentAndJitter()
    {
        MaceFall fall = Drop(1, 90);
        fall.Update(6.002f, 1.01f, false);
        fall.Update(5.998f, 1.02f, false);
        Check(fall.Blocks >= 5.99f, "millimeter tracking jitter preserves real descent");
        fall.Update(6.2f, 1.04f, false);
        Near(fall.Blocks, 0, 0, "new jump discards prior descent");
        Check(!fall.Falling && fall.VelocityY > 0, "upward travel is never a fall");
        fall.Update(7, 1.14f, false);
        fall.Update(8, 1.24f, false);
        fall.Update(7.5f, 1.34f, false);
        Near(fall.Blocks, 0.5f, 0.002f, "descending segment starts at new jump apex");
        fall.Reset();
        Near(fall.Blocks, 0, 0, "room reset clears credit");
        Check(!fall.Falling && fall.VelocityY == 0, "room reset clears velocity");
    }

    private static void InvalidSamplesAndWarps()
    {
        MaceFall fall = Drop(1, 90);
        fall.Update(-100, 1.01f, false);
        Near(fall.Blocks, 0, 0, "teleport cannot create fall credit");
        fall = Drop(1, 90);
        fall.Update(5, 2, false);
        Near(fall.Blocks, 0, 0, "stale sample gap discards fall credit");
        fall = Drop(1, 90);
        fall.Update(5, 1, false);
        Near(fall.Blocks, 0, 0, "duplicate clock cannot credit motion");
        fall = Drop(1, 90);
        fall.Update(5, 0.5f, false);
        Near(fall.Blocks, 0, 0, "backward clock clears state");
        foreach (float invalid in new float[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            fall = Drop(1, 90); fall.Update(invalid, 1.01f, false);
            Near(fall.Blocks, 0, 0, "invalid height clears state");
            fall = Drop(1, 90); fall.Update(5, invalid, false);
            Near(fall.Blocks, 0, 0, "invalid clock clears state");
            fall = Drop(1, 90); fall.Update(5, 1.01f, false, invalid);
            Near(fall.Blocks, 0, 0, "invalid player scale clears state");
        }
        foreach (float invalid in new float[] { 0, -1, float.Epsilon })
        {
            fall = Drop(1, 90); fall.Update(5, 1.01f, false, invalid);
            Near(fall.Blocks, 0, 0, "nonphysical player scale clears state");
        }
        fall = Drop(1, 90); fall.Update(5, 1.02f, false, 2);
        Near(fall.Blocks, 0, 0, "player resize cannot retain old fall credit");
        fall = new MaceFall(); fall.Update(float.MaxValue, 0, false);
        fall.Update(-float.MaxValue, 0.01f, false);
        Check(fall.Blocks == 0 && fall.VelocityY == 0, "extreme finite heights cannot overflow");
        fall = new MaceFall(); fall.Update(100, 0, false);
        for (int i = 1; i <= 100; i++) fall.Update(100 - i, i * 0.1f, false);
        Near(fall.Blocks, 64, 0, "sustained fall credit is capped");
    }

    private static void BoundedMonotonicForces()
    {
        for (int density = 0; density <= 5; density++)
        for (int knockback = 0; knockback <= 2; knockback++)
        {
            float previous = -1;
            for (int block = 0; block <= 128; block++)
            {
                float speed = MaceMechanics.Knockback(block * 0.5f, density, knockback, 1);
                Check(speed >= previous && speed >= 0 && speed <= 18, "fall knockback is monotonic and bounded");
                previous = speed;
            }
        }
        for (int block = 0; block <= 64; block++)
        {
            float previous = -1;
            for (int density = 0; density <= 5; density++)
            {
                float speed = MaceMechanics.Knockback(block, density, 0, 1);
                Check(speed >= previous, "Density never reduces fling"); previous = speed;
            }
            previous = -1;
            for (int level = 0; level <= 2; level++)
            {
                float speed = MaceMechanics.Knockback(block, 0, level, 1);
                Check(speed >= previous, "Knockback enchant never reduces fling"); previous = speed;
            }
            previous = -1;
            for (int step = 1; step <= 8; step++)
            {
                float speed = MaceMechanics.Knockback(block, 5, 2, step * 0.25f);
                Check(speed >= previous && speed <= 18, "power scaling is monotonic and bounded"); previous = speed;
            }
        }
        Check(MaceMechanics.Knockback(0, 0, 0, 1) > 0, "normal mace strike still flings");
        Check(MaceMechanics.Knockback(10, 0, 0, 1) > MaceMechanics.Knockback(0, 0, 0, 1), "fall strengthens fling");
        Near(MaceMechanics.Knockback(0,5,2,2),MaceMechanics.Knockback(0,0,2,2),0,"Density adds fall power instead of consuming the standing-hit force budget");
        Check(MaceMechanics.Knockback(4,5,2,2)>MaceMechanics.Knockback(0,5,2,2)*1.4f,"Max-enchanted falling smash remains meaningfully stronger than a standing hit");
        Near(MaceMechanics.Knockback(float.MaxValue, int.MaxValue, int.MaxValue, float.MaxValue), 18, 0, "extreme forces clamp before arithmetic");
        Near(MaceMechanics.Knockback(5, -20, -20, -20), MaceMechanics.Knockback(5, 0, 0, 0.25f), 0, "negative force modifiers clamp to minimum");
        Near(MaceMechanics.WindLift(1.49f, 3), 0, 0, "Wind Burst requires smash fall");
        Near(MaceMechanics.WindLift(64, 0), 0, 0, "no Wind Burst means no lift");
        Check(MaceMechanics.WindLift(2, 1) > 0, "Wind Burst gives upward lift");
        float previousLift = 0;
        for (int i = 3; i <= 128; i++)
        {
            float lift = MaceMechanics.WindLift(i * 0.5f, 3);
            Check(lift >= previousLift && lift <= 9, "Wind Burst fall scaling is bounded"); previousLift = lift;
        }
        Near(MaceMechanics.WindLift(float.MaxValue, int.MaxValue), 9, 0, "extreme Wind Burst stays bounded");
        foreach (float invalid in new float[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Near(MaceMechanics.Knockback(invalid, 0, 0, 1), 0, 0, "invalid fall never creates fling");
            Near(MaceMechanics.Knockback(5, 0, 0, invalid), 0, 0, "invalid power never creates fling");
            Near(MaceMechanics.WindLift(invalid, 1), 0, 0, "invalid fall never creates lift");
        }
    }

    private static void SegmentGeometry()
    {
        Near(MaceMechanics.HitRadius(1,1,1),0.64f,0.0001f,"Hit radius includes mace-head width");
        Near(MaceMechanics.HitRadius(0.5f,0.5f,1),0.32f,0.0001f,"Scaled-player hit radius is consistent");
        Check(MaceMechanics.ClosestBodyZone(-1,0.85f,0,1,0.85f,0,0,0,0,1)==1,"Head-height swing selects upper body");
        Check(MaceMechanics.ClosestBodyZone(-1,-0.85f,0,1,-0.85f,0,0,0,0,1)==-1,"Low swing selects lower body");
        Check(MaceMechanics.ClosestBodyZone(-1,0,0,1,0,0,0,0,0,1)==0,"Torso-height swing keeps torso center");
        for(int frame=0;frame<=16;frame++)
        {
            float y=-0.8f+frame*0.1f;
            int zone=MaceMechanics.ClosestBodyZone(-1,y,0,1,y,0,0,0,0,1);
            Check(MaceMechanics.SegmentDistanceSquared(-1,y,0,1,y,0,0,zone*0.35f,0)<=0.64f*0.64f,"Continuous target coverage has no gaps between head, torso and lower body");
        }
        Near(MaceMechanics.ImpulseVertical(-30,6),6,0,"Wind Burst reverses a fast descent into an upward rebound");
        Near(MaceMechanics.ImpulseVertical(-2,4),4,0,"Grounded-target lift survives downward movement");
        Near(MaceMechanics.ImpulseVertical(3,4),7,0,"Existing upward momentum is retained");
        Near(MaceMechanics.ImpulseVertical(-3,0),-3,0,"Zero lift does not create a rebound");
        Near(MaceMechanics.ImpulseVertical(float.NaN,4),0,0,"Invalid movement is rejected");
        Near(MaceMechanics.ImpulseVertical(4,float.PositiveInfinity),0,0,"Invalid lift is rejected");
        Near(MaceMechanics.SegmentDistanceSquared(0, 0, 0, 2, 0, 0, 1, 3, 0), 9, 0.0001f, "interior segment projection");
        Near(MaceMechanics.SegmentDistanceSquared(0, 0, 0, 2, 0, 0, -2, 0, 0), 4, 0.0001f, "before segment endpoint");
        Near(MaceMechanics.SegmentDistanceSquared(0, 0, 0, 2, 0, 0, 4, 0, 0), 4, 0.0001f, "after segment endpoint");
        Near(MaceMechanics.SegmentDistanceSquared(1, 2, 3, 1, 2, 3, 4, 6, 3), 25, 0.0001f, "zero length segment");
        Near(MaceMechanics.SegmentDistanceSquared(-float.MaxValue, 0, 0, float.MaxValue, 0, 0, 0, 0, 0), 0, 0, "extreme segment projection avoids overflow");
        float huge = MaceMechanics.SegmentDistanceSquared(0, 0, 0, 1, 0, 0, 0, float.MaxValue, 0);
        Check(!float.IsInfinity(huge) && huge > 0, "finite geometry saturates safely");
        Check(float.IsPositiveInfinity(MaceMechanics.SegmentDistanceSquared(float.NaN, 0, 0, 1, 0, 0, 0, 0, 0)), "invalid geometry cannot count as collision");
        Check(float.IsPositiveInfinity(MaceMechanics.SegmentDistanceSquared(0, 0, 0, 1, 0, 0, 0, float.PositiveInfinity, 0)), "invalid target cannot count as collision");
    }

    public static int Main()
    {
        try
        {
            FallDistanceAndFrameRate(); GroundAndConsume(); AscentAndJitter();
            InvalidSamplesAndWarps(); BoundedMonotonicForces(); SegmentGeometry();
            Console.WriteLine("PASS MaceMechanicsTests (" + assertions + " assertions)");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine("FAIL MaceMechanicsTests: " + e.Message); return 1; }
    }
}
