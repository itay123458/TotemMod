using System;
using System.Collections.Generic;
using System.Reflection;
using TotemMod;

// Minimal game/loader boundary fixture. The production observer is compiled
// directly into this test. Game symbols are also checked by the real DLL build.
public class NetPlayer { public bool IsLocal; }
public class GorillaGameManager
{
    public virtual void HandleTagBroadcast(NetPlayer target, NetPlayer tagger) { }
}
public class GorillaTagCompetitiveManager : GorillaGameManager
{
    public static event Action<NetPlayer, NetPlayer> onTagOccurred;
    public bool Accepted;
    public override void HandleTagBroadcast(NetPlayer target, NetPlayer tagger)
    {
        if (!Accepted) return;
        if (onTagOccurred != null) onTagOccurred(target, tagger);
    }
}
namespace HarmonyLib
{
    public sealed class HarmonyMethod { public HarmonyMethod(MethodInfo method) { } }
    public sealed class Harmony
    {
        public readonly List<MethodInfo> Patched = new List<MethodInfo>();
        public void Patch(MethodInfo method, HarmonyMethod postfix) { Patched.Add(method); }
    }
}
namespace TotemMod
{
    public sealed class TestLog { public void LogWarning(string message) { throw new Exception(message); } }
    public sealed class TotemPlugin
    {
        public static readonly TestLog Log = new TestLog();
        public static TotemPlugin Instance;
        public readonly EquipState State = new EquipState();
        public int Pops;
        public void PopFromTag() { if (State.Consume()) Pops++; }
        public void OnConfirmedTag(NetPlayer player) { if (player.IsLocal) PopFromTag(); }
    }
}
internal static class TagObserverTests
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception(message);
    }
    public static int Main()
    {
        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony();
        TagObserver.Install(harmony);
        Check(!harmony.Patched.Exists(m => m.DeclaringType == typeof(GorillaTagCompetitiveManager)), "Competitive rejected paths must not receive a postfix.");
        Check(harmony.Patched.Exists(m => m.DeclaringType == typeof(GorillaGameManager)), "Ordinary broadcasts still get observed.");
        TotemPlugin.Instance = new TotemPlugin();
        TotemPlugin.Instance.State.Update(true, true, true);
        GorillaTagCompetitiveManager manager = new GorillaTagCompetitiveManager();
        NetPlayer local = new NetPlayer { IsLocal = true };
        NetPlayer remote = new NetPlayer();
        manager.HandleTagBroadcast(local, remote);
        Check(TotemPlugin.Instance.Pops == 0 && TotemPlugin.Instance.State.Equipped, "Rejected broadcasts must leave the totem equipped.");
        manager.Accepted = true;
        manager.HandleTagBroadcast(remote, local);
        Check(TotemPlugin.Instance.Pops == 0, "Remote players' tags cannot consume our totem.");
        manager.HandleTagBroadcast(local, remote);
        Check(TotemPlugin.Instance.Pops == 1 && !TotemPlugin.Instance.State.Equipped, "Accepted local tag pops once.");
        manager.HandleTagBroadcast(local, remote);
        Check(TotemPlugin.Instance.Pops == 1, "Duplicate events cannot pop twice.");
        TagObserver.Uninstall();
        TotemPlugin.Instance.State.Update(true, false, true);
        TotemPlugin.Instance.State.Update(true, true, true);
        manager.HandleTagBroadcast(local, remote);
        Check(TotemPlugin.Instance.Pops == 1, "Unloading unsubscribes from the static event.");
        Console.WriteLine("PASS " + checks + " accepted/rejected/remote/duplicate/unsubscribe tag checks");
        return 0;
    }
}
