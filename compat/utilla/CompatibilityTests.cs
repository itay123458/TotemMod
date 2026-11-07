using System;
using System.Collections.Generic;
using Utilla.Compat;
using Photon.Realtime;
using Photon.Pun;
using ExitGames.Client.Photon;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] public class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string method) {} }
    [AttributeUsage(AttributeTargets.Method)] public class HarmonyPrefix : Attribute {}
}
namespace ExitGames.Client.Photon
{
    public class Hashtable : Dictionary<object, object>
    {
        public new object this[object key] { get { object value; return TryGetValue(key, out value) ? value : null; } set { base[key] = value; } }
    }
}
namespace Photon.Realtime { public class Room { public bool IsVisible; public Hashtable CustomProperties = new Hashtable(); } }
namespace Photon.Pun { public static class PhotonNetwork { public static Room CurrentRoom; public static bool InRoom; public static bool IsMasterClient; } }
public class RoomConfig { public bool isPublic; public Hashtable CustomProps = new Hashtable(); }

internal static class CompatibilityTests
{
    private static int checks;
    private const string Legacy = "private;COMPETITIVE;MODDED_Infection;;";
    private const string Wire = "private;COMPETITIVE;Infection;;";
    private static void Equal(object actual, object expected, string name)
    {
        checks++;
        if (!object.Equals(actual, expected)) throw new Exception(name + ": " + actual);
    }
    public static void Main()
    {
        RoomConfig config = new RoomConfig();
        config.CustomProps["gameMode"] = Legacy;
        config.CustomProps["platform"] = "Steam";
        PrivateRoomCreationPatch.Prefix(config);
        Equal(config.CustomProps["gameMode"], Wire, "private creation uses native mode");
        Equal(config.CustomProps[InfectionWireFormat.Property], InfectionWireFormat.Modded, "explicit opt-in property");
        Equal(config.CustomProps["platform"], "Steam", "preserves other properties");
        PrivateRoomCreationPatch.Prefix(config);
        Equal(config.CustomProps["gameMode"], Wire, "creation retry keeps native mode");
        Equal(config.CustomProps[InfectionWireFormat.Property], InfectionWireFormat.Modded, "creation retry retains explicit opt-in");
        RoomConfig publicConfig = new RoomConfig { isPublic = true };
        publicConfig.CustomProps["gameMode"] = Legacy;
        PrivateRoomCreationPatch.Prefix(publicConfig);
        Equal(publicConfig.CustomProps["gameMode"], Legacy, "public creation unchanged");
        Equal(publicConfig.CustomProps.ContainsKey(InfectionWireFormat.Property), false, "no public opt-in");
        RoomConfig normalConfig = new RoomConfig();
        normalConfig.CustomProps["gameMode"] = Wire;
        PrivateRoomCreationPatch.Prefix(normalConfig);
        Equal(normalConfig.CustomProps.Count, 1, "ordinary room creation gets no marker property");

        Room room = new Room();
        room.CustomProperties = config.CustomProps;
        PhotonNetwork.CurrentRoom = room;
        PhotonNetwork.InRoom = true;
        PhotonNetwork.IsMasterClient = true;
        Equal(InfectionCompatibility.ProjectLocal(Wire), Legacy, "local Utilla receives modded event mode");
        Equal(InfectionCompatibility.ProjectLocal("private;DEFAULT;Infection;;"), "private;DEFAULT;Infection;;", "unrelated query not reclassified");
        Equal(InfectionCompatibility.ProjectLocal(Legacy), Legacy, "projection idempotent");
        room.IsVisible = true;
        Equal(InfectionCompatibility.ProjectLocal(Wire), Wire, "public rejects private-shaped string and marker");
        room.IsVisible = false;
        PhotonNetwork.InRoom = false;
        Equal(InfectionCompatibility.ProjectLocal(Wire), Wire, "no detached-room activation");
        PhotonNetwork.InRoom = true;
        room.CustomProperties[InfectionWireFormat.Property] = null;
        Equal(InfectionCompatibility.ProjectLocal(Wire), Wire, "no marker means ordinary Infection");
        room.CustomProperties[InfectionWireFormat.Property] = InfectionWireFormat.Modded;

        Hashtable update = new Hashtable();
        update["gameMode"] = Legacy;
        PrivateRoomModeUpdatePatch.Prefix(room, update);
        Equal(update["gameMode"], Wire, "master update normalized");
        Equal(update[InfectionWireFormat.Property], InfectionWireFormat.Modded, "master update opts in");
        update = new Hashtable();
        update["gameMode"] = Wire;
        PrivateRoomModeUpdatePatch.Prefix(room, update);
        Equal(update.ContainsKey(InfectionWireFormat.Property), true, "ordinary mode update sends marker deletion");
        Equal(update[InfectionWireFormat.Property], null, "ordinary mode clears marker");
        update = new Hashtable();
        update["other"] = 42;
        PrivateRoomModeUpdatePatch.Prefix(room, update);
        Equal(update.Count, 1, "unrelated update unchanged");

        for (int scenario = 0; scenario < 4; scenario++)
        {
            PhotonNetwork.IsMasterClient = scenario != 0;
            room.IsVisible = scenario == 1;
            PhotonNetwork.InRoom = scenario != 2;
            Room target = scenario == 3 ? new Room() : room;
            update = new Hashtable();
            update["gameMode"] = Legacy;
            PrivateRoomModeUpdatePatch.Prefix(target, update);
            Equal(update["gameMode"], Legacy, "reject nonmaster/public/disconnected/other-room " + scenario);
            Equal(update.ContainsKey(InfectionWireFormat.Property), false, "no marker outside authorized room " + scenario);
        }
        PhotonNetwork.CurrentRoom = null;
        Equal(InfectionCompatibility.ProjectLocal(Wire), Wire, "offline ordinary mode");
        Console.WriteLine("PASS: " + checks + " creation/update/event and authority fixture checks");
    }
}
