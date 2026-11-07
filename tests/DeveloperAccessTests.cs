using System;
using TotemMod;

internal static class DeveloperAccessTests
{
    private static int checks;
    private static void Check(bool value,string label) { checks++; if (!value) throw new Exception(label); }
    public static int Main()
    {
        const ulong owner = 76561199807179430UL;
        Check(DeveloperAccess.Allows(owner,true,true),"The developer account is allowed after Steam login.");
        Check(!DeveloperAccess.Allows(owner+1,true,true),"Another logged-in account cannot access Dev.");
        Check(!DeveloperAccess.Allows(owner-1,true,true),"Nearby account IDs do not match.");
        Check(!DeveloperAccess.Allows(0,true,true),"Missing identity fails closed.");
        Check(!DeveloperAccess.Allows(owner,false,true),"Steam must be initialized.");
        Check(!DeveloperAccess.Allows(owner,true,false),"A logged-out developer cannot access Dev.");
        Check(!DeveloperAccess.Allows(owner,false,false),"Startup cannot unlock Dev.");
        Check(!DeveloperAccess.Allows(ulong.MaxValue,true,true),"Invalid identity cannot unlock Dev.");
        Console.WriteLine("PASS " + checks + " developer account and authentication checks");
        return 0;
    }
}
