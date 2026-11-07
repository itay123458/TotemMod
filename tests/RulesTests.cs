using System;
using TotemMod;

internal static class RulesTests
{
    private static int checks;
    private const string Session = "0123456789abcdef0123456789abcdef";
    private const string NextSession = "fedcba9876543210fedcba9876543210";
    private static void Check(bool value, string label) { checks++; if (!value) throw new Exception(label); }
    private static void Reject(object payload, string label)
    {
        RoomRules decoded;
        Check(!RoomRules.TryDecode(payload, out decoded) && decoded == null, label);
    }
    private static void Throws(Action action, string label)
    {
        bool threw = false;
        try { action(); } catch (ArgumentException) { threw = true; }
        Check(threw, label);
    }
    private static object[] With(RoomRules rules, int index, object value)
    {
        object[] data = rules.Encode(); data[index] = value; return data;
    }
    public static int Main()
    {
        int[] capacities = { 5, 3, 5, 8, 20, 5 };
        double[] recharge = { 40, 60, 10, 30, 1, 40 };
        for (int i = 0; i < capacities.Length; i++)
        {
            RoomRules preset = RoomRules.ForPreset(i, Session, 7);
            Check(preset.Preset == i && preset.Capacity == capacities[i] && preset.Regeneration == recharge[i], "Preset stock and rate " + i);
            Check(preset.RoundSeconds == 180 && preset.Phase == 0 && preset.EndsAt == 0 && preset.Refill == 0, "Preset starts ready " + i);
            Check(preset.Protection && preset.AutoStart == (i == 1), "Preset flags " + i);
            Check(preset.Session == Session && preset.Revision == 7 && RoomRules.PresetName(i).Length > 0, "Preset identity " + i);
            RoomRules decoded;
            Check(RoomRules.TryDecode(preset.Encode(), out decoded) && decoded.Capacity == preset.Capacity && decoded.Revision == 7, "Preset round trip " + i);
        }
        RoomRules initial = RoomRules.ForPreset(1, Session, 0);
        RoomRules scrimStart = RoomRules.ForPreset(0,Session,0).StartConfigured(100);
        Check(scrimStart.Preset == 1 && scrimStart.Capacity == 3 && scrimStart.Regeneration == 60 && scrimStart.EndsAt == 280,"Starting a three-minute menu timer selects and starts full Scrims rules.");
        RoomRules customStart = RoomRules.ForPreset(2,Session,0).Change(roundSeconds:240).StartConfigured(100);
        Check(customStart.Preset == 5 && customStart.Regeneration == 10 && customStart.EndsAt == 340,"Other timer lengths preserve configured rules.");
        Check(scrimStart.Refill == 0,"Starting Scrims does not grant stock refills.");
        Check(initial.Remaining(50) == 180 && !initial.IsTimeCapped(50), "Ready has full time without deadline");
        RoomRules running = initial.Start(100);
        Check(running.Phase == 1 && running.EndsAt == 280 && running.Revision == 1, "Start uses shared clock");
        Check(running.Remaining(100) == 180 && running.Remaining(100.01) == 180, "Timer rounds up");
        Check(running.Remaining(279.999) == 1 && !running.IsTimeCapped(279.999), "Scrims has its last fractional second");
        Check(running.Remaining(280) == 0 && running.IsTimeCapped(280), "Scrims caps exactly at 180 seconds");
        Check(running.Remaining(281) == 0 && running.Remaining(90) == 180, "Timer clamps to valid range");
        RoomRules stopped = running.Stop(150);
        Check(stopped.Phase == 2 && stopped.Remaining(150) == 0 && stopped.IsTimeCapped(150), "Stop finishes immediately");
        RoomRules reset = stopped.ResetTimer();
        Check(reset.Phase == 0 && reset.EndsAt == 0 && reset.Remaining(500) == 180, "Reset clears deadline");
        RoomRules edited = running.Change(capacity: 8, regeneration: 20, protection: false, autoStart: false);
        Check(edited.Preset == 5 && edited.Revision == 2 && edited.Capacity == 8 && edited.Regeneration == 20 && !edited.Protection && !edited.AutoStart, "Editing creates custom rules");
        Check(edited.Phase == 1 && edited.EndsAt == 280, "Ordinary settings preserve timer");
        Check(running.Change(roundSeconds: 180).EndsAt == 280, "Unchanged duration keeps running timer");
        RoomRules duration = running.Change(roundSeconds: 240);
        Check(duration.Phase == 0 && duration.EndsAt == 0 && duration.Remaining(101) == 240, "Changing running duration resets ready");
        RoomRules refilled = running.RefillStock();
        Check(refilled.Refill == 1 && refilled.Revision == 2 && refilled.EndsAt == running.EndsAt && refilled.Phase == 1, "Explicit refill changes token without resetting clock");
        RoomRules selected = refilled.SelectPreset(2);
        Check(selected.Preset == 2 && selected.Phase == 0 && selected.EndsAt == 0 && !selected.AutoStart && selected.Refill == 1, "Preset selection preserves refill token");
        RoomRules rehosted = refilled.Rehost(NextSession);
        Check(rehosted.Session == NextSession && rehosted.Revision == 0 && rehosted.Refill == 1 && rehosted.EndsAt == 280 && rehosted.Phase == 1 && rehosted.Capacity == 3, "Host migration preserves running clock and refill token");
        RoomRules packet;
        Check(RoomRules.TryDecode(running.Encode(), out packet) && packet.Session == running.Session && packet.Revision == running.Revision, "Packet identity supports duplicate comparison");
        RoomRules older;
        Check(RoomRules.TryDecode(initial.Encode(), out older) && older.Session == packet.Session && older.Revision < packet.Revision, "Stale packet revision remains available for consumer rejection");
        Reject(null, "Null packet"); Reject(new object[11], "Wrong length"); Reject(new int[12], "Wrong array type");
        Reject(With(initial, 0, 2), "Unknown version"); Reject(With(initial, 0, 1L), "Version exact type");
        Reject(With(initial, 1, "not-a-session"), "Invalid session"); Reject(With(initial, 1, Guid.NewGuid().ToString()), "Hyphenated session");
        Reject(With(initial, 2, -1), "Negative revision"); Reject(With(initial, 3, 6), "Invalid preset");
        Reject(With(initial, 4, 0), "Capacity minimum"); Reject(With(initial, 4, 21), "Capacity maximum");
        Reject(With(initial, 5, 0.99), "Rate minimum"); Reject(With(initial, 5, 600.01), "Rate maximum");
        Reject(With(initial, 5, double.NaN), "NaN rate"); Reject(With(initial, 5, double.PositiveInfinity), "Infinite rate"); Reject(With(initial, 5, 40f), "Rate exact type");
        Reject(With(initial, 6, 29), "Duration minimum"); Reject(With(initial, 6, 601), "Duration maximum");
        Reject(With(initial, 7, 1), "Protection exact type"); Reject(With(initial, 8, "true"), "Autostart exact type");
        Reject(With(initial, 9, 3), "Phase maximum"); Reject(With(initial, 10, -1d), "Negative deadline");
        Reject(With(initial, 10, double.NaN), "NaN deadline"); Reject(With(initial, 10, double.PositiveInfinity), "Infinite deadline");
        Reject(With(initial, 10, 1e12 + 1), "Bounded deadline"); Reject(With(initial, 11, -1), "Negative refill token");
        for (int index = 0; index < 12; index++) Reject(With(initial, index, null), "Null field " + index);
        Throws(delegate { RoomRules.ForPreset(6, Session, 0); }, "Invalid preset construction");
        Throws(delegate { initial.Change(capacity: 21); }, "Invalid capacity edit");
        Throws(delegate { running.Start(double.NaN); }, "Invalid start clock");
        bool overflow = false;
        try { RoomRules.ForPreset(0, Session, int.MaxValue).Start(0); } catch (InvalidOperationException) { overflow = true; }
        Check(overflow, "Revision never wraps negative");

        TotemInventory stock = new TotemInventory();
        Check(stock.Capacity == 5 && stock.RechargeSeconds == 40 && stock.Count == 5, "Inventory retains default compatibility");
        stock.Configure(8, 40, 0);
        Check(stock.Count == 5 && stock.Capacity == 8 && stock.SecondsUntilNext(0) == 40, "Increasing full capacity starts recharge without refill");
        stock.Configure(8, 20, 10);
        Check(stock.Count == 5 && stock.SecondsUntilNext(10) == 15, "Rate change preserves proportional progress");
        Check(stock.Advance(24.999) == 0 && stock.Advance(25) == 1, "Proportional recharge reaches exact deadline");
        stock.Configure(8, 20, 30);
        Check(stock.SecondsUntilNext(30) == 15, "Unchanged configuration does not reset timer");
        stock.Configure(3, 20, 30);
        Check(stock.Count == 3 && stock.SecondsUntilNext(30) == 0, "Lower capacity clamps stock and stops full timer");
        stock.Configure(8, 20, 100);
        Check(stock.Count == 3 && stock.SecondsUntilNext(100) == 20, "Raising capacity never banks time spent full");
        stock.Refill();
        Check(stock.Count == 8 && stock.SecondsUntilNext(100) == 0 && stock.Advance(1000) == 0, "Explicit refill fills without banking charges");
        RoomRules guestBaseline = RoomRules.ForPreset(3,Session,0).Rehost("0123456789abcdef0123456789abcdef").ResetTimer();
        stock.Configure(guestBaseline.Capacity,guestBaseline.Regeneration,1000);
        Check(stock.Count == 8 && stock.Capacity == 8,"Guest rejoin preserves host capacity while awaiting current rules");
        RoomRules hostBaseline = RoomRules.ForPreset(3,Session,10);
        stock.Configure(hostBaseline.Capacity,hostBaseline.Regeneration,1000);
        Check(stock.Count == 8,"Receiving the same Casual host baseline does not lose charges");
        stock.Configure(8,20,1000);
        stock.Consume(1000); stock.Configure(8, 10, 1005);
        Check(stock.Count == 7 && stock.SecondsUntilNext(1005) == 8, "Rate adjustment rounds fractional remaining time up");
        stock.Configure(4, 10, 1005);
        Check(stock.Count == 4, "Lowering capacity clamps immediately");
        TotemInventory clock = new TotemInventory(); clock.Consume(0); clock.Consume(0);
        clock.Configure(10, 20, 60);
        Check(clock.Count == 4 && clock.SecondsUntilNext(60) == 10, "Configure advances old recharge before changing rate");
        clock.Configure(10, 20, 61);
        Check(clock.Count == 4 && clock.SecondsUntilNext(61) == 9, "Join-equivalent configure preserves stock and clock");
        Throws(delegate { clock.Configure(0, 20, 61); }, "Inventory capacity validation");
        Throws(delegate { clock.Configure(10, double.NaN, 61); }, "Inventory rate validation");
        Throws(delegate { clock.Configure(10, 20, double.NaN); }, "Inventory clock validation");
        Console.WriteLine("PASS " + checks + " room rules and configurable inventory checks");
        return 0;
    }
}
