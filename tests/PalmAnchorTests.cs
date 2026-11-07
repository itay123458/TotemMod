using System;
using System.Collections;
using System.Collections.Generic;
using TotemMod;

namespace UnityEngine
{
    public sealed class Transform : IEnumerable
    {
        public string name;
        public readonly List<Transform> Children = new List<Transform>();
        public Transform(string name) { this.name = name; }
        public IEnumerator GetEnumerator() { return Children.GetEnumerator(); }
    }
}

internal static class PalmAnchorTests
{
    private static int checks;
    private static void Check(bool condition, string label) { checks++; if (!condition) throw new Exception(label); }
    public static int Main()
    {
        UnityEngine.Transform rig = new UnityEngine.Transform("Rig");
        UnityEngine.Transform wrist = new UnityEngine.Transform("LeftHandTarget");
        UnityEngine.Transform skeleton = new UnityEngine.Transform("Skeleton");
        UnityEngine.Transform right = new UnityEngine.Transform("palm.01.R");
        UnityEngine.Transform left = new UnityEngine.Transform("palm.01.L");
        rig.Children.Add(wrist); rig.Children.Add(skeleton); skeleton.Children.Add(right);
        PalmAnchor anchor = new PalmAnchor();
        Check(anchor.Resolve(rig,wrist) == wrist, "Loading skeleton falls back to hand target.");
        skeleton.Children.Add(left);
        Check(anchor.Resolve(rig,wrist) == left, "A late-created left palm replaces the wrist fallback.");
        Check(anchor.Resolve(rig,wrist) != right, "Right palm is never used for a left-hand totem.");
        Check(anchor.Resolve(rig,wrist) == left, "Stable rig keeps the same palm attachment.");
        UnityEngine.Transform nextRig = new UnityEngine.Transform("NewRig");
        UnityEngine.Transform nextPalm = new UnityEngine.Transform("palm.01.L"); nextRig.Children.Add(nextPalm);
        Check(anchor.Resolve(nextRig,wrist) == nextPalm, "Changing rigs resolves the new palm.");
        Check(anchor.Resolve(null,wrist) == wrist, "Missing rig has a safe fallback.");
        Check(new PalmAnchor().Resolve(left,wrist) == left, "An existing palm can be the root.");
        Console.WriteLine("PASS " + checks + " palm/wrist/left-right/loading/rig-change attachment checks");
        return 0;
    }
}
