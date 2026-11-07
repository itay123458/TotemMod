using System;
using TotemMod;

internal static class InventoryTests
{
    private static int checks;
    private static void Check(bool value, string label) { checks++; if (!value) throw new Exception(label); }
    public static int Main()
    {
        TotemInventory stock = new TotemInventory();
        Check(stock.Count == 5, "Start with exactly five.");
        for (int i=0; i<5; i++) Check(stock.Consume(i), "Five real pops can consume five charges.");
        Check(stock.Count == 0 && !stock.Consume(5), "Sixth pop is impossible.");
        Check(stock.SecondsUntilNext(5) == 35, "Later pops do not restart the first regen timer.");
        Check(stock.Advance(39.999) == 0 && stock.Count == 0, "Cannot regenerate early.");
        Check(stock.Advance(40) == 1 && stock.Count == 1, "One charge regenerates at 40 seconds.");
        Check(stock.Advance(40) == 0, "Same frame cannot refill twice.");
        Check(stock.Consume(41) && stock.Count == 0, "Regenerated charge can be used.");
        Check(stock.SecondsUntilNext(41) == 39, "Consuming a regen does not restart the active recharge.");
        Check(stock.Advance(160) == 3 && stock.Count == 3, "Frame stalls catch up each 40-second charge.");
        Check(stock.Advance(1000) == 2 && stock.Count == 5, "Capacity is capped at five.");
        Check(stock.Advance(10000) == 0 && stock.Count == 5, "Full inventory never banks extra charges.");
        Check(stock.Consume(10000) && stock.SecondsUntilNext(10000) == 40, "Full-to-used starts a fresh 40-second timer.");
        Check(!stock.Consume(double.NaN) && !stock.Consume(double.PositiveInfinity), "Invalid times cannot spend charges.");
        EquipState equip = new EquipState();
        TotemInventory flow = new TotemInventory();
        equip.Update(true, true, true); equip.Update(true, false, true); equip.Update(true, true, true);
        Check(!equip.Equipped && flow.Count == 5 && flow.SecondsUntilNext(0) == 0, "Unequipping neither spends a charge nor starts recharge.");
        equip.Update(true, false, true);
        for (int i=0; i<5; i++)
        {
            equip.Update(true, false, flow.Count > 0); equip.Update(true, true, flow.Count > 0);
            Check(equip.Equipped, "Press equips an available charge.");
            equip.Update(true, false, flow.Count > 0);
            Check(equip.Equipped, "Release does not unequip.");
            Check(equip.Consume() && flow.Consume(0), "Each pop spends exactly one charge.");
        }
        Check(equip.Update(true, true, flow.Count > 0) && !equip.Equipped, "Empty press produces the out-of-totems notification signal.");
        flow.Advance(40); equip.Update(true, true, flow.Count > 0);
        Check(!equip.Equipped, "Refill does not auto-equip while Y remains held.");
        equip.Update(true, false, true); equip.Update(true, true, true);
        Check(equip.Equipped && flow.Count == 1, "Fresh press equips without spending until a pop.");
        equip.Update(false, false, true); equip.Reset();
        Check(flow.Count == 1, "Room changes do not refill the local supply.");
        Console.WriteLine("PASS " + checks + " five-charge/40-second/empty-notice/press-once inventory checks");
        return 0;
    }
}
