using System;
using ExitGames.Client.Photon;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;

namespace Utilla.Compat
{
    public static class InfectionCompatibility
    {
        public static void Normalize(Hashtable properties, bool roomUpdate)
        {
            if (properties == null || !properties.ContainsKey("gameMode")) return;
            string value = properties["gameMode"] as string;
            // Room creation can retry with the same RoomConfig after its first conversion.
            if (!roomUpdate && InfectionWireFormat.IsPrivateMode(value, "Infection") &&
                object.Equals(properties[InfectionWireFormat.Property], InfectionWireFormat.Modded)) return;
            if (InfectionWireFormat.IsPrivateMode(value, InfectionWireFormat.Modded))
            {
                properties["gameMode"] = InfectionWireFormat.Encode(value);
                properties[InfectionWireFormat.Property] = InfectionWireFormat.Modded;
            }
            else
            {
                // A host selecting ordinary Infection must also remove the previous opt-in.
                if (roomUpdate) properties[InfectionWireFormat.Property] = null;
                else properties.Remove(InfectionWireFormat.Property);
            }
        }

        public static string ProjectLocal(string value)
        {
            Room room = PhotonNetwork.CurrentRoom;
            if (room == null || room.IsVisible || !PhotonNetwork.InRoom) return value;
            Hashtable properties = room.CustomProperties;
            if (properties == null || !object.Equals(properties["gameMode"], value)) return value;
            return InfectionWireFormat.Project(value, properties[InfectionWireFormat.Property], true);
        }
    }

    [HarmonyPatch(typeof(RoomConfig), "ToPUNOpts")]
    internal static class PrivateRoomCreationPatch
    {
        [HarmonyPrefix]
        public static void Prefix(RoomConfig __instance)
        {
            if (__instance != null && !__instance.isPublic)
                InfectionCompatibility.Normalize(__instance.CustomProps, false);
        }
    }

    [HarmonyPatch(typeof(Room), "SetCustomProperties")]
    internal static class PrivateRoomModeUpdatePatch
    {
        [HarmonyPrefix]
        public static void Prefix(Room __instance, Hashtable propertiesToSet)
        {
            if (__instance != null && object.ReferenceEquals(__instance, PhotonNetwork.CurrentRoom) &&
                PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient && !__instance.IsVisible)
                InfectionCompatibility.Normalize(propertiesToSet, true);
        }
    }
}
