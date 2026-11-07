using System;

namespace MaceMod.Shared
{
    // Exact primitive types keep malformed/custom property payloads fail-closed.
    internal static class MaceWire
    {
        internal static bool Session(object value)
        {
            string text = value as string; Guid id;
            return text != null && text.Length == 32 && Guid.TryParseExact(text, "N", out id) && id != Guid.Empty && text == id.ToString("N");
        }
        internal static bool Int(object value, int min, int max) { return value is int && (int)value >= min && (int)value <= max; }
        internal static bool Float(object value, float min, float max)
        { return value is float && !float.IsNaN((float)value) && !float.IsInfinity((float)value) && (float)value >= min && (float)value <= max; }
        internal static bool Time(object value) { return value is double && !double.IsNaN((double)value) && !double.IsInfinity((double)value) && (double)value >= 0d; }
        internal static bool Coordinate(object value) { return Float(value, -99999.99f, 99999.99f); }
    }

    public sealed class MaceState : IEquatable<MaceState>
    {
        public string Session { get; private set; }
        public bool Enabled { get; private set; }
        public bool Equipped { get; private set; }
        public int Density { get; private set; }
        public int Knockback { get; private set; }
        public int WindBurst { get; private set; }
        public float Scale { get; private set; }
        public bool Receive { get; private set; }
        public float Power { get; private set; }
        public MaceState(string session, bool enabled, bool equipped, int density, int knockback, int windBurst, float scale, bool receive, float power = 1f)
        { Session = session; Enabled = enabled; Equipped = equipped; Density = density; Knockback = knockback; WindBurst = windBurst; Scale = scale; Receive = receive; Power = power; }
        public object[] Encode() { return new object[] { 1, Session, Enabled, Equipped, Density, Knockback, WindBurst, Scale, Receive, Power }; }
        public static bool TryDecode(object value, out MaceState state)
        {
            state = null; object[] d = value as object[];
            if (d == null || d.Length != 10 || !MaceWire.Int(d[0], 1, 1) || !MaceWire.Session(d[1]) || !(d[2] is bool) || !(d[3] is bool) ||
                !MaceWire.Int(d[4], 0, 5) || !MaceWire.Int(d[5], 0, 2) || !MaceWire.Int(d[6], 0, 3) || !MaceWire.Float(d[7], .5f, 1.5f) || !(d[8] is bool) || !MaceWire.Float(d[9], .25f, 2f)) return false;
            state = new MaceState((string)d[1], (bool)d[2], (bool)d[3], (int)d[4], (int)d[5], (int)d[6], (float)d[7], (bool)d[8], (float)d[9]); return true;
        }
        public bool Equals(MaceState other)
        { return other != null && Session == other.Session && Enabled == other.Enabled && Equipped == other.Equipped && Density == other.Density && Knockback == other.Knockback && WindBurst == other.WindBurst && Scale == other.Scale && Receive == other.Receive && Power == other.Power; }
        public override bool Equals(object other) { return Equals(other as MaceState); }
        public override int GetHashCode() { return (Session ?? "").GetHashCode(); }
    }

    public sealed class MaceHit
    {
        public string SourceSession { get; private set; }
        public int TargetActor { get; private set; }
        public string TargetSession { get; private set; }
        public int Sequence { get; private set; }
        public double Time { get; private set; }
        public float Blocks { get; private set; }
        public float X { get; private set; }
        public float Y { get; private set; }
        public float Z { get; private set; }
        public MaceHit(string sourceSession, int targetActor, string targetSession, int sequence, double time, float blocks, float x, float y, float z)
        { SourceSession = sourceSession; TargetActor = targetActor; TargetSession = targetSession; Sequence = sequence; Time = time; Blocks = blocks; X = x; Y = y; Z = z; }
        public object[] Encode() { return new object[] { 1, SourceSession, TargetActor, TargetSession, Sequence, Time, Blocks, X, Y, Z }; }
        public static bool TryDecode(object value, out MaceHit hit)
        {
            hit = null; object[] d = value as object[];
            if (d == null || d.Length != 10 || !MaceWire.Int(d[0], 1, 1) || !MaceWire.Session(d[1]) || !MaceWire.Int(d[2], 1, int.MaxValue) ||
                !MaceWire.Session(d[3]) || !MaceWire.Int(d[4], 1, int.MaxValue) || !MaceWire.Time(d[5]) || !MaceWire.Float(d[6], 0f, 64f) ||
                !MaceWire.Coordinate(d[7]) || !MaceWire.Coordinate(d[8]) || !MaceWire.Coordinate(d[9])) return false;
            hit = new MaceHit((string)d[1], (int)d[2], (string)d[3], (int)d[4], (double)d[5], (float)d[6], (float)d[7], (float)d[8], (float)d[9]); return true;
        }
    }

    public sealed class MaceReceipt
    {
        public const int Protected = 1, Fling = 2, TagRequest = 3;
        public int SourceActor { get; private set; }
        public string SourceSession { get; private set; }
        public int Sequence { get; private set; }
        public string TargetSession { get; private set; }
        public double Time { get; private set; }
        public int Outcome { get; private set; }
        public MaceReceipt(int sourceActor, string sourceSession, int sequence, string targetSession, double time, int outcome)
        { SourceActor = sourceActor; SourceSession = sourceSession; Sequence = sequence; TargetSession = targetSession; Time = time; Outcome = outcome; }
        public object[] Encode() { return new object[] { 1, SourceActor, SourceSession, Sequence, TargetSession, Time, Outcome }; }
        public static bool TryDecode(object value, out MaceReceipt receipt)
        {
            receipt = null; object[] d = value as object[];
            if (d == null || d.Length != 7 || !MaceWire.Int(d[0], 1, 1) || !MaceWire.Int(d[1], 1, int.MaxValue) || !MaceWire.Session(d[2]) ||
                !MaceWire.Int(d[3], 1, int.MaxValue) || !MaceWire.Session(d[4]) || !MaceWire.Time(d[5]) || !MaceWire.Int(d[6], 1, 3)) return false;
            receipt = new MaceReceipt((int)d[1], (string)d[2], (int)d[3], (string)d[4], (double)d[5], (int)d[6]); return true;
        }
    }
}
