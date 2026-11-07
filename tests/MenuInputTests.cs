using System;
using TotemMod;

internal static class MenuInputTests
{
    private static int checks;
    private static void Check(bool value,string message) { checks++; if (!value) throw new Exception(message); }
    public static int Main()
    {
        MenuHoldGesture input = new MenuHoldGesture();
        Check(!input.Update(true,true,0),"A quick X press does not open settings.");
        Check(!input.Update(true,true,0.59),"Hold must last at least 0.6 seconds.");
        Check(input.Update(true,true,0.6),"Holding X opens settings once.");
        Check(!input.Update(true,true,4),"Continuing to hold cannot repeatedly close/open it.");
        Check(!input.Update(true,false,5),"Release rearms the gesture.");
        Check(!input.Update(true,true,6) && input.Update(true,true,6.7),"Fresh hold toggles again.");
        Check(!input.Update(false,true,10),"Vanilla/offline input is ignored.");
        Check(!input.Update(true,true,20),"Entering modded mode while held cannot open it.");
        Check(!input.Update(true,false,21),"Releasing at the room boundary is safe.");
        Check(!input.Update(true,true,22) && input.Update(true,true,23),"A fresh permitted hold works.");
        input.Update(true,false,24);
        Check(!input.Update(true,true,25,1.2) && !input.Update(true,true,26.19,1.2),"Custom hold duration waits for its configured interval.");
        Check(input.Update(true,true,26.2,1.2),"Custom hold fires at its configured boundary.");
        input.Update(true,false,27);
        Check(!input.Update(true,true,28,double.NaN) && input.Update(true,true,28.61,double.NaN),"Invalid duration falls back to the normal gesture.");
        input.Update(true,false,29);
        Check(!input.Update(true,true,30,-100) && !input.Update(true,true,30.29,-100) && input.Update(true,true,30.31,-100),"Negative duration clamps to a deliberate short hold.");
        Console.WriteLine("PASS " + checks + " menu-hold/debounce/room-boundary checks");
        return 0;
    }
}
