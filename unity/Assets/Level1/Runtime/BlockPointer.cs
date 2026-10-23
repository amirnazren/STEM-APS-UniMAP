using UnityEngine;
using UnityEngine.EventSystems;

namespace EcoBotBuilders.Level1.View
{
    public sealed class BlockPointer : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public Level1Game Owner;
        public Command Kind;
        public Block Source, Parent;
        public void OnBeginDrag(PointerEventData e) { Owner.BeginDrag(Kind,Source,Parent,e); }
        public void OnDrag(PointerEventData e) { Owner.Drag(e); }
        public void OnEndDrag(PointerEventData e) { Owner.EndDrag(); }
    }
    public sealed class BlockDrop : MonoBehaviour, IDropHandler
    {
        public Level1Game Owner;
        public Block Parent;
        public int Boundary;
        public void OnDrop(PointerEventData e) { Owner.Drop(Parent,Boundary); }
    }
}
