using System;
using System.Reflection;
using TotemMod;

public sealed class NetPlayer { public int ActorNumber; }
public class GorillaTagManager
{
    public void ReportTag(NetPlayer taggedPlayer, NetPlayer taggingPlayer) { }
    public void AddInfectedPlayer(NetPlayer infectedPlayer, bool withTagStop = true) { }
    protected void ChangeCurrentIt(NetPlayer newCurrentIt, bool withTagFreeze = true) { }
}
namespace HarmonyLib
{
    public sealed class HarmonyMethod { public HarmonyMethod(MethodInfo method) { if (method == null) throw new Exception("Missing hook"); } }
    public sealed class Harmony
    {
        public int Count;
        public void Patch(MethodInfo method, HarmonyMethod prefix = null, HarmonyMethod finalizer = null)
        { if (method == null) throw new Exception("Missing game method"); Count++; }
    }
}
namespace TotemMod
{
    public sealed class TestLog { public void LogWarning(string message) { throw new Exception(message); } }
    public sealed class TotemPlugin
    {
        public static readonly TestLog Log = new TestLog();
        public static TotemPlugin Instance;
        public bool Allowed = true;
        public bool Equipped = true;
        public int Saves;
        internal ProtectionTicket ReserveProtection(GorillaTagManager manager, NetPlayer target)
        { return Allowed && Equipped ? new ProtectionTicket(target, "session", 0) : null; }
        internal bool CommitProtection(GorillaTagManager manager, ProtectionTicket ticket)
        { if (!Allowed) return false; Equipped = false; Saves++; return true; }
    }
}
internal static class HostProtectionTests
{
    private static int count;
    private static void Check(bool value, string label) { count++; if (!value) throw new Exception(label); }
    public static int Main()
    {
        TotemPlugin.Instance = new TotemPlugin();
        GorillaTagManager manager = new GorillaTagManager();
        NetPlayer target = new NetPlayer { ActorNumber = 2 }, other = new NetPlayer { ActorNumber = 3 };
        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony();
        HostProtection.Install(harmony);
        Check(harmony.Count == 3, "Both infection and small-lobby tag mutation points are hooked.");
        Check(HostProtection.BeforeAdd(manager, target), "Round seeding/joins outside ReportTag stay unchanged.");
        HostProtection.Context context;
        HostProtection.BeforeReport(manager, target, out context);
        HostProtection.AfterReport(null, context);
        Check(TotemPlugin.Instance.Saves == 0 && TotemPlugin.Instance.Equipped, "Rejected ReportTag consumes no totem.");
        HostProtection.BeforeReport(manager, target, out context);
        Check(HostProtection.BeforeAdd(manager, other), "Other players are not protected by this ticket.");
        Check(!HostProtection.BeforeAdd(manager, target), "Validated infection consumes the ticket and leaves target untagged.");
        Check(HostProtection.BeforeAdd(manager, target), "Same ticket cannot be consumed twice.");
        HostProtection.AfterReport(null, context);
        Check(TotemPlugin.Instance.Saves == 1, "One accepted tag produces exactly one save.");
        TotemPlugin.Instance.Equipped = true;
        HostProtection.BeforeReport(manager, target, out context);
        TotemPlugin.Instance.Equipped = false; // Original broadcast popped visual before AddInfectedPlayer.
        Check(!HostProtection.BeforeAdd(manager, target), "Reserved ticket survives a preceding visual-pop broadcast.");
        HostProtection.AfterReport(null, context);
        TotemPlugin.Instance.Equipped = true;
        HostProtection.BeforeReport(manager, target, out context);
        Check(!HostProtection.BeforeChangeIt(manager, target), "Small-lobby tag keeps original tagger IT.");
        HostProtection.AfterReport(null, context);
        TotemPlugin.Instance.Equipped = true; TotemPlugin.Instance.Allowed = false;
        HostProtection.BeforeReport(manager, target, out context);
        Check(HostProtection.BeforeAdd(manager, target), "Disallowed rules do not alter tags.");
        HostProtection.AfterReport(null, context);
        TotemPlugin.Instance.Allowed = true;
        HostProtection.BeforeReport(manager, target, out context);
        Exception error = new Exception("game failure");
        Check(HostProtection.AfterReport(error, context) == error, "Game exceptions remain visible to the game.");
        Check(HostProtection.BeforeAdd(manager, target), "Exception finalizer cleans the ticket.");
        Console.WriteLine("PASS " + count + " accepted/rejected/one-use/seed/small-lobby/exception host-rule checks");
        return 0;
    }
}
