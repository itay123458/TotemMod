using UnityEngine;

namespace MaceMod.Shared
{
    internal sealed class MaceHandAnchor
    {
        private Transform root,palm;
        private float retry;

        internal Transform Resolve(VRRig rig)
        {
            if(rig==null) return null;
            if(root!=rig.transform) { root=rig.transform; palm=null; retry=0; }
            if(palm==null && Time.unscaledTime>=retry)
            { retry=Time.unscaledTime+1; palm=Find(root); }
            return palm!=null ? palm : rig.rightHandTransform;
        }

        private static Transform Find(Transform node)
        {
            if(node.name=="palm.01.R") return node;
            for(int i=0;i<node.childCount;i++)
            { Transform result=Find(node.GetChild(i)); if(result!=null) return result; }
            return null;
        }
    }
}
