namespace TotemMod
{
    public sealed class MenuHoldGesture
    {
        private bool downBefore;
        private bool fired;
        private double began;
        public bool Update(bool allowed, bool down, double now,double holdSeconds = 0.6)
        {
            if (!allowed || !down) { downBefore = down; fired = !allowed && down; return false; }
            if (!downBefore) { began = now; fired = false; }
            downBefore = true;
            if (double.IsNaN(holdSeconds) || double.IsInfinity(holdSeconds)) holdSeconds=0.6;
            holdSeconds=System.Math.Max(0.3,System.Math.Min(1.5,holdSeconds));
            if (fired || now < began + holdSeconds) return false;
            fired = true;
            return true;
        }
    }
}
