using UnityEngine;

namespace TotemMod
{
    internal sealed class PalmAnchor
    {
        private Transform cachedRoot;
        private Transform cachedPalm;

        public Transform Resolve(Transform rigRoot, Transform fallback)
        {
            if (rigRoot == null) return fallback;
            if (cachedRoot == rigRoot && cachedPalm != null) return cachedPalm;
            cachedRoot = rigRoot;
            cachedPalm = FindPalm(rigRoot);
            // Do not cache a fallback: the skeleton may still be loading.
            return cachedPalm != null ? cachedPalm : fallback;
        }

        private static Transform FindPalm(Transform node)
        {
            if (node.name == "palm.01.L") return node;
            foreach (Transform child in node)
            {
                Transform palm = FindPalm(child);
                if (palm != null) return palm;
            }
            return null;
        }
    }
}
