using System;
using System.Reflection;
using TotemMod;

internal static class SurvivalStateTests
{
    private static int passed, failed;
    private static void Main()
    {
        Run("Inventory starter stacks and atomic consumption", InventoryConsumption);
        Run("Inventory selection bounds and wrapping", Selection);
        Run("Inventory click and close preserve all items", Conservation);
        Run("Inventory full cursor close is transactional", FullCursor);
        Run("Inventory merge caps at 64 without loss", Merge);
        Run("Damage immunity and death clock", DamageDeath);
        Run("Hunger exhaustion and starvation floor", HungerStarvation);
        Run("Food permissions and restoration", Food);
        Run("Natural regeneration spends exhaustion", NaturalRegeneration);
        Run("Totem save immunity and bounded recovery", Totem);
        Run("Nonfatal totem pop preserves current health", NonfatalTotem);
        Run("Invalid clocks and inactive time do not catch up", Clock);
        Run("Falling damages once after stable landing", Falling);
        Run("Descending wall hand contact keeps fall", WallContact);
        Run("Falling scale, new ascent and short drop", FallScale);
        Run("Fall teleport, stale and one-frame zero samples", FallOutliers);
        Run("Invalid fall samples discard old descent", InvalidFallSamples);
        Run("Supported slow steps do not bank fall damage", SupportedSteps);
        Run("Fast grounded stairs never bank fall damage", GroundedStairs);
        Run("Hard floor landing counts before vertical motion stops", HardFloorLanding);
        Console.WriteLine("Survival tests: " + passed + " passed, " + failed + " failed");
        if (failed != 0) Environment.Exit(1);
    }
    private static void Run(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e.Message); }
    }
    private static void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); }
    private static void Equal(int want, int actual, string reason) { Check(want == actual, reason + ": expected " + want + ", got " + actual); }
    private static void InventoryConsumption()
    {
        MinecraftInventory x = new MinecraftInventory();
        Equal(8, x.CountAll(CraftItem.Apple), "starter apples");
        Equal(8, x.CountAll(CraftItem.Bread), "starter bread");
        Equal(2, x.CountAll(CraftItem.GoldenApple), "starter golden apples");
        Check(!x.ConsumeSelected(CraftItem.Bread), "wrong kind cannot consume");
        Equal(8, x.Get(0).Count, "wrong kind leaves stack");
        Check(x.ConsumeSelected(CraftItem.Apple), "selected apple consumed");
        Equal(7, x.Get(0).Count, "one item consumed");
        for (int i = 0; i < 7; i++) Check(x.ConsumeSelected(CraftItem.Apple), "remaining apple consumed");
        Check(!x.ConsumeSelected(CraftItem.Apple), "empty cannot consume");
        Check(x.Get(0).Item == CraftItem.None, "empty stack clears kind");
        x.Reset(); Equal(8, x.CountAll(CraftItem.Apple), "reset restores starter");
        Check(x.Revision > 0, "changes invalidate UI");
    }
    private static void Selection()
    {
        MinecraftInventory x = new MinecraftInventory();
        x.Select(8); Equal(8, x.Selected, "select last hotbar");
        x.Select(9); x.Select(-1); Equal(8, x.Selected, "invalid selects ignored");
        x.Cycle(1); Equal(0, x.Selected, "wrap forward");
        x.Cycle(-1); Equal(8, x.Selected, "wrap backward");
        x.Cycle(0); Equal(8, x.Selected, "zero unchanged");
        Equal(0, x.Get(-1).Count, "invalid read empty"); Equal(0, x.Get(36).Count, "out-of-range read empty");
        Check(!x.Click(-1) && !x.Click(36), "invalid click safe");
    }
    private static void Conservation()
    {
        MinecraftInventory x = new MinecraftInventory();
        Check(x.Click(0), "pickup"); Equal(8, x.Cursor.Count, "cursor holds apples");
        Equal(8, x.CountAll(CraftItem.Apple), "cursor counted");
        Check(!x.ConsumeSelected(CraftItem.Apple), "cursor cannot be consumed as selected slot");
        Check(x.ReturnCursor(), "close returns cursor"); Equal(8, x.Get(0).Count, "original slot restored");
        Random r = new Random(417);
        for (int i = 0; i < 10000; i++)
        {
            x.Click(r.Next(36));
            if (i % 17 == 0) Check(x.ReturnCursor(), "close has space");
            Equal(8, x.CountAll(CraftItem.Apple), "apples conserved");
            Equal(8, x.CountAll(CraftItem.Bread), "bread conserved");
            Equal(2, x.CountAll(CraftItem.GoldenApple), "golden apples conserved");
        }
    }
    private static CraftStack[] Slots(MinecraftInventory x)
    {
        foreach (FieldInfo f in typeof(MinecraftInventory).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
            if (f.FieldType == typeof(CraftStack[])) return (CraftStack[])f.GetValue(x);
        throw new Exception("inventory storage unavailable for full-capacity fixture");
    }
    private static void FullCursor()
    {
        MinecraftInventory x = new MinecraftInventory();
        Check(x.Click(0), "pickup apples");
        CraftStack[] slots = Slots(x);
        for (int i = 0; i < slots.Length; i++) slots[i] = new CraftStack(CraftItem.Bread, 64);
        Check(!x.ReturnCursor(), "full close fails safely");
        Equal(8, x.Cursor.Count, "cursor not destroyed"); Equal(2304, x.CountAll(CraftItem.Bread), "full slots untouched");
        slots[7] = new CraftStack(CraftItem.Apple, 60);
        Check(!x.ReturnCursor(), "insufficient merge capacity fails atomically");
        Equal(60, x.Get(7).Count, "failed close performs no partial merge"); Equal(8, x.Cursor.Count, "full cursor retained");
        slots[12] = new CraftStack();
        Check(x.ReturnCursor(), "available capacity closes");
        Equal(68, x.CountAll(CraftItem.Apple), "merge plus empty conserves apples"); Equal(0, x.Cursor.Count, "cursor returned");
    }
    private static void Merge()
    {
        MinecraftInventory x = new MinecraftInventory();
        Slots(x)[3] = new CraftStack(CraftItem.Apple, 60);
        Check(x.Click(0) && x.Click(3), "pickup and merge");
        Equal(64, x.Get(3).Count, "stack cap"); Equal(4, x.Cursor.Count, "overflow retained");
        Check(!x.Click(3), "full same-kind stack unchanged");
        Check(x.Click(1), "swap with bread");
        Check(x.Cursor.Item == CraftItem.Bread, "swap cursor identity"); Equal(4, x.Get(1).Count, "swap retains apples");
        Check(x.ReturnCursor(), "return swapped stack");
        Equal(68, x.CountAll(CraftItem.Apple), "merge conservation"); Equal(8, x.CountAll(CraftItem.Bread), "swap conservation");
    }
    private static void DamageDeath()
    {
        SurvivalState x = new SurvivalState(); x.Reset(0);
        Equal(20, x.Health, "full health"); Equal(20, x.Hunger, "full hunger");
        Check(x.Damage(4, 1), "hit accepted"); Equal(16, x.Health, "health decreases");
        Check(!x.Damage(5, 1.49), "immunity rejects rapid hit"); Equal(16, x.Health, "immune health unchanged");
        Check(x.Damage(30, 1.5), "fatal hit after immunity"); Equal(0, x.Health, "death health zero"); Check(x.Dead, "dead state");
        Check(Math.Abs(x.DeathUntil - 4.5) < .000001, "death delay three seconds");
        Check(!x.RespawnReady(4.49) && x.RespawnReady(4.5), "respawn boundary");
        x.Advance(4, 1000); Check(!x.Eat(CraftItem.GoldenApple, 4), "dead cannot eat"); Equal(20, x.Hunger, "dead food frozen");
        x.Reset(4.5); Check(!x.Dead && !x.RespawnReady(100), "reset clears death"); Equal(20, x.Health, "reset heals");
    }
    private static void HungerStarvation()
    {
        SurvivalState x = new SurvivalState(); x.Reset(0); x.Advance(1, 300);
        Equal(19, x.Hunger, "movement burns saturation before hunger");
        x.Reset(0); x.Advance(1, 1250); Equal(0, x.Hunger, "exhaustion empties food");
        for (int i = 2; i <= 5; i++) x.Advance(i, 0);
        Equal(19, x.Health, "starvation damages after four seconds");
        for (int i = 6; i < 200; i++) x.Advance(i, 0);
        Equal(1, x.Health, "starvation cannot kill"); Check(!x.Dead, "starvation alive");
    }
    private static void Food()
    {
        SurvivalState x = new SurvivalState(); x.Reset(0);
        Check(!x.CanEat(CraftItem.Apple) && !x.Eat(CraftItem.Bread, 0), "ordinary food cannot waste at full");
        Check(!x.CanEat(CraftItem.None), "empty item not edible");
        Check(x.Damage(8, 0), "damage before golden apple");
        Check(x.Eat(CraftItem.GoldenApple, .1), "golden apple edible at full food"); Equal(16, x.Health, "golden apple heals four");
        x.Reset(0); x.Advance(1, 700); Equal(11, x.Hunger, "fixture food level");
        Check(x.Eat(CraftItem.Apple, 1), "apple edible"); Equal(15, x.Hunger, "apple restores four");
        Check(x.Eat(CraftItem.Bread, 1), "bread edible"); Equal(20, x.Hunger, "bread restores five");
        Check(x.Revision > 0, "survival invalidates UI");
    }
    private static void NaturalRegeneration()
    {
        SurvivalState x = new SurvivalState(); x.Reset(0); x.Damage(6, 0);
        for (int i = 1; i <= 3; i++) x.Advance(i, 0);
        Equal(14, x.Health, "regen waits four seconds"); x.Advance(4, 0); Equal(15, x.Health, "regen heals one");
        for (int i = 5; i <= 24; i++) x.Advance(i, 0);
        Check(x.Hunger < 20, "healing spends food exhaustion");
    }
    private static void Totem()
    {
        SurvivalState x = new SurvivalState(); x.Reset(0); x.Damage(20, 0); x.TotemSave(0);
        Equal(1, x.Health, "totem one heart point"); Check(!x.Dead, "totem clears death");
        Check(!x.Damage(3, 1.99), "two-second immunity");
        x.Advance(1, 0); Check(x.Health > 1, "totem regenerates");
        Check(x.Damage(1, 2), "immunity expires");
        for (int i = 2; i <= 21; i++) x.Advance(i, 0);
        x.Damage(6, 22); int before = x.Health; x.Advance(23, 0);
        Equal(before, x.Health, "totem regeneration expires");
    }
    private static void Clock()
    {
        SurvivalState x = new SurvivalState(); x.Reset(10); x.Damage(6, 10);
        x.Advance(1000, 0); Equal(14, x.Health, "large time jump capped");
        x.Advance(1001, 10000, false); Equal(20, x.Hunger, "inactive ignores movement");
        x.Advance(999, 5000); x.Advance(double.NaN, 5000); x.Advance(double.PositiveInfinity, 5000);
        Equal(20, x.Hunger, "invalid times no mutation"); Check(!x.Damage(3, 998), "past damage rejected");
        Check(!x.Damage(-2, 1002) && !x.Damage(2, double.NaN), "invalid damage safe");
        x.Advance(1002, double.NaN); x.Advance(1003, -10000); Equal(20, x.Hunger, "invalid movement cannot add hunger exhaustion");
    }
    private static void NonfatalTotem()
    {
        SurvivalState x = new SurvivalState(); x.Reset(0); x.TotemSave(1);
        Equal(20, x.Health, "native tag totem cannot lower full health");
        Check(!x.Damage(2, 2.99), "nonfatal pop grants immunity");
        x.Reset(0); Check(x.Damage(12, 0), "damaged health fixture"); x.TotemSave(1);
        Equal(8, x.Health, "native tag totem preserves damaged health");
        x.Advance(2, 0); Check(x.Health > 8, "nonfatal pop grants regeneration");
    }
    private static void Falling()
    {
        SurvivalFall f = new SurvivalFall(); f.Sample(10, 0, true);
        int total = Drop(f, 10, 4, 0, false, 1); Equal(0, total, "no damage airborne");
        Equal(3, f.Sample(4, 1.01, true), "six meter drop damages three");
        Equal(0, f.Sample(4, 1.02, true), "landing counted once");
        Equal(0, f.Sample(4, 1.03, true), "standing no banked fall");
    }
    private static int Drop(SurvivalFall f, float from, float to, double start, bool contact, float scale)
    {
        int total = 0;
        for (int i = 1; i <= 90; i++) total += f.Sample(from + (to - from) * i / 90f, start + i / 90.0, contact, scale);
        return total;
    }
    private static void WallContact()
    {
        SurvivalFall f = new SurvivalFall(); f.Sample(10, 0, true);
        Equal(0, Drop(f, 10, 4, 0, true, 1), "descending hand contact not landing");
        Equal(3, f.Sample(4, 1.02, true), "wall slide retained descent until stopped");
        Equal(0, f.Sample(4, 1.04, true), "wall slide landing only once");
    }
    private static void FallScale()
    {
        SurvivalFall f = new SurvivalFall(); f.Sample(20, 0, true, 2); Drop(f, 20, 8, 0, false, 2);
        Equal(3, f.Sample(8, 1.02, true, 2), "scale-normalized fall");
        f.Reset(); f.Sample(10, 0, true); Drop(f, 10, 8, 0, false, 1);
        Equal(0, f.Sample(8, 1.02, true), "short drop safe");
        f.Reset(); f.Sample(3, 0, false); Drop(f, 3, 10, 0, false, 1); Drop(f, 10, 4, 1, false, 1);
        Equal(3, f.Sample(4, 2.02, true), "new ascent tracks apex");
    }
    private static void FallOutliers()
    {
        SurvivalFall f = new SurvivalFall(); f.Sample(20, 0, true);
        Equal(0, f.Sample(-20, .01, true), "teleport ignored"); Equal(0, f.Sample(-20, .02, true), "teleport clears fall");
        f.Reset(); f.Sample(10, 0, true); f.Sample(4, 5, true); Equal(0, f.Sample(4, 5.02, true), "stale gap clears fall");
        f.Reset(); f.Sample(10, 0, true); Drop(f, 10, 5, 0, false, 1);
        Equal(0, f.Sample(0, 1.01, false), "one zero position sample ignored");
        Equal(0, f.Sample(5, 1.02, false), "zero recovery safe"); Equal(2, f.Sample(5, 1.04, true), "real fall survives transient zero");
        f.Reset(); f.Sample(10, 0, true); Drop(f, 10, 0, 0, false, 1);
        Equal(7, f.Sample(0, 1.02, true), "real zero height landing works");
        Equal(0, f.Sample(float.NaN, 1.03, true), "invalid position safe");
    }
    private static void InvalidFallSamples()
    {
        SurvivalFall f = new SurvivalFall(); f.Sample(10, 0, true); Drop(f, 10, 4, 0, false, 1);
        f.Sample(float.NaN, 1.01, false);
        Equal(0, f.Sample(4, 1.02, true), "invalid height discards previous fall debt");
        f.Reset(); f.Sample(10, 0, true); Drop(f, 10, 4, 0, false, 1);
        f.Sample(4, double.PositiveInfinity, false);
        Equal(0, f.Sample(4, 1.02, true), "invalid time discards previous fall debt");
    }
    private static void SupportedSteps()
    {
        SurvivalFall f = new SurvivalFall(); f.Sample(10, 0, true);
        for (int i = 1; i <= 1200; i++)
            Equal(0, f.Sample(10 - i / 180f, i / 90.0, true), "slow supported descent never accumulates fall damage");
        Equal(0, f.Sample(10 - 1200 / 180f, 1201 / 90.0, true), "stopping supported descent is safe");
    }
    private static void GroundedStairs()
    {
        SurvivalFall f = new SurvivalFall(); f.Sample(10, 0, true, 1, true);
        for (int i = 1; i <= 90; i++)
            Equal(0, f.Sample(10 - 6 * i / 90f, i / 90.0, true, 1, true), "fast grounded step safe");
        Equal(0, f.Sample(4, 1.02, true, 1, true), "grounded stair stop has no fall debt");
    }
    private static void HardFloorLanding()
    {
        SurvivalFall f = new SurvivalFall(); f.Sample(10, 0, true, 1, true);
        for (int i = 1; i < 90; i++)
            Equal(0, f.Sample(10 - 6 * i / 90f, i / 90.0, false), "airborne drop does not damage");
        Equal(3, f.Sample(4, 1, true, 1, true), "hard floor landing counts immediately during downward motion");
        Equal(0, f.Sample(4, 1.02, true, 1, true), "hard floor landing counts once");
    }
}
