using System;
using TotemMod;

internal static class DeveloperCommandTests
{
    private static int checks;
    private static void Check(bool value, string label) { checks++; if (!value) throw new Exception(label); }
    public static int Run()
    {
        DeveloperCommand command;
        object[][] valid = {
            new object[] { 1, 0, 0, 0f }, new object[] { 1, 0, 5, 0f },
            new object[] { 1, 1, 1, 0f }, new object[] { 1, 1, 20, 0f },
            new object[] { 1, 2, 0, 1f }, new object[] { 1, 2, 0, 600f },
            new object[] { 1, 3, 30, 0f }, new object[] { 1, 3, 600, 0f },
            new object[] { 1, 4, 0, 0f }, new object[] { 1, 4, 1, 0f },
            new object[] { 1, 5, 0, 0f }, new object[] { 1, 5, 1, 0f },
            new object[] { 1, 6, 0, 0f }, new object[] { 1, 7, 0, 0f },
            new object[] { 1, 8, 0, 0f }, new object[] { 1, 9, 0, 0f }, new object[] { 1, 10, 0, 0f }
        };
        foreach (object[] data in valid)
        {
            Check(DeveloperCommand.TryDecode(data,out command),"Bounded command schema accepts valid boundary");
            Check((int)command.Kind == (int)data[1] && command.Value == (int)data[2] && command.Number == (float)data[3],"Decoded command preserves typed values");
            DeveloperCommand copy;
            Check(DeveloperCommand.TryDecode(command.Encode(),out copy),"Command encodes supported primitive packet");
        }
        object[] invalid = { null, "code", new object[] {1,0,0}, new object[] {2,0,0,0f},
            new object[] {1,11,0,0f}, new object[] {1,-1,0,0f}, new object[] {1,0,6,0f},
            new object[] {1,1,0,0f}, new object[] {1,1,21,0f}, new object[] {1,2,0,0.99f},
            new object[] {1,2,0,601f}, new object[] {1,2,0,float.NaN}, new object[] {1,2,0,float.PositiveInfinity},
            new object[] {1,2,0,1.0}, new object[] {1,3,29,0f}, new object[] {1,3,601,0f},
            new object[] {1,4,2,0f}, new object[] {1,5,-1,0f}, new object[] {1,6,1,0f},
            new object[] {1,7,0,1f}, new object[] {1,0,0,1f}, new object[] {1,2,1,1f},
            new object[] {1,0,0L,0f}, new object[] {1,0L,0,0f}
        };
        foreach (object data in invalid) Check(!DeveloperCommand.TryDecode(data,out command),"Malformed, unbounded, or mixed command payload rejected");
        bool threw = false;
        try { new DeveloperCommand(DeveloperCommandKind.Capacity, 21); } catch (ArgumentException) { threw = true; }
        Check(threw,"Invalid locally constructed command cannot enter protocol");
        return checks;
    }
}
