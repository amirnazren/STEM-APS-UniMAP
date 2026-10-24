using UnityEngine;
using UnityEngine.EventSystems;

namespace EcoBotBuilders.Level1.View
{
    // A separate MonoScript asset keeps Unity's filename/type mapping unambiguous.
    public sealed class BlockDrop : MonoBehaviour, IDropHandler
    {
        public Level1Game Owner;
        public Block Parent;
        public int Boundary;
        public void OnDrop(PointerEventData e) { Owner.Drop(Parent, Boundary); }
    }
}
