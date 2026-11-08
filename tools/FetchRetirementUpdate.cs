using System;
using System.IO;
using System.Security.Cryptography;
using TotemMod.Updating;

// Publication compatibility only: preserve the existing signed inactive update
// for old installations without keeping or compiling retired weapon sources.
internal static class FetchRetirementUpdate
{
    private static int Main(string[] args)
    {
        try
        {
            string gameHash;
            using(SHA256 hash=SHA256.Create())
            using(FileStream input=File.OpenRead(args[0]))
                gameHash=BitConverter.ToString(hash.ComputeHash(input)).Replace("-","");
            UpdateClient client=new UpdateClient((address,limit)=> {
                Uri pinned=address.AbsoluteUri==UpdateTrust.MaceManifestUrl
                    ? new Uri("https://github.com/"+UpdateTrust.Repository+"/releases/download/v1.9.8/mace-update.json") : address;
                return UpdateClient.FetchBytes(pinned,limit);
            },UpdateProduct.Mace);
            UpdateDownload update=client.Fetch(new Version(0,0),gameHash);
            if(update==null || update.Manifest.Version!="1.9.8" || update.Payload.Length>16384)
                throw new InvalidDataException("Expected the small inactive retirement update.");
            Directory.CreateDirectory(args[1]);
            string dll=Path.Combine(args[1],"MaceMod.dll");
            File.WriteAllBytes(dll,update.Payload);
            if(MaceUpdateInstaller.InstalledVersion(dll)!=new Version(1,9,8))
                throw new InvalidDataException("Unexpected retirement plugin identity.");
            File.WriteAllBytes(Path.Combine(args[1],"mace-update.json"),update.Manifest.ToJson());
            Console.WriteLine("Verified signed inactive legacy update; no weapon source or assets required.");
            return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}
