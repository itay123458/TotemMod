using System;

namespace TotemMod
{
    // Menu-local coordinates keep filtering independent of canvas scale and
    // Unity's frame clock. One point is shared by selection and all visuals.
    internal sealed class MenuPointerState
    {
        public bool Valid { get; private set; }
        public float X { get; private set; }
        public float Y { get; private set; }
        private double lastTime;
        private double clickTime = double.NegativeInfinity;

        public void Update(bool valid, float x, float y, double now, float smoothing)
        {
            if (!valid || !Finite(x) || !Finite(y) || double.IsNaN(now) || double.IsInfinity(now))
            {
                Reset();
                return;
            }
            if (!Finite(smoothing)) smoothing = 1;
            smoothing = Math.Max(0, Math.Min(1, smoothing));
            double elapsed = now - lastTime;
            float dx = x - X, dy = y - Y;
            // Snap intentional target changes; filter the small motions that
            // make a steady hand flicker between adjacent hit areas.
            bool acquire = !Valid || elapsed < 0 || elapsed > 0.25 || dx * dx + dy * dy > 1024;
            if (acquire || smoothing == 0) { X = x; Y = y; }
            else
            {
                float blend = (float)(1 - Math.Exp(-45 * elapsed / smoothing));
                X += dx * blend;
                Y += dy * blend;
            }
            Valid = true;
            lastTime = now;
        }

        public void Click(double now)
        {
            if (Valid && !double.IsNaN(now) && !double.IsInfinity(now)) clickTime = now;
        }

        public float Pulse(double now)
        {
            double elapsed = now - clickTime;
            return !Valid || double.IsNaN(elapsed) || elapsed < 0 || elapsed >= 0.1 ? 0 : (float)(1 - elapsed / 0.1);
        }

        public void Reset()
        {
            Valid = false;
            X = Y = 0;
            lastTime = 0;
            clickTime = double.NegativeInfinity;
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}
