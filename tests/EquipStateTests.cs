using System;
using TotemMod;

internal static class EquipStateTests
{
    private static int checks;
    private static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new Exception(message);
    }
    public static int Main()
    {
        EquipState s = new EquipState();
        Check(!s.Equipped && !s.Consume(), "Hidden state ignores tag events.");
        s.Update(true, true, true);
        Check(s.Equipped, "One Y press equips.");
        s.Update(true, false, true);
        Check(s.Equipped, "Releasing Y keeps the totem equipped.");
        s.Update(true, true, true);
        Check(!s.Equipped, "A second Y press unequips.");
        Check(!s.Consume(), "An unequipped totem cannot pop.");
        s.Update(true, false, true); s.Update(true, true, true);
        Check(s.Equipped, "A third press equips again.");
        Check(s.Consume(), "First tag consumes the equipped totem.");
        Check(!s.Consume(), "Duplicate broadcasts cannot replay the pop.");
        s.Update(true, true, true);
        Check(!s.Equipped, "Held Y cannot respawn a consumed totem.");
        s.Update(true, false, true); s.Update(true, true, true);
        Check(s.Equipped, "A new press after a pop equips another.");
        s.Consume(); s.Update(true, false, false); s.Update(true, true, false);
        Check(!s.Equipped, "An empty supply cannot equip.");
        s.Update(true, true, true);
        Check(!s.Equipped, "Regeneration while Y is held cannot auto-equip.");
        s.Update(true, false, true); s.Update(true, true, true);
        Check(s.Consume(), "A regenerated and newly equipped totem is consumable.");
        s.Update(true, true, true);
        Check(!s.Equipped, "Held toggle input cannot replay after consuming.");
        s.Update(true, false, true); s.Update(true, true, true);
        Check(s.Equipped, "Fresh toggle press rearms.");
        s.Update(false, true, true);
        Check(!s.Equipped && !s.Consume(), "Disallowed rooms clear the totem.");
        s.Update(true, true, true);
        Check(!s.Equipped, "Crossing an allowed-room boundary requires release.");
        s.Update(true, false, true); s.Update(true, true, true);
        Check(s.Equipped, "Input works again after release at room boundary.");
        s.Reset();
        Check(!s.Equipped, "Disabling cleans up state.");
        s.Update(true,true,true);
        s.Toggle(true,true);
        Check(s.Equipped,"Dev action equips independently of a held physical Y.");
        s.Update(true,true,true);
        Check(s.Equipped,"Dev equip preserves the physical Y edge latch.");
        s.Toggle(true,true);
        Check(!s.Equipped,"Dev action unequips without spending a totem.");
        Check(s.Toggle(true,false) && !s.Equipped,"Dev equip reports an empty supply.");
        s.Toggle(true,true); s.Toggle(false,true);
        Check(!s.Equipped,"Dev action cannot equip outside the allowed room.");
        Console.WriteLine("PASS " + checks + " press-once/empty/refill/room-boundary equip checks");
        return 0;
    }
}
