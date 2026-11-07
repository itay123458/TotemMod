using System;
using TotemMod;

internal static class BetaAccountAccessTests
{
    public static int Main(string[] args)
    {
        int checks=0;
        Action<bool,string> check=(ok,label) => { checks++; if (!ok) throw new Exception(label); };
        check(!BetaAccountAccess.Allows(null),"Missing account cannot unlock Beta.");
        check(!BetaAccountAccess.Allows(""),"Empty account cannot unlock Beta.");
        check(!BetaAccountAccess.Allows("nickname"),"A nickname cannot unlock Beta.");
        check(!BetaAccountAccess.Allows(new string('A',129)),"Oversized identity is rejected.");
        check(BetaAccountAccess.Digest("ab12")==BetaAccountAccess.Digest("AB12"),"Account identifier case is normalized.");
        check(BetaAccountAccess.Digest(" ab12 ")==BetaAccountAccess.Digest("AB12"),"Copied whitespace is trimmed.");
        if (args.Length==1)
        {
            check(BetaAccountAccess.Allows(args[0]),"Chosen account is authorized.");
            check(BetaAccountAccess.Allows(args[0].ToLowerInvariant()),"Chosen account case cannot block access.");
            check(!BetaAccountAccess.Allows(args[0]+"0"),"Another account is rejected.");
        }
        Console.WriteLine("PASS "+checks+" Beta account authorization checks"); return 0;
    }
}
