using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EcoBotBuilders.Level1.View
{
    /// <summary>The only Level 1 scene controller. Legacy managers are deliberately not instantiated.</summary>
    [DisallowMultipleComponent]
    public sealed class Level1Game : MonoBehaviour
    {
        const float ActionSeconds = .55f;
        readonly ProgramEditor editor = new ProgramEditor();
        readonly Dictionary<Block,Image> rowImages = new Dictionary<Block,Image>();
        readonly List<Button> editButtons = new List<Button>();
        PuzzleSession session;
        IslandBoard board;
        IslandAudio sounds;
        Canvas canvas;
        RectTransform root, content, modal, ghost;
        GameObject portrait;
        ScrollRect scroll;
        Text progress, message, budget, targetLabel, pauseLabel, muteLabel, phaseLabel;
        Button run;
        Block selectedParent;
        bool rebuild, paused, animating, dragging, hintShown;
        int hintTier;
        float elapsed;
        Cell from, to;
        Effect effect;
        Command dragKind;
        Block dragSource, dragParent;
        Font regular, bold;
        bool Busy { get { return animating || session.State == Phase.Running || session.State == Phase.Paused; } }
        bool CanEdit { get { return !Busy && !paused && modal == null; } }

        void Awake()
        {
            Debug.Log("[Tideleaf] Booting offline Level 1 from shared puzzle definition.");
            regular = Resources.Load<Font>("Level1/Interface"); bold = Resources.Load<Font>("Level1/InterfaceBold");
            if (!regular || !bold) { Debug.LogError("[Tideleaf] Bundled UI fonts missing."); enabled=false; return; }
            IslandUI.Regular=regular; IslandUI.Bold=bold;
            session = new PuzzleSession(LevelDefinition.Tideleaf()); sounds = gameObject.AddComponent<IslandAudio>();
            EnsureCameraAndInput(); BuildUI(); RefreshProgram(); SetMessage(session.Message); UpdateHUD();
            Debug.Log("[Tideleaf] Scene ready. Goal derives from " + session.Goal + " litter cells. No network connection is used.");
        }
        void EnsureCameraAndInput()
        {
            if (!Camera.main)
            {
                var c = new GameObject("Offline camera",typeof(Camera)); c.tag="MainCamera";
                c.GetComponent<Camera>().orthographic=true; c.transform.position=new Vector3(0,0,-10);
            }
            Camera.main.clearFlags=CameraClearFlags.SolidColor; Camera.main.backgroundColor=IslandUI.Ocean;
            if (!FindObjectOfType<AudioListener>()) Camera.main.gameObject.AddComponent<AudioListener>();
            if (!FindObjectOfType<EventSystem>()) new GameObject("Offline input",typeof(EventSystem),typeof(StandaloneInputModule));
        }
        void BuildUI()
        {
            canvas = new GameObject("Tideleaf canvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster)).GetComponent<Canvas>();
            canvas.transform.SetParent(transform,false); canvas.renderMode=RenderMode.ScreenSpaceOverlay;
            var scaler=canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1280,720); scaler.matchWidthOrHeight=.5f;
            var backdrop=IslandUI.Panel("Ocean backdrop",canvas.transform,0,0,0,0,IslandUI.Ocean).rectTransform;
            backdrop.anchorMin=Vector2.zero; backdrop.anchorMax=Vector2.one; backdrop.offsetMin=backdrop.offsetMax=Vector2.zero;
            root=IslandUI.Rect("Safe design area",canvas.transform,0,0,1280,720);
            root.anchorMin=root.anchorMax=new Vector2(.5f,.5f); root.pivot=new Vector2(.5f,.5f);
            IslandUI.Art("robot",root,24,15,72,72);
            IslandUI.Label("ECO-BOT BUILDERS",root,106,20,420,24,15,IslandUI.Hex("8DD9C2"),true);
            IslandUI.Label("Tideleaf Island",root,104,46,560,42,32,IslandUI.Cream,true);
            IslandUI.Button("?  Help",root,828,28,120,48,IslandUI.Cream,ShowHelp);
            var pause=IslandUI.Button("Pause",root,960,28,120,48,IslandUI.Cream,TogglePause); pauseLabel=pause.GetComponentInChildren<Text>();
            var mute=IslandUI.Button("Sound: on",root,1092,28,160,48,IslandUI.Cream,ToggleMute); muteLabel=mute.GetComponentInChildren<Text>();
            IslandUI.Panel("Header rule",root,28,102,1224,2,IslandUI.Hex("3B696E"));
            IslandUI.Label("01  /  SKY-ISLE CLEANUP",root,32,119,620,25,14,IslandUI.Hex("8DD9C2"),true);
            IslandUI.Label("A little code. A cleaner island.",root,32,149,640,35,27,IslandUI.Cream,true);
            progress=IslandUI.Label("",root,32,189,640,34,21,IslandUI.Cream,true);
            // Calm decorative wave glyphs are interface accents, not substitute world geometry.
            for (int i=0;i<8;i++) IslandUI.Label("~  ~",root,45+(i%4)*170,260+(i/4)*250,90,26,24,IslandUI.Hex("265562"));
            board=new IslandBoard(root,session.Level);
            message=IslandUI.Label("",root,32,529,640,58,18,IslandUI.Cream);
            IslandUI.Label("YOUR BLOCKS  ·  tap to add, or drag into your code",root,32,590,650,28,15,IslandUI.Hex("B5DAD7"),true);
            var commands=new[] {Command.Move,Command.Left,Command.Right,Command.Clean,Command.Repeat};
            for (int i=0;i<commands.Length;i++)
            {
                Command kind=commands[i];
                var button=IslandUI.Button(IslandUI.Name(kind),root,32+i*130,626,120,54,IslandUI.BlockColor(kind),()=>Add(kind),17);
                var pointer=button.gameObject.AddComponent<BlockPointer>(); pointer.Owner=this; pointer.Kind=kind;
                editButtons.Add(button);
            }
            IslandUI.Panel("Code paper",root,718,120,534,490,IslandUI.Cream);
            IslandUI.Label("YOUR PROGRAM",root,738,132,280,35,23,IslandUI.Ink,true);
            budget=IslandUI.Label("",root,1006,133,224,34,18,IslandUI.Ink,true);
            targetLabel=IslandUI.Label("",root,738,172,485,26,15,IslandUI.Ink);
            var main=IslandUI.Button("+ Main",root,738,205,126,40,IslandUI.Hex("CDE3D8"),()=>SelectTarget(null),16); editButtons.Add(main);
            IslandUI.Button("Hint",root,878,205,100,40,IslandUI.Hex("F5CB7D"),ShowHint,16);
            IslandUI.Button("Restart",root,992,205,120,40,IslandUI.Hex("E7DCC2"),ConfirmRestart,16);
            IslandUI.Label("10 slots",root,1124,205,110,40,16,IslandUI.Ink);
            var viewport=IslandUI.Panel("Program viewport",root,738,258,490,302,IslandUI.Hex("F4E8CB"));
            viewport.raycastTarget=true; viewport.gameObject.AddComponent<RectMask2D>();
            scroll=viewport.gameObject.AddComponent<ScrollRect>(); scroll.horizontal=false; scroll.vertical=true;
            scroll.movementType=ScrollRect.MovementType.Clamped; scroll.scrollSensitivity=28;
            scroll.viewport=viewport.rectTransform;
            content=IslandUI.Rect("Program rows",viewport.transform,0,0,476,302); scroll.content=content;
            var scrollbarRoot=IslandUI.Panel("Scroll track",root,1232,258,12,302,IslandUI.Hex("D8CCB1"));
            var scrollbar=scrollbarRoot.gameObject.AddComponent<Scrollbar>(); scrollbar.direction=Scrollbar.Direction.BottomToTop;
            var handle=IslandUI.Panel("Scroll handle",scrollbarRoot.transform,0,0,12,50,IslandUI.Hex("6C9C91")); handle.raycastTarget=true;
            scrollbar.handleRect=handle.rectTransform; scrollbar.targetGraphic=handle;
            scroll.verticalScrollbar=scrollbar; scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.AutoHide;
            phaseLabel=IslandUI.Label("",root,738,568,490,32,16,IslandUI.Ink);
            run=IslandUI.Button("RUN  ▶",root,718,626,260,54,IslandUI.Hex("8DD9C2"),Run,22);
            IslandUI.Button("Stop / reset",root,990,626,262,54,IslandUI.Hex("E7DCC2"),Stop,20);
            IslandUI.Label("OFFLINE ADVENTURE  /  No sign-in. Just curiosity.",root,32,689,1100,24,12,IslandUI.Hex("8AB6B6"));
            portrait=IslandUI.Panel("Rotate device",root,0,0,1280,720,new Color(.04f,.12f,.16f,.98f)).gameObject;
            portrait.GetComponent<Image>().raycastTarget=true;
            var rotate=IslandUI.Label("Please rotate your device\nto landscape to build your code.",portrait.transform,200,240,880,240,38,IslandUI.Cream,true);
            rotate.alignment=TextAnchor.MiddleCenter; portrait.SetActive(false);
        }
        void Update()
        {
            if (session==null || !root) return;
            var safe=Screen.safeArea; float factor=Mathf.Max(.001f,canvas.scaleFactor);
            float fit=Mathf.Min(safe.width/(1280*factor),safe.height/(720*factor));
            root.localScale=Vector3.one*fit;
            root.anchoredPosition=(safe.center-new Vector2(Screen.width,Screen.height)*.5f)/factor;
            bool isPortrait=Screen.height>Screen.width;
            portrait.SetActive(isPortrait); if(isPortrait) { portrait.transform.SetAsLastSibling(); if(!paused) SetPaused(true); }
            if (Input.GetKeyDown(KeyCode.Escape) && !isPortrait) TogglePause();
            if (rebuild && !dragging) { rebuild=false; RefreshProgram(); }
            if (paused) return;
            float dt=Mathf.Min(Time.unscaledDeltaTime,.05f); board.Update(dt);
            if (!animating && session.State==Phase.Running) ExecuteStep();
            if (!animating) return;
            elapsed+=dt;
            board.Position(from,to,Mathf.Clamp01(elapsed/ActionSeconds),session.Facing,effect==Effect.Move);
            if (elapsed<ActionSeconds) return;
            animating=false;
            if(session.State==Phase.Won) Win();
            else if(session.State==Phase.Retry) { SetMessage(session.Message); UpdateHUD(); }
        }
        void ExecuteStep()
        {
            from=session.Position; effect=session.Step(); to=session.Position; elapsed=0; animating=true;
            if(effect==Effect.Clean) board.Clean(to);
            string key=effect==Effect.Move?"move":effect==Effect.Turn?"turn":effect==Effect.Clean?"clean":effect==Effect.Blocked?"blocked":"empty";
            sounds.Play(key); SetMessage(session.Message); Highlight(); FocusRow(session.ActiveBlock); UpdateHUD();
        }
        void Run()
        {
            if(Busy || paused || modal!=null) return;
            string error; if(!session.Run(editor.Main,out error)) { SetMessage(error); sounds.Play("blocked"); return; }
            board.Reset(session); sounds.Play("tap"); UpdateHUD(); Highlight();
        }
        void Stop()
        {
            CloseModal(); EndDrag(); animating=false; paused=false; session.Reset(); sounds.Pause(false);
            board.Reset(session); SetMessage("Robot reset. Your code is safe — edit it and run again!"); Highlight(); UpdateHUD();
        }
        void Restart()
        {
            Stop(); editor.Main.Clear(); selectedParent=null; hintTier=0; hintShown=false; RefreshProgram();
            SetMessage("Fresh start! Tap Move, then Clean. Watch what each block does.");
        }
        void ConfirmRestart()
        {
            if (modal!=null) return;
            SetPaused(true); MakeModal("Start a fresh program?","Restart clears your code, tokens and hints. Stop / reset keeps your code instead.");
            ModalButton("Keep my code",90,280,CloseAndResume); ModalButton("Restart",360,280,Restart);
        }
        void ToggleMute() { sounds.Toggle(); UpdateHUD(); }
        void TogglePause()
        {
            if(modal!=null) { if(session.State!=Phase.Won) CloseAndResume(); return; }
            if(paused) { SetPaused(false); return; }
            SetPaused(true); MakeModal("Take a breath","Your robot and program are safe. Ready when you are.");
            ModalButton("Resume",90,280,CloseAndResume); ModalButton("Stop / reset",360,280,Stop);
        }
        void SetPaused(bool value)
        {
            paused=value; if(value) session.Pause(); else session.Resume(); sounds.Pause(value); UpdateHUD();
        }
        void CloseAndResume() { CloseModal(); SetPaused(false); }
        void OnApplicationFocus(bool focus) { if(!focus && session!=null) SetPaused(true); }
        void OnApplicationPause(bool value) { if(value && session!=null) SetPaused(true); }
        void OnDisable() { if(session!=null && sounds) SetPaused(true); }
        void OnDestroy() { if(ghost) Destroy(ghost.gameObject); }
        void SetMessage(string text) { message.text=text; }
        void UpdateHUD()
        {
            if(!progress) return;
            progress.text="Clean tokens  " + session.Tokens + " / " + session.Goal + "     ·     Collect every litter pile";
            budget.text=Programs.Cost(editor.Main) + " / 10 blocks";
            phaseLabel.text=paused?"Paused — tap Resume to continue":session.State==Phase.Running?"Running · action " + session.Cursor + " / " + session.ActionCount:
                session.State==Phase.Won?"Island restored!":session.State==Phase.Retry?"Try again — your code is ready to edit":"Build your code, then press Run.";
            pauseLabel.text=paused?"Resume":"Pause"; muteLabel.text=sounds.Muted?"Sound: off":"Sound: on";
            run.interactable=!Busy && !paused && modal==null;
            foreach(var b in editButtons) if(b) b.interactable=CanEdit;
            targetLabel.text=selectedParent==null?"Add to: MAIN  ·  tap a Repeat's Body to work inside it":"Add to: REPEAT BODY  ·  tap Main to exit";
        }
        void Highlight()
        {
            foreach(var pair in rowImages)
            {
                bool active=pair.Key==session.ActiveBlock && (Busy || session.State==Phase.Won || session.State==Phase.Retry);
                pair.Value.color=active?IslandUI.Hex("FFE08A"):IslandUI.BlockColor(pair.Key.Kind);
            }
        }
        void FocusRow(Block block)
        {
            Image row; if(block==null || !rowImages.TryGetValue(block,out row)) return;
            float top=-row.rectTransform.anchoredPosition.y, bottom=top+row.rectTransform.rect.height;
            float offset=content.anchoredPosition.y, height=scroll.viewport.rect.height;
            if(top<offset) offset=top; else if(bottom>offset+height) offset=bottom-height;
            scroll.StopMovement(); content.anchoredPosition=new Vector2(0,Mathf.Clamp(offset,0,Mathf.Max(0,content.rect.height-height)));
        }
        void SelectTarget(Block parent)
        {
            if(!CanEdit) return; selectedParent=parent; sounds.Play("tap"); RefreshProgram();
            SetMessage(parent==null?"New blocks will go at the end of Main.":"Now tap Move or Clean to put it INSIDE this Repeat.");
        }
        void Add(Command kind)
        {
            if(!CanEdit) return;
            Insert(kind,selectedParent,editor.Container(selectedParent).Count);
        }
        void Insert(Command kind,Block parent,int index)
        {
            string error;
            if(editor.Insert(kind,parent,index,out error))
            {
                sounds.Play("tap"); rebuild=true;
                SetMessage(kind==Command.Repeat?"Repeat added. Tap its Body button, then add actions inside.":"Added " + IslandUI.Name(kind) + ". Use ↑ ↓ or drag the grip to reorder.");
            }
            else { SetMessage(error); sounds.Play("blocked"); }
        }
        void Remove(Block block,Block parent)
        {
            if(!CanEdit) return; editor.Remove(block,parent); if(selectedParent==block) selectedParent=null;
            rebuild=true; sounds.Play("tap"); SetMessage("Block removed. You can try a different idea.");
        }
        void Reorder(Block block,Block parent,int delta)
        {
            if(!CanEdit) return; var list=editor.Container(parent); int index=list.IndexOf(block);
            editor.Move(block,parent,parent,index+(delta>0?2:-1)); rebuild=true; sounds.Play("tap");
        }
        void ChangeCount(Block block,int delta)
        {
            if(!CanEdit) return; editor.ChangeCount(block,delta); rebuild=true; sounds.Play("tap");
        }
        void AddDrop(GameObject go,Block parent,int boundary)
        {
            var drop=go.AddComponent<BlockDrop>(); drop.Owner=this; drop.Parent=parent; drop.Boundary=boundary;
        }
        void RefreshProgram()
        {
            float position=scroll.verticalNormalizedPosition;
            for(int i=content.childCount-1;i>=0;i--) { var child=content.GetChild(i).gameObject; child.SetActive(false); Destroy(child); }
            rowImages.Clear(); editButtons.RemoveAll(b=>!b || b.transform.IsChildOf(content));
            if(selectedParent!=null && !editor.Main.Contains(selectedParent)) selectedParent=null;
            float y=4;
            for(int i=0;i<editor.Main.Count;i++)
            {
                var block=editor.Main[i]; AddRow(block,null,i,ref y);
                if(block.Kind!=Command.Repeat) continue;
                for(int j=0;j<block.Body.Count;j++) AddRow(block.Body[j],block,j,ref y);
                AddEnd(block,ref y); y+=8;
            }
            AddEnd(null,ref y);
            if(editor.Main.Count==0)
            {
                IslandUI.Label("1. Tap Move in the palette.\n2. Tap Clean.\n3. Press Run to test your idea.\n\nLook for a pattern you can Repeat!",content,20,y+8,440,152,21,IslandUI.Ink);
                y+=174;
            }
            content.sizeDelta=new Vector2(476,Mathf.Max(302,y+8));
            Canvas.ForceUpdateCanvases(); scroll.verticalNormalizedPosition=position;
            UpdateHUD(); Highlight();
        }
        void AddRow(Block block,Block parent,int index,ref float y)
        {
            bool child=parent!=null; float x=child?28:0, width=child?448:476;
            var row=IslandUI.Panel("Block " + IslandUI.Name(block.Kind),content,x,y,width,48,IslandUI.BlockColor(block.Kind)); row.raycastTarget=true;
            rowImages[block]=row; AddDrop(row.gameObject,parent,index);
            var grip=IslandUI.Button("≡",row.transform,0,0,40,48,new Color(1,1,1,.12f),()=>{},24);
            var pointer=grip.gameObject.AddComponent<BlockPointer>(); pointer.Owner=this; pointer.Kind=block.Kind; pointer.Source=block; pointer.Parent=parent;
            editButtons.Add(grip);
            IslandUI.Label((index+1)+". " + IslandUI.Name(block.Kind),row.transform,43,0,width-192,48,19,IslandUI.Ink,true);
            editButtons.Add(IslandUI.Button("↑",row.transform,width-144,0,46,48,new Color(1,1,1,.16f),()=>Reorder(block,parent,-1),22));
            editButtons.Add(IslandUI.Button("↓",row.transform,width-96,0,46,48,new Color(1,1,1,.16f),()=>Reorder(block,parent,1),22));
            editButtons.Add(IslandUI.Button("×",row.transform,width-48,0,46,48,new Color(1,1,1,.16f),()=>Remove(block,parent),24));
            y+=54;
            if(block.Kind!=Command.Repeat) return;
            var controls=IslandUI.Panel("Repeat body header",content,28,y,448,44,IslandUI.Hex("E2D6EF"));
            editButtons.Add(IslandUI.Button("−",controls.transform,0,0,44,44,IslandUI.Hex("C8B8ED"),()=>ChangeCount(block,-1),24));
            IslandUI.Label(block.Count+" times",controls.transform,52,0,112,44,18,IslandUI.Ink,true);
            editButtons.Add(IslandUI.Button("+",controls.transform,160,0,44,44,IslandUI.Hex("C8B8ED"),()=>ChangeCount(block,1),24));
            editButtons.Add(IslandUI.Button(selectedParent==block?"✓ Body selected":"+ Body",controls.transform,216,0,232,44,IslandUI.Hex("CDE3D8"),()=>SelectTarget(block),16));
            y+=50;
        }
        void AddEnd(Block parent,ref float y)
        {
            var area=IslandUI.Button(parent==null?"+ Add to Main / drop here":"+ Add to this body / drop here",content,parent==null?0:28,y,parent==null?476:448,44,
                selectedParent==parent?IslandUI.Hex("B9DCCB"):IslandUI.Hex("E5DDCA"),()=>SelectTarget(parent),16);
            AddDrop(area.gameObject,parent,editor.Container(parent).Count); editButtons.Add(area); y+=50;
        }
        public void BeginDrag(Command kind,Block source,Block parent,PointerEventData e)
        {
            if(!CanEdit) return; dragging=true; dragKind=kind; dragSource=source; dragParent=parent;
            var image=IslandUI.Panel("Dragging block",root,0,0,190,48,IslandUI.BlockColor(kind)); ghost=image.rectTransform;
            IslandUI.Label(IslandUI.Name(kind),ghost,12,0,170,48,20,IslandUI.Ink,true);
            ghost.gameObject.AddComponent<CanvasGroup>().blocksRaycasts=false; Drag(e);
        }
        public void Drag(PointerEventData e)
        {
            if(!ghost) return; Vector2 local;
            if(RectTransformUtility.ScreenPointToLocalPointInRectangle(root,e.position,e.pressEventCamera,out local))
                ghost.anchoredPosition=local+new Vector2(640,-360)+new Vector2(12,-12);
            // Edge auto-scroll makes long programs reachable while dragging on a small screen.
            Vector2 point;
            if(RectTransformUtility.ScreenPointToLocalPointInRectangle(scroll.viewport,e.position,e.pressEventCamera,out point))
            {
                if(point.y>-28) scroll.verticalNormalizedPosition=Mathf.Clamp01(scroll.verticalNormalizedPosition+Time.unscaledDeltaTime*1.2f);
                if(point.y<-scroll.viewport.rect.height+28) scroll.verticalNormalizedPosition=Mathf.Clamp01(scroll.verticalNormalizedPosition-Time.unscaledDeltaTime*1.2f);
            }
        }
        public void Drop(Block parent,int boundary)
        {
            if(!dragging || !CanEdit) return;
            if(dragSource==null) Insert(dragKind,parent,boundary);
            else if(editor.Move(dragSource,dragParent,parent,boundary)) { rebuild=true; sounds.Play("tap"); SetMessage("Order changed. Ready to test?"); }
            else SetMessage("Reorder within Main or the same body. To change groups, remove and add the block.");
        }
        public void EndDrag() { dragging=false; if(ghost) Destroy(ghost.gameObject); ghost=null; }
        void MakeModal(string title,string body)
        {
            EndDrag();
            var shade=IslandUI.Panel("Modal shade",root,0,0,1280,720,new Color(.025f,.09f,.12f,.93f)); shade.raycastTarget=true; modal=shade.rectTransform;
            var card=IslandUI.Panel("Dialog",modal,300,150,680,420,IslandUI.Cream);
            IslandUI.Label(title,card.transform,40,26,600,62,32,IslandUI.Ink,true);
            IslandUI.Label(body,card.transform,40,100,600,168,20,IslandUI.Ink);
            UpdateHUD();
        }
        void ModalButton(string text,float x,float y,UnityEngine.Events.UnityAction action)
        {
            IslandUI.Button(text,modal.GetChild(0),x,y,230,54,IslandUI.Hex("8DD9C2"),action,18);
        }
        void CloseModal() { if(modal) { modal.gameObject.SetActive(false); Destroy(modal.gameObject); } modal=null; }
        void ShowHelp()
        {
            if(modal!=null) return; SetPaused(true);
            MakeModal("You are the robot's coder!","Move goes one tile in the arrow's direction. Left / Right turns in place. Clean picks up litter UNDER the robot.\n\nRepeat runs its body 2–4 times. Wrapper + each body block costs one slot. Select Body before adding actions inside it.");
            ModalButton("Let's build!",90,320,CloseAndResume); ModalButton("Stop / reset",360,320,Stop);
        }
        void ShowHint()
        {
            if(modal!=null) return; SetPaused(true); hintTier=Mathf.Min(3,hintTier+1); hintShown=true;
            string[] hints={"", "Look at the litter trail. Move onto a pile, then Clean. The little arrow shows where the robot will go next.",
                "The first three piles make a straight line. Repeat [Move, Clean] three times. At the corner, turn LEFT and find the next pattern.",
                "Try this 7-block program:\nRepeat 3 [Move, Clean]\nTurn left\nRepeat 3 [Move, Clean]\nEach Repeat costs 1 + its 2 body blocks = 3 slots."};
            MakeModal("Hint " + hintTier + " of 3",hints[hintTier]);
            ModalButton("Got it",90,320,CloseAndResume);
            if(hintTier==3) ModalButton("Use example",360,320,()=>{Stop();editor.LoadExample();selectedParent=null;RefreshProgram();SetMessage("Example loaded. Read each Repeat, then press Run!");});
        }
        void Win()
        {
            int cost=Programs.Cost(editor.Main),best=PlayerPrefs.GetInt("TideleafBest",99);
            if(cost<best) { best=cost; PlayerPrefs.SetInt("TideleafBest",cost); PlayerPrefs.Save(); }
            sounds.Play("win"); UpdateHUD();
            MakeModal("Island restored!","You collected all " + session.Goal + " clean tokens with " + cost + " blocks.\nBest on this device: " + best + " blocks.\n\n" + (hintShown?"Learning with hints is great coding, too!":"Great problem-solving, eco-engineer!") + "\nCan you explain what your Repeat blocks do?");
            ModalButton("Try another idea",90,320,()=>{Stop();SetMessage("Your code is saved. Try a different route or fewer blocks.");});
            ModalButton("Fresh adventure",360,320,Restart);
        }
    }
}
