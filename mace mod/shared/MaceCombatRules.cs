namespace MaceMod.Shared
{
    internal static class MaceCombatRules
    {
        internal static int Resolve(bool privateModded,bool participants,bool attackerTagged,bool targetTagged,bool saved)
        {
            if(!privateModded || !participants) return 0;
            if(!attackerTagged) return 2;
            if(targetTagged) return 0;
            return saved ? 1 : 3;
        }
    }
}
