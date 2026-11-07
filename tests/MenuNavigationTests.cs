using System;
using TotemMod;

internal static class MenuNavigationTests
{
    private static int checks;
    private static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new Exception(message);
    }

    public static int Main()
    {
        MenuNavigation navigation = new MenuNavigation(new[] { 3, 2, 0, 1 });
        Check(navigation.Category == 0 && navigation.Page == 0, "The first category starts on its first page.");
        navigation.MovePage(-1);
        Check(navigation.Category == 0 && navigation.Page == 2, "Previous page wraps inside the selected category.");
        navigation.MovePage(1);
        Check(navigation.Category == 0 && navigation.Page == 0, "Next page wraps without changing category.");
        navigation.MovePage(1);
        navigation.SelectCategory(1);
        Check(navigation.Page == 0, "An unvisited category starts on its first page.");
        navigation.MovePage(1);
        navigation.SelectCategory(0);
        Check(navigation.Page == 1, "Returning to a category restores its selected page.");
        navigation.SelectCategory(1);
        Check(navigation.Page == 1, "Each category remembers its own page.");
        navigation.MoveCategory(1);
        navigation.MovePage(1);
        Check(navigation.Category == 2 && navigation.Page == -1 && navigation.PageCount == 0,
            "An empty category remains selectable and page navigation is safe.");
        navigation.MoveCategory(1);
        navigation.MovePage(-1);
        Check(navigation.Page == 0, "A single-page category stays on its only page.");
        navigation.MoveCategory(1);
        Check(navigation.Category == 0 && navigation.Page == 1, "Category navigation wraps and restores pages.");
        navigation.MoveCategory(-1);
        Check(navigation.Category == 3, "Previous category wraps backwards.");
        navigation.MoveCategory(0);
        navigation.MovePage(0);
        Check(navigation.Category == 3 && navigation.Page == 0, "Zero directions do not navigate.");

        MenuNavigation empty = new MenuNavigation(new int[0]);
        empty.MoveCategory(1);
        empty.MovePage(-1);
        Check(empty.Category == -1 && empty.Page == -1 && empty.PageCount == 0,
            "A menu without categories has no selected page and safely ignores navigation.");
        bool rejected = false;
        try { new MenuNavigation(new[] { -1 }); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "Negative page counts are rejected.");
        Console.WriteLine("PASS " + checks + " category paging checks");
        return 0;
    }
}
