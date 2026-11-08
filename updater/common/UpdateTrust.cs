namespace TotemMod.Updating
{
    internal enum UpdateProduct { Totem, Mace }

    internal static class UpdateTrust
    {
        internal const string Repository="itay123458/TotemMod";
        internal const string ManifestUrl="https://github.com/itay123458/TotemMod/releases/latest/download/totem-update.json";
        internal const string PluginGuid="totemmod.gtag.rhys";
        internal const string MaceGuid="macemod.gtag.rhys";
        internal const string MaceManifestUrl="https://github.com/itay123458/TotemMod/releases/latest/download/mace-update.json";
        internal const int Protocol=1;
        internal const int UpdaterVersion=1;
        internal const int MaximumDllBytes=16*1024*1024;
        internal const int MaximumManifestBytes=16*1024;
        internal static string AssetNameFor(UpdateProduct product)
        {
            switch(product) { case UpdateProduct.Totem: return "TotemMod.dll"; case UpdateProduct.Mace: return "MaceMod.dll"; default: throw new System.ArgumentOutOfRangeException("product"); }
        }
        internal static string ProductGuidFor(UpdateProduct product)
        {
            switch(product) { case UpdateProduct.Totem: return PluginGuid; case UpdateProduct.Mace: return MaceGuid; default: throw new System.ArgumentOutOfRangeException("product"); }
        }
        internal static string ManifestFor(UpdateProduct product)
        {
            switch(product) { case UpdateProduct.Totem: return ManifestUrl; case UpdateProduct.Mace: return MaceManifestUrl; default: throw new System.ArgumentOutOfRangeException("product"); }
        }
        internal const string PublicKey="<RSAKeyValue><Modulus>qwIB/4S5J/Xxpg83MEXee3ZmrIYKMQPAAx2OdfY5rCFRuYMVWmIuerDMoUQMQpKGHxKMpf3t7fvcDkJOrGPOJTwb6cwsbqC94VunIQclVRn+Q/p+36MgUi6UvWkNsNqbm8zsoEYDrP347lLWibyeIOaCr3N4BHeUffoM1wJT9efPeTH1odXLWlpXLwKE+gW6HlySh/erNcJPWl99qVXtwsV1xx4gH16o+Rh98H6HJkBJAjCEvcG0fUqYeRX4Gxeo340KS14dSp6CIOHelP6DcJzIvuEaUQByJWG/AZH1zCBpeEBTe/HwmD7mSUzSN9EdG3vvcYUeUdW207IsNFA6LQ==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";
    }
}
