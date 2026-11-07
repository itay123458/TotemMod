using System;
using System.Reflection;
using HarmonyLib;

namespace TotemMod
{
    internal sealed class ProtectionTicket
    {
        internal readonly NetPlayer Player;
        internal readonly string Session;
        internal readonly int Pops;
        internal ProtectionTicket(NetPlayer player, string session, int pops) { Player = player; Session = session; Pops = pops; }
    }

    internal static class HostProtection
    {
        internal sealed class Context
        {
            internal GorillaTagManager Manager;
            internal ProtectionTicket Ticket;
            internal Context Previous;
            internal bool Used;
        }

        [ThreadStatic] private static Context current;

        public static void Install(Harmony harmony)
        {
            Type type = typeof(HostProtection);
            harmony.Patch(typeof(GorillaTagManager).GetMethod("ReportTag"),
                prefix: new HarmonyMethod(type.GetMethod("BeforeReport")),
                finalizer: new HarmonyMethod(type.GetMethod("AfterReport")));
            harmony.Patch(typeof(GorillaTagManager).GetMethod("AddInfectedPlayer"),
                prefix: new HarmonyMethod(type.GetMethod("BeforeAdd")));
            MethodInfo changeIt = typeof(GorillaTagManager).GetMethod("ChangeCurrentIt", BindingFlags.Instance | BindingFlags.NonPublic);
            if (changeIt == null) throw new MissingMethodException("GorillaTagManager.ChangeCurrentIt");
            harmony.Patch(changeIt, prefix: new HarmonyMethod(type.GetMethod("BeforeChangeIt")));
        }

        public static void BeforeReport(GorillaTagManager __instance, NetPlayer taggedPlayer, out Context __state)
        {
            __state = new Context { Manager = __instance, Previous = current };
            current = __state;
            try
            {
                if (TotemPlugin.Instance != null)
                    __state.Ticket = TotemPlugin.Instance.ReserveProtection(__instance, taggedPlayer);
            }
            catch (Exception error) { TotemPlugin.Log.LogWarning("Totem protection reserve: " + error.Message); }
        }

        public static Exception AfterReport(Exception __exception, Context __state)
        {
            current = __state != null ? __state.Previous : null;
            return __exception;
        }

        private static bool KeepNormalTag(GorillaTagManager manager, NetPlayer target)
        {
            Context context = current;
            if (context == null || context.Used || context.Manager != manager || context.Ticket == null || context.Ticket.Player != target) return true;
            try
            {
                if (TotemPlugin.Instance == null || !TotemPlugin.Instance.CommitProtection(manager, context.Ticket)) return true;
                context.Used = true;
                return false;
            }
            catch (Exception error) { TotemPlugin.Log.LogWarning("Totem protection commit: " + error.Message); return true; }
        }

        // These calls occur only AFTER the game's ReportTag validated the tag.
        // Round seeding, joins, and direct AddInfectedPlayer calls have no ticket.
        public static bool BeforeAdd(GorillaTagManager __instance, NetPlayer infectedPlayer) { return KeepNormalTag(__instance, infectedPlayer); }
        public static bool BeforeChangeIt(GorillaTagManager __instance, NetPlayer newCurrentIt) { return KeepNormalTag(__instance, newCurrentIt); }
    }
}
