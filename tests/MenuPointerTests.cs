using System;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class MenuPointerTests
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    // The compiled menu must filter before target bounds, and never smooth the
    // reticle separately afterward: that makes a visible cursor miss its target.
    private static void VerifyIntegration(string path)
    {
        using (AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(path))
        {
            TypeDefinition menu = assembly.MainModule.GetType("TotemMod.TotemMenu");
            MethodDefinition tick = null;
            foreach (MethodDefinition method in menu.Methods)
                if (method.Name == "Tick") tick = method;
            Check(tick != null, "The menu must expose its frame input method.");
            bool filtered = false, checkedBounds = false, wroteReticle = false, updatesSlider = false;
            foreach (Instruction instruction in tick.Body.Instructions)
            {
                MethodReference called = instruction.Operand as MethodReference;
                if (called == null) continue;
                if (called.DeclaringType.FullName == "TotemMod.MenuPointerState" && called.Name == "Update") filtered = true;
                if (called.Name == "IsEnabled")
                {
                    Check(filtered, "Target selection must consume filtered input before activation; the old cursor only filtered after selection.");
                    checkedBounds = true;
                }
                Check(called.DeclaringType.FullName != "UnityEngine.Vector2" || called.Name != "Lerp",
                    "Reticle must display the selected point without a second smoothing pass or click snap.");
                if (called.Name == "set_anchoredPosition") wroteReticle = true;
                if (called.Name == "UpdateSlider") updatesSlider = true;
            }
            Check(filtered && checkedBounds && wroteReticle && updatesSlider, "Menu must filter, select, draw and drag in the same input path.");
        }
    }

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0) VerifyIntegration(args[0]);
            VerifyBehavior();
            Console.WriteLine("PASS " + checks + " pointer checks");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }

    private static void VerifyBehavior()
    {
        TotemMod.MenuPointerState pointer = new TotemMod.MenuPointerState();
        pointer.Update(true, 10, 20, 1, 1);
        Check(pointer.Valid && pointer.X == 10 && pointer.Y == 20, "Acquiring tracking must start at the current aim.");
        pointer.Update(true, 20, 20, 1 + 1.0 / 90, 1);
        Check(pointer.X > 10 && pointer.X < 16, "Small hand motion must filter jitter before hit testing.");
        float selectedX = pointer.X;
        pointer.Click(1 + 1.0 / 90);
        Check(pointer.X == selectedX && pointer.Pulse(1 + 1.0 / 90) > 0.99f, "Click feedback must never move the selected point.");
        Check(pointer.Pulse(1.2) == 0, "The click pulse must finish within 0.1 seconds.");

        TotemMod.MenuPointerState slow = new TotemMod.MenuPointerState();
        TotemMod.MenuPointerState fast = new TotemMod.MenuPointerState();
        slow.Update(true, 0, 0, 0, 1); fast.Update(true, 0, 0, 0, 1);
        for (int i = 1; i <= 2; i++) slow.Update(true, 20, -10, i / 36.0, 1);
        for (int i = 1; i <= 8; i++) fast.Update(true, 20, -10, i / 144.0, 1);
        Check(Math.Abs(slow.X - fast.X) < 0.0001 && Math.Abs(slow.Y - fast.Y) < 0.0001,
            "36Hz and 144Hz must converge equally after the same elapsed time.");

        double time = 0;
        pointer.Reset(); pointer.Update(true, 0, 0, time, 1);
        float peak = 0;
        for (int i = 0; i < 100; i++)
        {
            time += 1.0 / 90;
            pointer.Update(true, i % 2 == 0 ? 3 : -3, 0, time, 1);
            peak = Math.Max(peak, Math.Abs(pointer.X));
        }
        Check(peak < 1.3f, "Alternating three-pixel jitter must be attenuated without a dead zone.");
        pointer.Update(true, 200, -150, time + 1.0 / 90, 1);
        Check(pointer.X == 200 && pointer.Y == -150, "A deliberate target jump must arrive immediately.");
        pointer.Update(true, 201, -150, time + 2.0 / 90, 0);
        Check(pointer.X == 201, "Smoothing zero must use raw input.");

        pointer.Update(false, 999, 999, time + 3.0 / 90, 1);
        Check(!pointer.Valid && pointer.Pulse(time + 3.0 / 90) == 0, "Lost tracking must invalidate stale aim and pulse.");
        pointer.Update(true, -40, 90, time + 4.0 / 90, 1);
        Check(pointer.X == -40 && pointer.Y == 90, "Tracking recovery must acquire current aim instead of crossing stale targets.");
        pointer.Reset(); pointer.Update(true, 2, 3, 30, 1);
        Check(pointer.X == 2 && pointer.Y == 3, "Reopen and recenter must reset the filter.");
        pointer.Update(true, 3, 4, double.NaN, 1);
        Check(!pointer.Valid, "Nonfinite clock input must not poison cursor coordinates.");
        pointer.Update(true, 4, 5, 40, 1);
        pointer.Update(true, float.NaN, 5, 41, 1);
        Check(!pointer.Valid, "Nonfinite aim must not be interactive.");
        pointer.Update(true, 4, 5, 40, 1);
        pointer.Update(true, 10, 11, 39, 1);
        Check(pointer.X == 10 && pointer.Y == 11, "A backwards clock must reacquire instead of using negative elapsed time.");
        pointer.Update(true, 12, 13, 42, 1);
        Check(pointer.X == 12 && pointer.Y == 13, "A long frame gap must not preserve stale aim.");
        pointer.Update(true, 14, 15, 42, 1);
        Check(pointer.X == 12 && pointer.Y == 13, "A repeated timestamp cannot advance a time-based filter.");
        pointer.Update(true, 16, 17, 42.01, float.NaN);
        Check(pointer.Valid && !float.IsNaN(pointer.X), "An invalid smoothing setting must use a finite safe default.");
        pointer.Update(false, 0, 0, 42.02, 1);
        pointer.Click(42.02);
        Check(pointer.Pulse(42.02) == 0, "Invalid pointer input cannot produce a stale click pulse.");
        pointer.Update(true, float.MaxValue, 0, 43, 1);
        pointer.Update(true, -float.MaxValue, 0, 44, 1);
        Check(pointer.Valid && pointer.X == -float.MaxValue, "Finite extreme aim must not overflow when reacquiring.");
    }
}
