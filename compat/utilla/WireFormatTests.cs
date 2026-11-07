using System;
using Utilla.Compat;

internal static class WireFormatTests
{
    private static int checks;
    private static void Equal(object actual, object expected, string scenario)
    {
        checks++;
        if (!object.Equals(actual, expected)) throw new Exception(scenario + ": " + actual);
    }

    public static void Main()
    {
        string legacy = "private;COMPETITIVE;MODDED_Infection;;";
        string wire = "private;COMPETITIVE;Infection;;";
        Equal(InfectionWireFormat.Encode(legacy), wire, "private opt-in encoding");
        // These are the exact vanilla parser/dictionary rules recovered from the installed game.
        Equal(InfectionWireFormat.Encode(legacy).Split(';')[2], "Infection", "vanilla exact gameType");
        Equal(InfectionWireFormat.Project(wire, InfectionWireFormat.Modded, true), legacy, "local gating projection");
        Equal(InfectionWireFormat.Project(wire, null, true), wire, "ordinary private room");
        Equal(InfectionWireFormat.Project(wire, InfectionWireFormat.Modded, false), wire, "public room rejects marker");
        Equal(InfectionWireFormat.Project(wire, "MODDED_Casual", true), wire, "unrelated marker");
        Equal(InfectionWireFormat.Project(wire, "modded_infection", true), wire, "case-sensitive marker");
        string[] untouched = { null, "", "private;COMPETITIVE;Infection;;", "forest;COMPETITIVE;MODDED_Infection;;",
            "private;COMPETITIVE;MODDED_Casual;;", "private;COMPETITIVE;MODDED_Infection;123;456",
            "private;COMPETITIVE;MODDED_Infection;;extra;", "private;COMPETITIVE;MODDED_Infection;", "MODDED_Infection" };
        foreach (string value in untouched) Equal(InfectionWireFormat.Encode(value), value, "preserve non-target format");
        string[] rejected = { "forest;COMPETITIVE;Infection;;", "private;COMPETITIVE;Casual;;", "private;COMPETITIVE;Infection;123;456", null, "private;COMPETITIVE;Infection;" };
        foreach (string value in rejected) Equal(InfectionWireFormat.Project(value, InfectionWireFormat.Modded, true), value, "reject false opt-in");
        Equal(InfectionWireFormat.Project(InfectionWireFormat.Project(wire, InfectionWireFormat.Modded, true), InfectionWireFormat.Modded, true), legacy, "idempotent projection");
        Console.WriteLine("PASS: " + checks + " wire-format and room-gating checks");
    }
}
