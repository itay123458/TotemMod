using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using Mono.Cecil;

namespace TotemMod.Updating.Preloader
{
    // BepInEx 5 runs Initialize before plugin discovery, even with no patch targets.
    public static class Patcher
    {
        public static IEnumerable<string> TargetDLLs { get { return new string[0]; } }
        public static void Patch(AssemblyDefinition assembly) { }

        public static void Initialize()
        {
            ManualLogSource log = Logger.CreateLogSource("TotemModUpdater");
            try
            {
                string status = UpdateInstaller.ApplyPending(Paths.BepInExRootPath,
                    Path.Combine(Paths.PluginPath, "TotemMod.dll"),
                    Path.Combine(Paths.ManagedPath, "Assembly-CSharp.dll"));
                if (status != null) log.LogInfo(status);
            }
            catch (Exception error)
            {
                log.LogError("Totem Mod update was not applied; the installed plugin was retained. " + error.Message);
            }
        }
    }
}
