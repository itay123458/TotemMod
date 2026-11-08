using System;
using MaceMod.Shared;

internal static class ModdedRoomMarkerTests
{
    private static int checks;
    private static void Check(bool value,string message) { checks++; if(!value) throw new Exception(message); }
    public static int Main()
    {
        try
        {
            const string marker="MODDED_Infection",wire="private;COMPETITIVE;Infection;;";
            Check(ModdedRoomMarker.Allows(true,false,marker,wire),"Private patched host canonical Infection accepts exact room marker");
            Check(ModdedRoomMarker.Property=="org.legoandmars.utilla.mode","Uses shared Utilla marker property");
            Check(!ModdedRoomMarker.Allows(true,false,null,wire),"Regular Infection without marker rejected");
            Check(!ModdedRoomMarker.Allows(true,true,marker,wire),"Public room marker rejected");
            Check(!ModdedRoomMarker.Allows(false,false,marker,wire),"Offline or departed room rejected");
            Check(ModdedRoomMarker.Allows(true,false,marker,wire),"New private room accepts its own current marker");
            foreach(object invalid in new object[] {null,1,true,new object(),"","modded_Infection","MODDED_infection","MODDED_Infection ","MODDED_Infection_more","xMODDED_Infection"})
                Check(!ModdedRoomMarker.Allows(true,false,invalid,wire),"Invalid or inexact marker rejected");
            foreach(object invalid in new object[] {null,1,true,new object(),"","private;COMPETITIVE;MODDED_Infection;;","public;COMPETITIVE;Infection;;","Private;COMPETITIVE;Infection;;","xprivate;COMPETITIVE;Infection;;","private;COMPETITIVE;infection;;","private;COMPETITIVE;InfectionExtra;;","private;COMPETITIVE;xInfection;;","private;COMPETITIVE; Infection;;","private;COMPETITIVE;Infection ;;","private;Infection;;","private;COMPETITIVE;Infection","private;COMPETITIVE;Infection;MODDED_Infection;"})
                Check(!ModdedRoomMarker.Allows(true,false,marker,invalid),"Invalid, legacy or inexact canonical wire mode rejected");
            AppDomain.MonitoringIsEnabled=true;
            for(int i=0;i<10;i++) ModdedRoomMarker.Allows(true,false,marker,wire);
            GC.Collect(); long before=AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize;
            bool accepted=true;
            for(int i=0;i<10000;i++) accepted&=ModdedRoomMarker.Allows(true,false,marker,wire);
            GC.Collect(); long allocated=AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize-before;
            Check(accepted && allocated<1024,"Per-frame marker checks allocate no objects: "+allocated);
            Console.WriteLine("Modded room marker: "+checks+" checks passed."); return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}
