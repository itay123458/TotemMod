using System;

namespace TotemMod
{
    public sealed class TotemInventory
    {
        public const int Maximum = 5;
        public const double RegenSeconds = 40;
        public int Capacity { get; private set; }
        public double RechargeSeconds { get; private set; }
        public int Count { get; private set; }
        private double nextRegen = double.PositiveInfinity;

        public TotemInventory() { Capacity = Maximum; RechargeSeconds = RegenSeconds; Count = Capacity; }

        public void Configure(int capacity, double regen, double now)
        {
            if (capacity < 1 || capacity > 20) throw new ArgumentOutOfRangeException("capacity");
            if (double.IsNaN(regen) || double.IsInfinity(regen) || regen < 1 || regen > 600)
                throw new ArgumentOutOfRangeException("regen");
            if (double.IsNaN(now) || double.IsInfinity(now)) throw new ArgumentOutOfRangeException("now");
            Advance(now);
            bool wasFull = Count >= Capacity;
            double fraction = wasFull ? 1 : Math.Max(0, Math.Min(1, (nextRegen - now) / RechargeSeconds));
            double oldRate = RechargeSeconds;
            Capacity = capacity;
            RechargeSeconds = regen;
            Count = Math.Min(Count, Capacity);
            if (Count == Capacity) nextRegen = double.PositiveInfinity;
            else if (wasFull) nextRegen = now + RechargeSeconds;
            else if (oldRate != RechargeSeconds) nextRegen = now + fraction * RechargeSeconds;
        }

        public void Refill() { Count = Capacity; nextRegen = double.PositiveInfinity; }

        public int Advance(double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || Count >= Capacity || now < nextRegen) return 0;
            int added = (int)Math.Min(Capacity - Count, Math.Floor((now-nextRegen)/RechargeSeconds)+1);
            Count += added;
            nextRegen = Count == Capacity ? double.PositiveInfinity : nextRegen + added * RechargeSeconds;
            return added;
        }

        public bool Consume(double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now)) return false;
            Advance(now);
            if (Count == 0) return false;
            Count--;
            if (double.IsPositiveInfinity(nextRegen)) nextRegen = now + RechargeSeconds;
            return true;
        }

        public int SecondsUntilNext(double now)
        {
            if (Count >= Capacity || double.IsNaN(now) || double.IsInfinity(now)) return 0;
            return (int)Math.Min(Math.Ceiling(RechargeSeconds), Math.Max(0, Math.Ceiling(nextRegen-now)));
        }
    }
}
