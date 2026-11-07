using System;

namespace Utilla.Compat
{
    public static class InfectionWireFormat
    {
        public const string Property = "org.legoandmars.utilla.mode";
        public const string Modded = "MODDED_Infection";

        public static bool IsPrivateMode(string value, string mode)
        {
            if (value == null) return false;
            string[] fields = value.Split(';');
            return fields.Length == 5 && fields[0] == "private" &&
                fields[2] == mode && fields[3].Length == 0 && fields[4].Length == 0;
        }

        public static string Encode(string value)
        {
            if (!IsPrivateMode(value, Modded)) return value;
            string[] fields = value.Split(';');
            fields[2] = "Infection";
            return string.Join(";", fields);
        }

        public static string Project(string value, object marker, bool isPrivate)
        {
            if (!isPrivate || !string.Equals(marker as string, Modded, StringComparison.Ordinal) ||
                !IsPrivateMode(value, "Infection")) return value;
            string[] fields = value.Split(';');
            fields[2] = Modded;
            return string.Join(";", fields);
        }
    }
}
