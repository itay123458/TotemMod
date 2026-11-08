namespace TotemMod
{
    internal sealed class SurvivalFall
    {
        private bool initialized, descending, pendingZero;
        private double lastTime;
        private float lastHeight, apex, lastScale;
        internal void Reset() { initialized = descending = pendingZero = false; }
        internal int Sample(float height, double now, bool supported, float playerScale = 1, bool groundedFloor = false)
        {
            if (float.IsNaN(height) || float.IsInfinity(height) || double.IsNaN(now) || double.IsInfinity(now))
            { Reset(); return 0; }
            if (float.IsNaN(playerScale) || float.IsInfinity(playerScale) || playerScale <= 0) { Reset(); return 0; }
            if (!initialized) { Start(height, now, playerScale); return 0; }
            if (now <= lastTime) return 0;
            double elapsed = now - lastTime;
            if (elapsed > .5 || System.Math.Abs(playerScale - lastScale) > lastScale * .25)
            { Start(height, now, playerScale); return 0; }
            double delta = height - lastHeight;
            double jumpLimit = System.Math.Max(2 * playerScale, 80 * playerScale * elapsed);
            // A transient tracking loss often produces exactly zero for one frame.
            // Defer that sample, but reset immediately if zero persists: no stored fall debt.
            if (pendingZero && height == 0) { Start(height, now, playerScale); return 0; }
            pendingZero = false;
            if (System.Math.Abs(delta) > jumpLimit)
            {
                if (height == 0 && lastHeight != 0) { pendingZero = true; return 0; }
                Start(height, now, playerScale); return 0;
            }
            double velocity = delta / elapsed;
            if (velocity > .6 * playerScale && descending) { apex = height; descending = false; }
            if (height > apex) apex = height;
            if (velocity < -.6 * playerScale) descending = true;
            lastHeight = height; lastTime = now; lastScale = playerScale;
            // Floor support is definitive; hand contact against a wall is not a
            // landing while moving down quickly.
            if (!groundedFloor && (!supported || velocity < -.6 * playerScale)) return 0;
            double drop = (apex - height) / playerScale;
            int damage = drop > 3 ? (int)System.Math.Min(20, System.Math.Ceiling(drop - 3)) : 0;
            apex = height; descending = false;
            return damage;
        }
        private void Start(float height, double now, float scale)
        {
            initialized = true; descending = pendingZero = false;
            lastHeight = apex = height; lastTime = now; lastScale = scale;
        }
    }
}
