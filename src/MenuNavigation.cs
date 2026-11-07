using System;

namespace TotemMod
{
    internal sealed class MenuNavigation
    {
        private readonly int[] pageCounts;
        private readonly int[] selectedPages;
        private int category;

        public MenuNavigation(int[] pageCounts)
        {
            this.pageCounts = pageCounts == null ? new int[0] : (int[])pageCounts.Clone();
            selectedPages = new int[this.pageCounts.Length];
            for (int i = 0; i < this.pageCounts.Length; i++)
            {
                if (this.pageCounts[i] < 0) throw new ArgumentOutOfRangeException("pageCounts");
                selectedPages[i] = this.pageCounts[i] == 0 ? -1 : 0;
            }
            category = this.pageCounts.Length == 0 ? -1 : 0;
        }

        public int Category { get { return category; } }
        public int Page { get { return category < 0 ? -1 : selectedPages[category]; } }
        public int PageCount { get { return category < 0 ? 0 : pageCounts[category]; } }

        public void SelectCategory(int next)
        {
            if (next < 0 || next >= pageCounts.Length) throw new ArgumentOutOfRangeException("next");
            category = next;
        }

        public void MoveCategory(int direction)
        {
            if (category < 0 || direction == 0) return;
            category = (category + (direction < 0 ? -1 : 1) + pageCounts.Length) % pageCounts.Length;
        }

        public void MovePage(int direction)
        {
            if (PageCount == 0 || direction == 0) return;
            selectedPages[category] = (Page + (direction < 0 ? -1 : 1) + PageCount) % PageCount;
        }
    }
}
