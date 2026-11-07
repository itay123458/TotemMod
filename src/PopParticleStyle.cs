using System;

namespace TotemMod
{
    internal struct PopSparkPose
    {
        internal float X, Y, Z, Size, Rotation;
    }

    internal static class PopParticleStyle
    {
        internal const float Duration = 1.45f;

        internal static PopSparkPose Evaluate(int index, int count, float seed, float elapsed, float size, float spread, int motion)
        {
            seed = Safe(seed, 0.5f, 0, 1);
            elapsed = Safe(elapsed, 0, 0, Duration);
            size = Safe(size, 1, 0.5f, 3);
            spread = Safe(spread, 1, 0.5f, 2);
            count = Math.Max(1, Math.Min(64, count));
            index = Math.Max(0, Math.Min(count-1, index));
            float progress = elapsed / Duration;
            float angle = (float)((index + seed) * Math.PI * 2 / count);
            // Most travel occurs early, then the burst drifts rather than racing
            // outside the headset's field of view for the rest of the animation.
            float travel = 1f - (float)Math.Exp(-2.8f * elapsed);
            float radius = 0.045f + (0.24f + seed * 0.16f) * travel;
            if (motion == 1) angle += elapsed * (2.6f + seed * 0.8f);
            float tail = Safe((progress-0.68f)/0.32f, 0, 0, 1);
            float fade = 1f - tail*tail*(3f-2f*tail);
            return new PopSparkPose
            {
                X = (float)Math.Cos(angle) * radius * spread,
                Y = ((float)Math.Sin(angle) * radius + 0.10f*elapsed - 0.10f*elapsed*elapsed) * spread,
                Z = -0.045f - seed*0.035f,
                Size = (0.032f + seed*0.022f) * size * fade,
                Rotation = (index*47f + elapsed*(motion == 1 ? 95f : 45f)) % 360f
            };
        }

        private static float Safe(float value, float fallback, float min, float max)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? fallback : Math.Max(min,Math.Min(max,value));
        }
    }
}
