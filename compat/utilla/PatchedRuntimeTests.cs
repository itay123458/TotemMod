using System;
using System.IO;
using System.Reflection;

internal static class PatchedRuntimeTests
{
    private static int checks;
    private static void Equal(object actual, object expected)
    {
        checks++;
        if (!object.Equals(actual, expected)) throw new Exception("Patched payload returned unexpected value: " + actual);
    }
    public static void Main(string[] args)
    {
        // Executes only the injected pure string functions; no Unity/Photon/plugin type is initialized.
        Assembly assembly = Assembly.LoadFile(Path.GetFullPath(args[0]));
        Type format = assembly.GetType("Utilla.Compat.InfectionWireFormat", true);
        MethodInfo encode = format.GetMethod("Encode");
        MethodInfo project = format.GetMethod("Project");
        string legacy = "private;COMPETITIVE;MODDED_Infection;;";
        string wire = "private;COMPETITIVE;Infection;;";
        Equal(encode.Invoke(null, new object[] { legacy }), wire);
        Equal(project.Invoke(null, new object[] { wire, "MODDED_Infection", true }), legacy);
        Equal(project.Invoke(null, new object[] { wire, "MODDED_Infection", false }), wire);
        Equal(project.Invoke(null, new object[] { wire, null, true }), wire);
        foreach (string value in new[] { null, "", "forest;COMPETITIVE;MODDED_Infection;;", "private;COMPETITIVE;MODDED_Casual;;", "private;COMPETITIVE;MODDED_Infection;12;34" })
            Equal(encode.Invoke(null, new object[] { value }), value);
        if (args.Length > 1)
        {
            Assembly game = Assembly.LoadFile(Path.GetFullPath(args[1]));
            Type gameFormat = game.GetType("GorillaGameModes.GameModeString", true);
            MethodInfo contains = gameFormat.GetMethod("DoesPropertyStringContainGameMode");
            Equal(contains.Invoke(null, new object[] { legacy, "Infection" }), false);
            Equal(contains.Invoke(null, new object[] { encode.Invoke(null, new object[] { legacy }), "Infection" }), true);
            Equal(contains.Invoke(null, new object[] { wire, "Casual" }), false);
        }
        Console.WriteLine("PASS: " + checks + " actual patched-DLL and installed vanilla predicate execution checks");
    }
}
