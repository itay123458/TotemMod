using UnityEngine;

namespace TotemMod
{
    internal sealed class PalmAnchor
    {
        private Transform cachedRoot;
        private Transform cachedPalm;
        private readonly string palmName;

        public PalmAnchor(bool left=true) { palmName=left ? "palm.01.L" : "palm.01.R"; }

        public Transform Resolve(Transform rigRoot, Transform fallback)
        {
            if (rigRoot == null) return fallback;
            if (cachedRoot == rigRoot && cachedPalm != null) return cachedPalm;
            cachedRoot = rigRoot;
            cachedPalm = FindPalm(rigRoot);
            // Do not cache a fallback: the skeleton may still be loading.
            return cachedPalm != null ? cachedPalm : fallback;
        }

        private Transform FindPalm(Transform node)
        {
            if (node.name == palmName) return node;
            foreach (Transform child in node)
            {
                Transform palm = FindPalm(child);
                if (palm != null) return palm;
            }
            return null;
        }
    }
}
