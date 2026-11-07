using System;

namespace TotemMod
{
    // Only primitive values cross the room-property boundary.
    public sealed class RoomRules
    {
        public const string PropertyKey = "totemmod.gtag.rhys/rules/v1";
        private const double MaximumClock = 1e12;
        public readonly string Session;
        public readonly int Revision, Preset, Capacity, RoundSeconds, Phase, Refill;
        public readonly double Regeneration, EndsAt;
        public readonly bool Protection, AutoStart;

        public RoomRules(string session, int revision, int preset, int capacity, double regeneration,
            int roundSeconds, bool protection, bool autoStart, int phase, double endsAt, int refill)
        {
            if (!Valid(session, revision, preset, capacity, regeneration, roundSeconds, phase, endsAt, refill))
                throw new ArgumentException("Invalid room rules.");
            Session = session; Revision = revision; Preset = preset; Capacity = capacity;
            Regeneration = regeneration; RoundSeconds = roundSeconds; Protection = protection;
            AutoStart = autoStart; Phase = phase; EndsAt = endsAt; Refill = refill;
        }

        public static RoomRules ForPreset(int preset, string session, int revision)
        {
            int capacity = 5;
            double regeneration = 40;
            switch (preset)
            {
                case 0: case 5: break;
                case 1: capacity = 3; regeneration = 60; break;
                case 2: regeneration = 10; break;
                case 3: capacity = 8; regeneration = 30; break;
                case 4: capacity = 20; regeneration = 1; break;
                default: throw new ArgumentOutOfRangeException("preset");
            }
            return new RoomRules(session, revision, preset, capacity, regeneration, 180, true, preset == 1, 0, 0, 0);
        }

        public static string PresetName(int preset)
        {
            switch (preset)
            {
                case 0: return "Default";
                case 1: return "Scrims";
                case 2: return "Filming";
                case 3: return "Casual";
                case 4: return "Sandbox";
                case 5: return "Custom";
                default: throw new ArgumentOutOfRangeException("preset");
            }
        }

        public RoomRules SelectPreset(int preset)
        {
            RoomRules selected = ForPreset(preset, Session, Increment(Revision));
            return new RoomRules(Session, selected.Revision, preset, selected.Capacity, selected.Regeneration,
                selected.RoundSeconds, selected.Protection, selected.AutoStart, 0, 0, Refill);
        }

        public RoomRules Change(int? capacity = null, double? regeneration = null, int? roundSeconds = null,
            bool? protection = null, bool? autoStart = null)
        {
            bool reset = Phase == 1 && roundSeconds.HasValue && roundSeconds.Value != RoundSeconds;
            return new RoomRules(Session, Increment(Revision), 5, capacity ?? Capacity, regeneration ?? Regeneration,
                roundSeconds ?? RoundSeconds, protection ?? Protection, autoStart ?? AutoStart,
                reset ? 0 : Phase, reset ? 0 : EndsAt, Refill);
        }

        public RoomRules Start(double now)
        {
            ValidateClock(now);
            return new RoomRules(Session, Increment(Revision), Preset, Capacity, Regeneration,
                RoundSeconds, Protection, AutoStart, 1, now + RoundSeconds, Refill);
        }

        public RoomRules StartConfigured(double now)
        {
            return (RoundSeconds == 180 && Preset != 1 ? SelectPreset(1) : this).Start(now);
        }

        public RoomRules Stop(double now)
        {
            ValidateClock(now);
            return new RoomRules(Session, Increment(Revision), Preset, Capacity, Regeneration,
                RoundSeconds, Protection, AutoStart, 2, EndsAt, Refill);
        }

        public RoomRules ResetTimer()
        {
            return new RoomRules(Session, Increment(Revision), Preset, Capacity, Regeneration,
                RoundSeconds, Protection, AutoStart, 0, 0, Refill);
        }

        public RoomRules RefillStock()
        {
            return new RoomRules(Session, Increment(Revision), Preset, Capacity, Regeneration,
                RoundSeconds, Protection, AutoStart, Phase, EndsAt, Increment(Refill));
        }

        public RoomRules Rehost(string session)
        {
            return new RoomRules(session, 0, Preset, Capacity, Regeneration,
                RoundSeconds, Protection, AutoStart, Phase, EndsAt, Refill);
        }

        public int Remaining(double now)
        {
            if (Phase == 0) return RoundSeconds;
            if (Phase == 2 || !Finite(now)) return 0;
            return (int)Math.Max(0, Math.Min(RoundSeconds, Math.Ceiling(EndsAt - now)));
        }

        public bool IsTimeCapped(double now)
        {
            return Phase == 2 || (Phase == 1 && Finite(now) && now >= EndsAt);
        }

        public object[] Encode()
        {
            return new object[] { 1, Session, Revision, Preset, Capacity, Regeneration, RoundSeconds,
                Protection, AutoStart, Phase, EndsAt, Refill };
        }

        // Freshness is evaluated by the room consumer using Session and Revision.
        public static bool TryDecode(object payload, out RoomRules rules)
        {
            rules = null;
            object[] data = payload as object[];
            if (data == null || data.Length != 12 || !(data[0] is int) || (int)data[0] != 1 ||
                !(data[1] is string) || !(data[2] is int) || !(data[3] is int) || !(data[4] is int) ||
                !(data[5] is double) || !(data[6] is int) || !(data[7] is bool) || !(data[8] is bool) ||
                !(data[9] is int) || !(data[10] is double) || !(data[11] is int)) return false;
            if (!Valid((string)data[1], (int)data[2], (int)data[3], (int)data[4], (double)data[5],
                (int)data[6], (int)data[9], (double)data[10], (int)data[11])) return false;
            rules = new RoomRules((string)data[1], (int)data[2], (int)data[3], (int)data[4], (double)data[5],
                (int)data[6], (bool)data[7], (bool)data[8], (int)data[9], (double)data[10], (int)data[11]);
            return true;
        }

        private static bool Valid(string session, int revision, int preset, int capacity, double regeneration,
            int roundSeconds, int phase, double endsAt, int refill)
        {
            Guid parsed;
            return session != null && session.Length == 32 && Guid.TryParseExact(session, "N", out parsed) &&
                revision >= 0 && preset >= 0 && preset <= 5 && capacity >= 1 && capacity <= 20 &&
                Finite(regeneration) && regeneration >= 1 && regeneration <= 600 &&
                roundSeconds >= 30 && roundSeconds <= 600 && phase >= 0 && phase <= 2 &&
                Finite(endsAt) && endsAt >= 0 && endsAt <= MaximumClock && refill >= 0 &&
                (phase != 0 || endsAt == 0) && (phase != 1 || endsAt > 0);
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static void ValidateClock(double now)
        {
            if (!Finite(now) || now < 0 || now > MaximumClock - 600) throw new ArgumentOutOfRangeException("now");
        }
        private static int Increment(int counter)
        {
            if (counter == int.MaxValue) throw new InvalidOperationException("Room rules counter exhausted; create a new session.");
            return counter + 1;
        }
    }
}
