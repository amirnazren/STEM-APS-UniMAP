using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace EcoBotBuilders.Level1.View
{
    public sealed class IslandBoard
    {
        readonly Dictionary<Cell, RawImage> litter = new Dictionary<Cell, RawImage>();
        readonly RectTransform robot;
        readonly Text heading, popup;
        readonly RectTransform headingRect;
        float popClock;
        Vector2 popOrigin;
        public static Vector2 Project(Cell c) { return new Vector2(120 + (c.X + c.Y) * 67, 370 + (c.X - c.Y) * 33); }
        static Vector2[] HeadingOffsets = { new Vector2(43,21), new Vector2(43,-21), new Vector2(-43,-21), new Vector2(-43,21) };
        public IslandBoard(Transform root, LevelDefinition level)
        {
            // Tiles are drawn back-to-front. Surface centres align with the generated tile's diamond.
            for (int depth = -(level.Height - 1); depth < level.Width; depth++)
                for (int y = 0; y < level.Height; y++)
                    for (int x = 0; x < level.Width; x++)
                    {
                        if (x - y != depth) continue;
                        var p = Project(new Cell(x,y));
                        IslandUI.Art("grass", root, p.x - 75, p.y - 54.3f, 150, 117);
                    }
            var start = Project(level.Spawn);
            var startText = IslandUI.Label("START", root, start.x - 30, start.y + 18, 60, 20, 11, IslandUI.Cream, true);
            startText.alignment = TextAnchor.MiddleCenter;
            foreach (var c in level.Obstacles)
            {
                var p = Project(c); IslandUI.Art("stone", root, p.x - 42, p.y - 72, 84, 84);
            }
            foreach (var c in level.Trash)
            {
                var p = Project(c); litter.Add(c, IslandUI.Art("litter", root, p.x - 32, p.y - 40, 64, 64));
            }
            heading = IslandUI.Label("➜", root, 0,0,46,46,35,IslandUI.Cream,true);
            heading.alignment = TextAnchor.MiddleCenter; headingRect = heading.rectTransform;
            headingRect.pivot = new Vector2(0.5f,0.5f);
            var outline = heading.gameObject.AddComponent<Outline>(); outline.effectColor = IslandUI.Ocean; outline.effectDistance = new Vector2(2, -2);
            robot = IslandUI.Art("robot", root, 0,0,101,101).rectTransform;
            popup = IslandUI.Label("",root,0,0,150,44,26,IslandUI.Cream,true);
            popup.alignment = TextAnchor.MiddleCenter;
            var shadow = popup.gameObject.AddComponent<Shadow>(); shadow.effectColor = IslandUI.Ocean;
            Reset(new PuzzleSession(level));
        }
        public void Reset(PuzzleSession state)
        {
            foreach (var pair in litter) pair.Value.gameObject.SetActive(state.HasTrash(pair.Key));
            popup.text = ""; popClock = 0; Position(state.Position, state.Position, 1, state.Facing, false);
        }
        public void Clean(Cell cell)
        {
            RawImage image; if (litter.TryGetValue(cell, out image)) image.gameObject.SetActive(false);
            popOrigin = Project(cell); popClock = 1.1f; popup.text = "+1 token";
        }
        public void Position(Cell from, Cell to, float t, Heading facing, bool bounce)
        {
            var p = Vector2.Lerp(Project(from), Project(to), Mathf.SmoothStep(0,1,t));
            float hop = bounce ? Mathf.Sin(t * Mathf.PI) * 8 : 0;
            robot.anchoredPosition = new Vector2(p.x - 50.5f, -(p.y - 89 - hop));
            var a = p + HeadingOffsets[(int)facing];
            headingRect.anchoredPosition = new Vector2(a.x,-a.y);
            headingRect.localRotation = Quaternion.Euler(0,0,-Mathf.Atan2(HeadingOffsets[(int)facing].y, HeadingOffsets[(int)facing].x) * Mathf.Rad2Deg);
        }
        public void Update(float dt)
        {
            if (popClock <= 0) return;
            popClock = Mathf.Max(0,popClock-dt);
            popup.rectTransform.anchoredPosition = new Vector2(popOrigin.x - 75, -(popOrigin.y - 92 - (1.1f - popClock) * 26));
            popup.color = new Color(1,0.95f,0.72f,Mathf.Min(1,popClock*2));
            if (popClock == 0) popup.text = "";
        }
    }
}
