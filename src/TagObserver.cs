using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace TotemMod
{
    internal static class TagObserver
    {
        public static int Install(Harmony harmony)
        {
            int count = 0;
            // Competitive broadcasts validate distance and may return without accepting
            // a tag. Its event runs only after validation, unlike a Harmony postfix.
            GorillaTagCompetitiveManager.onTagOccurred -= AfterCompetitiveTag;
            GorillaTagCompetitiveManager.onTagOccurred += AfterCompetitiveTag;
            HashSet<MethodInfo> patched = new HashSet<MethodInfo>();
            // Observe confirmed broadcasts, never ReportTag (which can be rejected).
            foreach (Type type in typeof(GorillaGameManager).Assembly.GetTypes())
            {
                if (!typeof(GorillaGameManager).IsAssignableFrom(type)) continue;
                if (typeof(GorillaTagCompetitiveManager).IsAssignableFrom(type)) continue;
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (method.Name != "HandleTagBroadcast" || method.IsAbstract || method.GetMethodBody() == null) continue;
                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length < 2 || parameters[0].ParameterType != typeof(NetPlayer)) continue;
                    if (!patched.Add(method)) continue;
                    try
                    {
                        harmony.Patch(method, postfix: new HarmonyMethod(typeof(TagObserver).GetMethod("AfterTag", BindingFlags.Static | BindingFlags.Public)));
                        count++;
                    }
                    catch (Exception error) { TotemPlugin.Log.LogWarning("Tag observer could not attach to " + type.Name + ": " + error.Message); }
                }
            }
            return count;
        }

        public static void Uninstall()
        {
            GorillaTagCompetitiveManager.onTagOccurred -= AfterCompetitiveTag;
        }

        private static void AfterCompetitiveTag(NetPlayer taggedPlayer, NetPlayer taggingPlayer)
        {
            AfterTag(taggedPlayer);
        }

        public static void AfterTag(NetPlayer __0)
        {
            try
            {
                if (__0 != null && TotemPlugin.Instance != null)
                    TotemPlugin.Instance.OnConfirmedTag(__0);
            }
            catch (Exception error) { TotemPlugin.Log.LogWarning("Totem tag effect: " + error.Message); }
        }
    }
}
