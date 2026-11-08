namespace MaceMod.Shared
{
    internal static class MacePresentation
    {
        internal static bool Visible(bool equipped) { return equipped; }
        internal static bool CanAttack(bool equipped,bool menuOpen) { return equipped && !menuOpen; }
        internal static int Layer(int cullingMask)
        {
            if((cullingMask&1)!=0) return 0;
            for(int layer=1;layer<32;layer++) if((cullingMask&(1<<layer))!=0) return layer;
            return 0;
        }
    }
}
