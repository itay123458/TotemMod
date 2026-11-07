using System;
using TotemMod;

internal static class RoomCommandQueueTests
{
    private static int checks;
    private static void Check(bool value,string label) { checks++; if (!value) throw new Exception(label); }
    public static int Main()
    {
        RoomCommandQueue q=new RoomCommandQueue();
        Check(q.Desired(DeveloperCommandKind.Capacity,5)==5,"No local edit shows host value.");
        q.Enqueue(new DeveloperCommand(DeveloperCommandKind.Capacity,10)); q.Begin();
        Check(q.Desired(DeveloperCommandKind.Capacity,5)==10,"In-flight slider stays at requested value.");
        q.Enqueue(new DeveloperCommand(DeveloperCommandKind.Capacity,5));
        Check(q.Desired(DeveloperCommandKind.Capacity,5)==5 && q.Count==1,"Dragging back to old host value remains queued.");
        q.Complete(true);
        Check(q.Desired(DeveloperCommandKind.Capacity,10)==5,"First acknowledgment preserves newer desired value.");
        q.Begin(); q.Complete(true);
        Check(q.Desired(DeveloperCommandKind.Capacity,5)==5 && q.Count==0,"Final confirmed value retires preview.");
        q.Enqueue(new DeveloperCommand(DeveloperCommandKind.Capacity,6));
        q.Enqueue(new DeveloperCommand(DeveloperCommandKind.Capacity,(int)q.Desired(DeveloperCommandKind.Capacity,5)+1));
        Check(q.Desired(DeveloperCommandKind.Capacity,5)==7 && q.Count==1,"Rapid keyboard increments accumulate then coalesce.");
        q.Enqueue(new DeveloperCommand(DeveloperCommandKind.Regen,number:23)); q.Begin();
        Check(q.Desired(DeveloperCommandKind.Regen,40)==23,"Independent slider has its desired value.");
        q.Complete(false);
        Check(q.Count==0 && q.Desired(DeveloperCommandKind.Regen,40)==40,"Denial removes pending previews.");
        q.Enqueue(new DeveloperCommand(DeveloperCommandKind.Capacity,20)); q.Begin(); q.Clear();
        Check(q.Desired(DeveloperCommandKind.Capacity,5)==5,"Room/authority boundary removes in-flight preview.");
        for (int i=0;i<16;i++) Check(q.Enqueue(new DeveloperCommand(DeveloperCommandKind.StartTimer)),"Bounded action queue accepts available slot.");
        Check(!q.Enqueue(new DeveloperCommand(DeveloperCommandKind.RefillAll)) && q.Count==16,"Action queue cannot grow without bound.");
        Console.WriteLine("PASS "+checks+" remote slider intent, acknowledgment and bounded queue checks"); return 0;
    }
}
