namespace TotemMod
{
    // Steam identity is supplied by the running game's authenticated Steam API.
    // Host status, display names and config values never grant developer access.
    internal static class DeveloperAccess
    {
        internal static bool Allows(ulong steamId,bool initialized,bool loggedOn)
        {
            return initialized && loggedOn && steamId == 76561199807179430UL;
        }
    }
}
