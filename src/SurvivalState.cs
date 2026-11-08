namespace TotemMod
{
    internal sealed class SurvivalState
    {
        private int health, hunger, revision;
        private double clock, lastAdvance, immunityUntil, deathUntil, regenerationUntil;
        private double saturation, exhaustion, healingTime, starvationTime, regenerationTime;
        internal SurvivalState() { Reset(0); }
        internal int Health { get { return health; } }
        internal int Hunger { get { return hunger; } }
        internal int Revision { get { return revision; } }
        internal bool Dead { get { return health <= 0; } }
        internal double DeathUntil { get { return deathUntil; } }
        internal void Reset(double now)
        {
            clock = lastAdvance = Finite(now) ? now : 0;
            health = hunger = 20; saturation = 5;
            exhaustion = healingTime = starvationTime = regenerationTime = 0;
            immunityUntil = deathUntil = regenerationUntil = double.NegativeInfinity;
            revision++;
        }
        internal void Advance(double now, double distance, bool active = true)
        {
            if (!ValidTime(now)) return;
            double elapsed = System.Math.Min(1, System.Math.Max(0, now - lastAdvance));
            clock = lastAdvance = now;
            if (!active || Dead || elapsed <= 0) return;
            int previousHealth = health, previousHunger = hunger;
            AddExhaustion(.04 * elapsed + (Finite(distance) && distance > 0 ? distance * .08 : 0));
            if (now <= regenerationUntil)
            {
                regenerationTime += elapsed;
                while (regenerationTime >= 1)
                {
                    regenerationTime -= 1;
                    health = System.Math.Min(20, health + 1);
                }
            }
            else regenerationTime = 0;
            if (hunger >= 18 && health < 20)
            {
                healingTime += elapsed;
                if (healingTime >= 4)
                {
                    healingTime -= 4; health++; AddExhaustion(6);
                }
            }
            else healingTime = 0;
            if (hunger == 0 && health > 1)
            {
                starvationTime += elapsed;
                if (starvationTime >= 4) { starvationTime -= 4; health--; }
            }
            else starvationTime = 0;
            if (previousHealth != health || previousHunger != hunger) revision++;
        }
        internal bool Damage(int amount, double now)
        {
            if (amount <= 0 || !ValidTime(now) || Dead || now < immunityUntil) return false;
            clock = now; health = System.Math.Max(0, health - amount); immunityUntil = now + .5;
            if (Dead) { deathUntil = now + 3; healingTime = starvationTime = regenerationTime = 0; }
            revision++; return true;
        }
        internal bool CanEat(CraftItem item)
        {
            return !Dead && (item == CraftItem.GoldenApple || (hunger < 20 && (item == CraftItem.Apple || item == CraftItem.Bread)));
        }
        internal bool Eat(CraftItem item, double now)
        {
            if (!ValidTime(now) || !CanEat(item)) return false;
            clock = now;
            hunger = System.Math.Min(20, hunger + (item == CraftItem.Bread ? 5 : 4));
            saturation = System.Math.Min(hunger, saturation + (item == CraftItem.Bread ? 6 : item == CraftItem.GoldenApple ? 9.6 : 2.4));
            if (item == CraftItem.GoldenApple)
            {
                health = System.Math.Min(20, health + 4);
                regenerationUntil = System.Math.Max(regenerationUntil, now + 10);
            }
            revision++; return true;
        }
        internal void TotemSave(double now)
        {
            if (!ValidTime(now)) return;
            clock = now; health = System.Math.Max(1, health); deathUntil = double.NegativeInfinity; immunityUntil = now + 2;
            regenerationUntil = now + 20; regenerationTime = healingTime = starvationTime = 0;
            revision++;
        }
        internal bool RespawnReady(double now) { return Dead && ValidTime(now) && now >= deathUntil; }
        private bool ValidTime(double now) { return Finite(now) && now >= clock; }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private void AddExhaustion(double amount)
        {
            // Once all food is gone, only the fractional remainder can affect future food.
            // Avoid an unbounded loop even if a caller supplies a huge finite distance.
            exhaustion += amount;
            if (double.IsInfinity(exhaustion)) exhaustion = 4 * 26;
            double steps = System.Math.Floor(exhaustion / 4);
            exhaustion %= 4;
            while (steps > 0 && (saturation > 0 || hunger > 0))
            {
                if (saturation > 0) saturation = System.Math.Max(0, saturation - 1);
                else hunger--;
                steps--;
            }
        }
    }
}
