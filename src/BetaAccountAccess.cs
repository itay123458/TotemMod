using System;
using System.Security.Cryptography;
using System.Text;

namespace TotemMod
{
    internal static class BetaAccountAccess
    {
        // Store an approved subject digest instead of publishing the player's ID.
        private const string Approved="D7B5A6D8A2BFD433CE01ABF7B93883DFCB4E34E4C592E8E140DA3352D3772F60";
        internal static string Digest(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId) || userId.Length>128) return null;
            using (SHA256 sha=SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes("Totem beta subject v1:"+userId.Trim().ToUpperInvariant()))).Replace("-","");
        }
        internal static bool Allows(string userId) { return string.Equals(Digest(userId),Approved,StringComparison.Ordinal); }
    }
}
