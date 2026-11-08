namespace TotemMod
{
    internal enum CraftItem { None = 0, Apple = 1, Bread = 2, GoldenApple = 3 }
    internal struct CraftStack
    {
        internal readonly CraftItem Item;
        internal readonly int Count;
        internal CraftStack(CraftItem item, int count)
        {
            bool valid = item >= CraftItem.Apple && item <= CraftItem.GoldenApple && count > 0;
            Item = valid ? item : CraftItem.None;
            Count = valid ? System.Math.Min(64, count) : 0;
        }
    }
    internal sealed class MinecraftInventory
    {
        internal const int HotbarSize = 9, SlotCount = 36;
        private readonly CraftStack[] slots = new CraftStack[SlotCount];
        private CraftStack cursor;
        private int selected, revision, cursorOrigin = -1;
        internal MinecraftInventory() { Reset(); }
        internal int Selected { get { return selected; } }
        internal int Revision { get { return revision; } }
        internal CraftStack Cursor { get { return cursor; } }
        internal CraftStack Get(int index) { return Valid(index) ? slots[index] : new CraftStack(); }
        internal void Reset()
        {
            System.Array.Clear(slots, 0, slots.Length);
            slots[0] = new CraftStack(CraftItem.Apple, 8);
            slots[1] = new CraftStack(CraftItem.Bread, 8);
            slots[2] = new CraftStack(CraftItem.GoldenApple, 2);
            cursor = new CraftStack(); cursorOrigin = -1; selected = 0; revision++;
        }
        internal void Select(int index)
        {
            if (index < 0 || index >= HotbarSize || selected == index) return;
            selected = index; revision++;
        }
        internal void Cycle(int direction)
        {
            if (direction != 0) Select((selected + (direction > 0 ? 1 : HotbarSize - 1)) % HotbarSize);
        }
        internal bool Click(int index)
        {
            if (!Valid(index)) return false;
            CraftStack slot = slots[index];
            if (cursor.Count == 0)
            {
                if (slot.Count == 0) return false;
                cursor = slot; slots[index] = new CraftStack(); cursorOrigin = index;
            }
            else if (slot.Count == 0)
            {
                slots[index] = cursor; cursor = new CraftStack(); cursorOrigin = -1;
            }
            else if (slot.Item == cursor.Item)
            {
                int moved = System.Math.Min(64 - slot.Count, cursor.Count);
                if (moved <= 0) return false;
                slots[index] = new CraftStack(slot.Item, slot.Count + moved);
                cursor = new CraftStack(cursor.Item, cursor.Count - moved);
                if (cursor.Count == 0) cursorOrigin = -1;
            }
            else { slots[index] = cursor; cursor = slot; }
            revision++; return true;
        }
        internal bool ReturnCursor()
        {
            if (cursor.Count == 0) return true;
            int capacity = 0;
            for (int i = 0; i < SlotCount; i++) capacity += Capacity(i);
            if (capacity < cursor.Count) return false;
            if (Valid(cursorOrigin)) ReturnInto(cursorOrigin);
            for (int i = 0; i < SlotCount && cursor.Count > 0; i++)
                if (slots[i].Item == cursor.Item) ReturnInto(i);
            for (int i = 0; i < SlotCount && cursor.Count > 0; i++)
                if (slots[i].Count == 0) ReturnInto(i);
            cursorOrigin = -1; revision++; return true;
        }
        internal bool ConsumeSelected(CraftItem expected)
        {
            CraftStack stack = slots[selected];
            if (expected == CraftItem.None || stack.Item != expected || stack.Count <= 0) return false;
            slots[selected] = new CraftStack(expected, stack.Count - 1); revision++; return true;
        }
        internal int CountAll(CraftItem item)
        {
            if (item == CraftItem.None) return 0;
            int count = cursor.Item == item ? cursor.Count : 0;
            for (int i = 0; i < SlotCount; i++) if (slots[i].Item == item) count += slots[i].Count;
            return count;
        }
        private static bool Valid(int index) { return index >= 0 && index < SlotCount; }
        private int Capacity(int index)
        {
            CraftStack stack = slots[index];
            return stack.Count == 0 ? 64 : stack.Item == cursor.Item ? 64 - stack.Count : 0;
        }
        private void ReturnInto(int index)
        {
            int moved = System.Math.Min(Capacity(index), cursor.Count);
            if (moved <= 0) return;
            CraftItem item = cursor.Item;
            slots[index] = new CraftStack(item, slots[index].Count + moved);
            cursor = new CraftStack(item, cursor.Count - moved);
        }
    }
}
