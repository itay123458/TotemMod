namespace TotemMod
{
    // Each fresh press toggles the held totem; only a pop spends its charge.
    public sealed class EquipState
    {
        public bool Equipped { get; private set; }
        private bool wasDown;

        public bool Update(bool allowed, bool down, bool hasTotem)
        {
            if (!allowed) { Equipped = false; wasDown = down; return false; }
            bool pressed = down && !wasDown;
            wasDown = down;
            if (!pressed) return false;
            return Toggle(allowed,hasTotem);
        }

        public bool Toggle(bool allowed,bool hasTotem)
        {
            if (!allowed) { Equipped = false; return false; }
            if (Equipped) { Equipped = false; return false; }
            if (!hasTotem) return true;
            Equipped = true;
            return false;
        }

        public bool Consume()
        {
            if (!Equipped) return false;
            Equipped = false;
            return true;
        }

        public void Reset() { Equipped = false; }
    }
}
