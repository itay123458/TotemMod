using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using Mono.Cecil;

namespace TotemMod.Updating.CompanionPreloader
{
    // A separate helper permits an existing Totem updater installation to apply
    // companion updates before Chainloader discovers either plugin assembly.
    public static class Patcher
    {
        public static IEnumerable<string> TargetDLLs { get { return new string[0]; } }
        public static void Patch(AssemblyDefinition assembly) { }

        public static void Initialize()
        {
            ManualLogSource log = Logger.CreateLogSource("TotemModCompanionUpdater");
            try
            {
                string status = UpdateInstaller.ApplyPending(Paths.BepInExRootPath,
                    Path.Combine(Paths.BepInExRootPath, "plugins", "TotemMod.dll"),
                    Path.Combine(Paths.ManagedPath, "Assembly-CSharp.dll"));
                if (status != null) log.LogInfo(status);
            }
            catch (Exception error)
            {
                log.LogError("Totem Mod update was not applied; the installed plugin was retained. " + error.Message);
            }
            // Deliberately independent: a rejected Totem stage cannot prevent a
            // valid Mace stage from being installed during this same startup.
            try
            {
                string status = MaceUpdateInstaller.ApplyPending(Paths.BepInExRootPath,
                    Path.Combine(Paths.BepInExRootPath, "plugins", "MaceMod.dll"),
                    Path.Combine(Paths.ManagedPath, "Assembly-CSharp.dll"));
                if (status != null) log.LogInfo(status);
            }
            catch (Exception error)
            {
                log.LogError("Mace Mod update was not applied; the installed plugin was retained. " + error.Message);
            }
        }
    }
}
