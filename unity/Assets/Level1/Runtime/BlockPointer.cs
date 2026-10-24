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
}
