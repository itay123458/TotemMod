using BepInEx;

namespace MaceMod
{
    // Preserve update identity so an existing installation can retire the old
    // receiver without running any of its networking, rendering or movement.
    [BepInPlugin("macemod.gtag.rhys","PrimalCraft Mace Removed",MaceVersion)]
    [BepInProcess("Gorilla Tag.exe")]
    public sealed class MacePlugin : BaseUnityPlugin
    {
        internal const string MaceVersion="1.9.8";
        private void Awake()
        {
            Logger.LogInfo("The mace feature has been removed. This compatibility plugin is inactive and may be deleted while Gorilla Tag is closed.");
        }
    }
}
