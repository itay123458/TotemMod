using System;

namespace TotemMod
{
    public enum DeveloperCommandKind
    {
        Preset, Capacity, Regen, RoundSeconds, Protection, AutoStart,
        StartTimer, StopTimer, ResetTimer, RefillAll, StartScrim
    }

    // A closed set of private-room operations; unused fields must be zero.
    public sealed class DeveloperCommand
    {
        public readonly DeveloperCommandKind Kind;
        public readonly int Value;
        public readonly float Number;

        public DeveloperCommand(DeveloperCommandKind kind, int value = 0, float number = 0f)
        {
            if (!Valid(kind, value, number)) throw new ArgumentException("Invalid developer command.");
            Kind = kind; Value = value; Number = number;
        }

        public object[] Encode() { return new object[] { 1, (int)Kind, Value, Number }; }

        public static bool TryDecode(object payload, out DeveloperCommand command)
        {
            command = null;
            object[] data = payload as object[];
            if (data == null || data.Length != 4 || !(data[0] is int) || (int)data[0] != 1 ||
                !(data[1] is int) || !(data[2] is int) || !(data[3] is float)) return false;
            DeveloperCommandKind kind = (DeveloperCommandKind)(int)data[1];
            if (!Valid(kind, (int)data[2], (float)data[3])) return false;
            command = new DeveloperCommand(kind, (int)data[2], (float)data[3]);
            return true;
        }

        private static bool Valid(DeveloperCommandKind kind, int value, float number)
        {
            if (float.IsNaN(number) || float.IsInfinity(number)) return false;
            switch (kind)
            {
                case DeveloperCommandKind.Preset: return value >= 0 && value <= 5 && number == 0f;
                case DeveloperCommandKind.Capacity: return value >= 1 && value <= 20 && number == 0f;
                case DeveloperCommandKind.Regen: return value == 0 && number >= 1f && number <= 600f;
                case DeveloperCommandKind.RoundSeconds: return value >= 30 && value <= 600 && number == 0f;
                case DeveloperCommandKind.Protection: case DeveloperCommandKind.AutoStart: return (value == 0 || value == 1) && number == 0f;
                case DeveloperCommandKind.StartTimer: case DeveloperCommandKind.StopTimer:
                case DeveloperCommandKind.ResetTimer: case DeveloperCommandKind.RefillAll:
                case DeveloperCommandKind.StartScrim: return value == 0 && number == 0f;
                default: return false;
            }
        }
    }
}
