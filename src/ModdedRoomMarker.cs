using System;

namespace TotemMod
{
    internal static class ModdedRoomMarker
    {
        internal const string Property="org.legoandmars.utilla.mode";
        internal static bool Allows(bool inRoom,bool isPublic,object marker,object wireMode)
        {
            if(!inRoom || isPublic || !string.Equals(marker as string,"MODDED_Infection",StringComparison.Ordinal)) return false;
            string wire=wireMode as string;
            if(wire==null || wire.Length<8 || string.CompareOrdinal(wire,0,"private;",0,8)!=0) return false;
            int queueEnd=wire.IndexOf(';',8);
            if(queueEnd<0) return false;
            int modeStart=queueEnd+1,modeEnd=wire.IndexOf(';',modeStart);
            return modeEnd>=0 && modeEnd-modeStart==9 && string.CompareOrdinal(wire,modeStart,"Infection",0,9)==0 &&
                modeEnd+2==wire.Length && wire[modeEnd+1]==';';
        }
    }
}
