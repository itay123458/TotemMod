using System.Collections.Generic;

namespace TotemMod
{
    internal sealed class RoomCommandQueue
    {
        private readonly List<DeveloperCommand> items = new List<DeveloperCommand>();
        private DeveloperCommand inFlight;
        internal int Count { get { return items.Count; } }
        internal DeveloperCommand Next { get { return items[0]; } }

        internal bool Enqueue(DeveloperCommand command)
        {
            if ((int)command.Kind <= (int)DeveloperCommandKind.AutoStart)
                for (int i=items.Count-1; i>=0; i--) if (items[i].Kind == command.Kind) items.RemoveAt(i);
            if (items.Count >= 16) return false;
            items.Add(command); return true;
        }
        internal DeveloperCommand Take()
        {
            DeveloperCommand command=items[0]; items.RemoveAt(0); return command;
        }
        internal void Begin() { inFlight=Take(); }
        internal void Complete(bool accepted) { inFlight=null; if (!accepted) items.Clear(); }
        internal void Clear() { items.Clear(); inFlight=null; }
        internal float Desired(DeveloperCommandKind kind,float fallback)
        {
            for (int i=items.Count-1; i>=0; i--)
                if (items[i].Kind == kind) return kind == DeveloperCommandKind.Regen ? items[i].Number : items[i].Value;
            if (inFlight != null && inFlight.Kind == kind) return kind == DeveloperCommandKind.Regen ? inFlight.Number : inFlight.Value;
            return fallback;
        }
    }
}
