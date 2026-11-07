using System;

namespace TotemMod
{
    // Settings contain only bounded primitives. Session is supplied by the
    // transport so a delayed appearance cannot decorate another core lifetime.
    public sealed class BetaAppearance
    {
        public readonly bool Enabled, UsePlayerColor, UseInPop, Rainbow;
        public readonly int Style, SourceActor;
        public readonly float TintR, TintG, TintB, Scale, Spin, Bob, RainbowSpeed, TiltX, TiltY, TiltZ;
        public readonly string Session;

        public BetaAppearance(bool enabled, int style, int sourceActor, bool usePlayerColor, bool useInPop,
            float tintR, float tintG, float tintB, float scale, float spin, float bob, bool rainbow,
            float rainbowSpeed, float tiltX, float tiltY, float tiltZ)
            : this(enabled, style, sourceActor, usePlayerColor, useInPop, tintR, tintG, tintB, scale,
                spin, bob, rainbow, rainbowSpeed, tiltX, tiltY, tiltZ, null) { }

        private BetaAppearance(bool enabled, int style, int sourceActor, bool usePlayerColor, bool useInPop,
            float tintR, float tintG, float tintB, float scale, float spin, float bob, bool rainbow,
            float rainbowSpeed, float tiltX, float tiltY, float tiltZ, string session)
        {
            Enabled = enabled; Style = style; SourceActor = sourceActor; UsePlayerColor = usePlayerColor; UseInPop = useInPop;
            TintR = tintR; TintG = tintG; TintB = tintB; Scale = scale; Spin = spin; Bob = bob;
            Rainbow = rainbow; RainbowSpeed = rainbowSpeed; TiltX = tiltX; TiltY = tiltY; TiltZ = tiltZ; Session = session;
        }

        public bool IsValid
        {
            get { return Style >= 0 && Style <= 4 && SourceActor >= 0 && Range(TintR, 0f, 1f) && Range(TintG, 0f, 1f) &&
                Range(TintB, 0f, 1f) && Range(Scale, .5f, 2f) && Range(Spin, -120f, 120f) && Range(Bob, 0f, .025f) &&
                Range(RainbowSpeed, .05f, .5f) && Range(TiltX, -180f, 180f) && Range(TiltY, -180f, 180f) && Range(TiltZ, -180f, 180f); }
        }

        private static bool Range(float value, float min, float max) { return !float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max; }
        internal BetaAppearance WithSession(string session)
        {
            return new BetaAppearance(Enabled, Style, SourceActor, UsePlayerColor, UseInPop, TintR, TintG, TintB,
                Scale, Spin, Bob, Rainbow, RainbowSpeed, TiltX, TiltY, TiltZ, session);
        }
        public object[] Encode()
        {
            return new object[] { 1, Enabled, Style, SourceActor, UsePlayerColor, UseInPop, TintR, TintG, TintB,
                Scale, Spin, Bob, Rainbow, RainbowSpeed, TiltX, TiltY, TiltZ };
        }
        public static bool TryDecode(object value, out BetaAppearance appearance)
        {
            appearance = null;
            object[] d = value as object[];
            if (d == null || d.Length != 17 || !(d[0] is int) || (int)d[0] != 1 || !(d[1] is bool) ||
                !(d[2] is int) || !(d[3] is int) || !(d[4] is bool) || !(d[5] is bool) || !(d[12] is bool)) return false;
            for (int i = 6; i < 17; i++) if (i != 12 && !(d[i] is float)) return false;
            BetaAppearance next = new BetaAppearance((bool)d[1], (int)d[2], (int)d[3], (bool)d[4], (bool)d[5],
                (float)d[6], (float)d[7], (float)d[8], (float)d[9], (float)d[10], (float)d[11], (bool)d[12],
                (float)d[13], (float)d[14], (float)d[15], (float)d[16]);
            if (!next.IsValid) return false;
            appearance = next; return true;
        }
        internal bool SameSettings(BetaAppearance other)
        {
            return other != null && Enabled == other.Enabled && Style == other.Style && SourceActor == other.SourceActor &&
                UsePlayerColor == other.UsePlayerColor && UseInPop == other.UseInPop && TintR == other.TintR && TintG == other.TintG &&
                TintB == other.TintB && Scale == other.Scale && Spin == other.Spin && Bob == other.Bob && Rainbow == other.Rainbow &&
                RainbowSpeed == other.RainbowSpeed && TiltX == other.TiltX && TiltY == other.TiltY && TiltZ == other.TiltZ;
        }
    }
}
