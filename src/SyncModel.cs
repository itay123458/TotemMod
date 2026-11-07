using System;

namespace TotemMod
{
    public static class RoomAccess
    {
        public static bool Allows(bool inRoom, bool hasRoom, bool modded)
        {
            return inRoom && hasRoom && modded;
        }
    }

    public sealed class TotemSnapshot
    {
        public const string PropertyKey = "totemmod.gtag.rhys/state/v2";
        public readonly string Session;
        public readonly bool Equipped;
        public readonly int Pops;
        public readonly float Size;

        public TotemSnapshot(string session, bool equipped, int pops, float size)
        {
            Session = session; Equipped = equipped; Pops = pops; Size = size;
        }

        public object[] Encode() { return new object[] { 2, Session, Equipped, Pops, Size }; }

        public static bool TryDecode(object value, out TotemSnapshot snapshot)
        {
            snapshot = null;
            object[] data = value as object[];
            if (data == null || data.Length != 5 || !(data[0] is int) || (int)data[0] != 2 ||
                !(data[1] is string) || !(data[2] is bool) || !(data[3] is int) || !(data[4] is float)) return false;
            string session = (string)data[1];
            Guid parsed;
            float size = (float)data[4];
            int pops = (int)data[3];
            if (session.Length != 32 || !Guid.TryParseExact(session, "N", out parsed) || pops < 0 ||
                float.IsNaN(size) || float.IsInfinity(size) || size < 0.25f || size > 3f) return false;
            snapshot = new TotemSnapshot(session, (bool)data[2], pops, size);
            return true;
        }
    }

    public sealed class RemoteState
    {
        public TotemSnapshot Snapshot { get; private set; }

        // The first snapshot, a new plugin session, and late joins establish a
        // baseline. They show a held totem but never replay an old pop sound.
        public bool Apply(TotemSnapshot next)
        {
            TotemSnapshot previous = Snapshot;
            if (previous != null && previous.Session == next.Session && next.Pops < previous.Pops) return false;
            Snapshot = next;
            return previous != null && previous.Session == next.Session && next.Pops > previous.Pops;
        }
    }

    public sealed class PopDeduplicator
    {
        private string lastSession;
        private int lastCounter = -1;
        public bool Accept(string session, int counter)
        {
            if (session == lastSession && counter <= lastCounter) return false;
            lastSession = session; lastCounter = counter;
            return true;
        }
    }
}
