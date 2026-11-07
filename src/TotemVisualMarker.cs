using UnityEngine;

namespace TotemMod
{
    // Identifies generated render objects so avatar snapshots cannot capture
    // another totem (or recursively capture their own previous miniature).
    internal sealed class TotemVisualMarker : MonoBehaviour { }
}
