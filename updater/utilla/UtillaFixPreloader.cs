using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using Mono.Cecil;

namespace TotemMod.Updating.UtillaFix
{
    public static class Patcher
    {
        public static IEnumerable<string> TargetDLLs { get { return new string[0]; } }
        public static void Patch(AssemblyDefinition assembly) { }
        public static void Initialize()
        {
            ManualLogSource log=Logger.CreateLogSource("TotemModUtillaFix");
            try
            {
                using(Stream stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("TotemMod.UtillaCompat.Payload.dll"))
                {
                    if(stream==null) throw new InvalidDataException("Embedded Utilla compatibility payload is missing.");
                    log.LogInfo(UtillaRepair.Apply(Paths.BepInExRootPath,Paths.ManagedPath,UtillaRepair.Read(stream,1024*1024)));
                }
            }
            catch(Exception error) { log.LogWarning("Utilla repair was skipped; installed files were retained. "+error.Message); }
        }
    }
}
