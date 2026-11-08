using System;

namespace MaceMod.Shared
{
    // A single continuous descent, measured in player-scaled meters (blocks).
    // Call Reset when changing room or rig; no samples survive an invalid clock.
    internal sealed class MaceFall
    {
        private const double GraceSeconds = 0.12;
        private const double LandingGraceSeconds = 0.25;
        private bool initialized, wasGrounded, falling;
        private float lastHeight, lastTime, scale, blocks, velocityY;
        private double peak, lowest, lastDescent, landedAt;

        internal float Blocks { get { return blocks; } }
        internal bool Falling { get { return falling; } }
        // World-space vertical speed; positive means ascent.
        internal float VelocityY { get { return velocityY; } }

        internal void Update(float height, float now, bool grounded, float playerScale = 1)
        {
            if (!MaceMechanics.Finite(height) || !MaceMechanics.Finite(now) ||
                !MaceMechanics.Finite(playerScale) || playerScale < 0.01f || playerScale > 100f)
            {
                Reset();
                return;
            }
            if (!initialized)
            {
                Baseline(height, now, grounded, playerScale);
                return;
            }

            double dt = (double)now - lastTime;
            double dy = (double)height - lastHeight;
            double scaledMove = Math.Abs(dy) / playerScale;
            if (dt <= 0 || dt > 0.30 || Math.Abs((double)playerScale - scale) > scale * 0.001 ||
                scaledMove > Math.Max(5, dt * 70))
            {
                Baseline(height, now, grounded, playerScale);
                return;
            }

            velocityY = (float)Math.Max(-7000, Math.Min(7000, dy / dt));
            falling = false;
            if (wasGrounded)
            {
                // Repeated contact samples must not renew the landing grace.
                if (grounded)
                {
                    if ((double)now - landedAt > LandingGraceSeconds) blocks = 0;
                    peak = lowest = height;
                    velocityY = 0;
                    SaveSample(height, now, true);
                    return;
                }
                blocks = 0;
                peak = lowest = lastHeight;
            }

            if ((double)height - lowest > playerScale * 0.04)
            {
                // New ascent cannot carry a prior drop into the next jump.
                blocks = 0;
                peak = lowest = height;
            }
            else
            {
                if (height > peak) peak = height;
                if (height < lowest) lowest = height;
                if (dy < -playerScale * 0.0005 && peak - height > playerScale * 0.01)
                {
                    blocks = (float)Math.Min(64, Math.Max(0, (peak - height) / playerScale));
                    falling = !grounded;
                    lastDescent = now;
                }
                else if ((double)now - lastDescent > GraceSeconds)
                {
                    blocks = 0;
                    peak = lowest = height;
                }
            }

            if (grounded)
            {
                landedAt = now;
                falling = false;
                velocityY = 0;
            }
            SaveSample(height, now, grounded);
        }

        internal void Reset()
        {
            initialized = wasGrounded = falling = false;
            lastHeight = lastTime = scale = blocks = velocityY = 0;
            peak = lowest = lastDescent = landedAt = 0;
        }

        internal void Consume()
        {
            blocks = 0;
            falling = false;
            peak = lowest = lastHeight;
            lastDescent = landedAt = lastTime;
        }

        private void Baseline(float height, float now, bool grounded, float playerScale)
        {
            Reset();
            initialized = true;
            scale = playerScale;
            peak = lowest = height;
            lastDescent = landedAt = now;
            SaveSample(height, now, grounded);
        }

        private void SaveSample(float height, float now, bool grounded)
        {
            lastHeight = height;
            lastTime = now;
            wasGrounded = grounded;
        }
    }

    internal static class MaceMechanics
    {
        internal static float ImpulseVertical(float velocity,float lift)
        {
            if(!Finite(velocity) || !Finite(lift)) return 0;
            double combined=(double)velocity+lift;
            if(lift>0) combined=Math.Max(combined,lift);
            return (float)Math.Max(-float.MaxValue,Math.Min(float.MaxValue,combined));
        }

        internal static float Knockback(float blocks, int density, int knockback, float power)
        {
            if (!Finite(blocks) || !Finite(power)) return 0;
            double drop = Clamp(blocks, 0, 64);
            double strength = 3 + (1.4 + 0.5 * Clamp(density, 0, 5)) * Math.Sqrt(drop) + 1.4 * Clamp(knockback, 0, 2);
            return (float)Math.Min(18, strength * Clamp(power, 0.25, 2));
        }

        internal static float WindLift(float blocks, int level)
        {
            if (!Finite(blocks) || blocks < 1.5f || level <= 0) return 0;
            return (float)Math.Min(9, 2 + 1.5 * Clamp(level, 0, 3) + 0.5 * Math.Sqrt(Clamp(blocks, 0, 64)));
        }

        internal static float HitRadius(float targetScale,float attackerScale,float weaponScale)
        {
            if(!Finite(targetScale) || !Finite(attackerScale) || !Finite(weaponScale)) return 0;
            return (float)(0.48*Clamp(targetScale,0.2,4)+0.16*Clamp(attackerScale,0.2,4)*Clamp(weaponScale,0.5,1.5));
        }

        internal static int ClosestBodyZone(float ax,float ay,float az,float bx,float by,float bz,float px,float py,float pz,float targetScale)
        {
            int zone=0;
            float best=SegmentDistanceSquared(ax,ay,az,bx,by,bz,px,py,pz);
            for(int candidate=-1;candidate<=1;candidate+=2)
            {
                float distance=SegmentDistanceSquared(ax,ay,az,bx,by,bz,px,py+candidate*0.35f*targetScale,pz);
                if(distance<best) { best=distance; zone=candidate; }
            }
            return zone;
        }

        // Double intermediates keep all finite float coordinates representable.
        // Invalid geometry fails closed; huge finite squared distances saturate.
        internal static float SegmentDistanceSquared(float ax, float ay, float az, float bx, float by, float bz, float px, float py, float pz)
        {
            if (!Finite(ax) || !Finite(ay) || !Finite(az) || !Finite(bx) || !Finite(by) ||
                !Finite(bz) || !Finite(px) || !Finite(py) || !Finite(pz)) return float.PositiveInfinity;
            double dx = (double)bx - ax, dy = (double)by - ay, dz = (double)bz - az;
            double tx = (double)px - ax, ty = (double)py - ay, tz = (double)pz - az;
            double lengthSquared = dx * dx + dy * dy + dz * dz;
            double t = lengthSquared > 0 ? Clamp((tx * dx + ty * dy + tz * dz) / lengthSquared, 0, 1) : 0;
            double ex = tx - t * dx, ey = ty - t * dy, ez = tz - t * dz;
            return (float)Math.Min(float.MaxValue, ex * ex + ey * ey + ez * ez);
        }

        internal static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static double Clamp(double value, double min, double max) { return Math.Max(min, Math.Min(max, value)); }
    }
}
