using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using static Noskang.SkillGraph.Editor.SkillGraphText;

namespace Noskang.SkillGraph.Editor
{
    // Toolbar skill / buff picker, closed. It copies the theme classes of a stock popup field (root, input, text, arrow),
    // so it draws exactly like one; pressing it raises Opened instead of the stock menu.
    // Pressing first puts its tooltip away and raises Opened on the next frame, so a tooltip shown while hovering never stays
    // over the opened list; RestoreTooltip (when the list closes) brings the tooltip back.
    public sealed class SkillGraphOwnerField : VisualElement
    {
        private readonly TextElement text;private string tip;
        public event Action Opened;
        public string Text=>text.text;
        public SkillGraphOwnerField(string label)
        {
            var probe=new PopupField<string>(new List<string>{label},0);
            foreach(var c in probe.GetClasses())AddToClassList(c);
            var input=Copy(probe.Q(className:BasePopupField<string,string>.inputUssClassName),new VisualElement());Add(input);
            text=Copy(probe.Q<TextElement>(className:BasePopupField<string,string>.textUssClassName),new TextElement{text=label});input.Add(text);
            input.Add(Copy(probe.Q(className:BasePopupField<string,string>.arrowUssClassName),new VisualElement()));
            RegisterCallback<PointerDownEvent>(e=>{if(e.button!=0)return;e.StopPropagation();Press();});
        }
        public void Press()
        {
            if(!string.IsNullOrEmpty(tooltip)){tip=tooltip;tooltip="";}
            CloseTooltip();
            if(panel!=null)schedule.Execute(()=>Opened?.Invoke());else Opened?.Invoke();
        }
        public void RestoreTooltip(){if(tip!=null){tooltip=tip;tip=null;}}
        // The editor's tooltip window has no public close; without this a tooltip already shown stays up until the next redraw.
        private static void CloseTooltip()
        {
            try{typeof(EditorWindow).Assembly.GetType("UnityEditor.TooltipView")?.GetMethod("Close",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic,null,Type.EmptyTypes,null)?.Invoke(null,null);}
            catch(Exception){}
        }
        private static TE Copy<TE>(VisualElement from,TE to) where TE:VisualElement{if(from!=null)foreach(var c in from.GetClasses())to.AddToClassList(c);to.pickingMode=from?.pickingMode??PickingMode.Position;return to;}
    }

    // Toolbar skill / buff picker, open: a drop-down under the field with a search box over every owner in key order.
    // The search keeps owners whose "key name" label contains the text (case-insensitive, outer spaces ignored),
    // so typing part of the key or part of the name narrows the list. Up / Down move, Enter picks, Esc closes.
    public sealed class SkillGraphOwnerPicker : EditorWindow
    {
        public const float RowHeight=20;
        public static SkillGraphOwnerPicker Last;
        // Raised when the drop-down goes away (picked, Esc, or a click elsewhere).
        public event Action Closed;
        private List<(int Key,string Name)> all=new List<(int Key,string Name)>();private List<(int Key,string Name)> shown=new List<(int Key,string Name)>();
        private int current,cursor;private Action<int> pick;private bool open;
        private TextField search;private ListView list;private Label count,empty;
        public IReadOnlyList<int> ShownKeys=>shown.Select(x=>x.Key).ToList();
        public int CursorKey=>cursor>=0&&cursor<shown.Count?shown[cursor].Key:0;
        public string Search=>search?.value??"";
        public bool EmptyShown=>empty!=null&&empty.style.display==DisplayStyle.Flex;

        public static string Label((int Key,string Name) o)=>o.Key+" "+o.Name;
        // Owners left by a search, in ascending key order. An empty search keeps all of them.
        public static List<(int Key,string Name)> Filter(IEnumerable<(int Key,string Name)> owners,string query)
        {
            string q=(query??"").Trim();
            return owners.Where(o=>q.Length==0||Label(o).IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0).OrderBy(o=>o.Key).ToList();
        }

        // screen: the closed field's rectangle in screen space; the list opens under it, at least as wide.
        public static SkillGraphOwnerPicker Show(Rect screen,IEnumerable<(int Key,string Name)> owners,int current,Action<int> pick)
        {
            foreach(var w in Resources.FindObjectsOfTypeAll<SkillGraphOwnerPicker>())w.Close();
            var p=Create(owners,current,pick);
            float height=Mathf.Clamp(p.all.Count,3,18)*RowHeight+40;
            p.open=true;p.ShowAsDropDown(screen,new Vector2(Mathf.Max(screen.width,360),height));
            return p;
        }
        // Builds the picker without showing it (Show and the tests).
        public static SkillGraphOwnerPicker Create(IEnumerable<(int Key,string Name)> owners,int current,Action<int> pick)
        {
            var p=CreateInstance<SkillGraphOwnerPicker>();p.all=owners.ToList();p.current=current;p.pick=pick;Last=p;p.Build();
            return p;
        }

        private void Build()
        {
            var root=rootVisualElement;root.Clear();
            var sheet=AssetDatabase.LoadAssetAtPath<StyleSheet>(SkillGraphCanvas.StyleSheetPath);if(sheet!=null)root.styleSheets.Add(sheet);
            root.AddToClassList("sg-editor");root.AddToClassList("sg-picker");
            var head=new VisualElement();head.AddToClassList("sg-picker__head");root.Add(head);
            search=new TextField();search.AddToClassList("sg-picker__search");search.textEdition.placeholder=T("picker.search");search.textEdition.hidePlaceholderOnFocus=false;head.Add(search);
            count=new Label();count.AddToClassList("sg-picker__count");head.Add(count);
            list=new ListView{fixedItemHeight=RowHeight,selectionType=SelectionType.None,makeItem=MakeRow,bindItem=BindRow,style={flexGrow=1}};list.AddToClassList("sg-picker__list");root.Add(list);
            empty=new Label(T("picker.empty"));empty.AddToClassList("sg-picker__empty");root.Add(empty);
            search.RegisterValueChangedCallback(e=>SetSearch(e.newValue));
            search.RegisterCallback<KeyDownEvent>(OnKey,TrickleDown.TrickleDown);
            SetSearch("");
            root.schedule.Execute(()=>{search.Focus();ScrollToCursor();});
        }
        private VisualElement MakeRow()
        {
            var row=new Label();row.AddToClassList("sg-picker__item");
            row.RegisterCallback<PointerDownEvent>(e=>{if(e.button==0&&row.userData is int i){cursor=i;Choose();e.StopPropagation();}});
            return row;
        }
        private void BindRow(VisualElement e,int i)
        {
            var row=(Label)e;row.userData=i;row.text=Label(shown[i]);
            row.EnableInClassList("sg-picker__item--current",shown[i].Key==current);row.EnableInClassList("sg-picker__item--cursor",i==cursor);
        }
        // Filters the list. The cursor starts on the current owner when it is still listed, otherwise on the first row.
        public void SetSearch(string query)
        {
            if(search!=null&&search.value!=query)search.SetValueWithoutNotify(query);
            shown=Filter(all,query);cursor=Math.Max(0,shown.FindIndex(o=>o.Key==current));
            count.text=F("picker.count",shown.Count,all.Count);
            empty.style.display=shown.Count==0?DisplayStyle.Flex:DisplayStyle.None;list.style.display=shown.Count==0?DisplayStyle.None:DisplayStyle.Flex;
            list.itemsSource=shown;list.RefreshItems();ScrollToCursor();
        }
        public void Move(int delta)
        {
            if(shown.Count==0)return;
            cursor=Mathf.Clamp(cursor+delta,0,shown.Count-1);list.RefreshItems();ScrollToCursor();
        }
        private void ScrollToCursor(){if(list.panel!=null&&cursor>=0&&cursor<shown.Count)list.ScrollToItem(cursor);}
        // Picks the row under the cursor: closes the drop-down, then hands the key to the editor.
        public void Choose()
        {
            if(cursor<0||cursor>=shown.Count)return;
            int key=shown[cursor].Key;var then=pick;
            Dismiss();then?.Invoke(key);
        }
        // A picker built without Show (tests) has no host window to close.
        private void Dismiss(){if(open)Close();else DestroyImmediate(this);}
        private void OnKey(KeyDownEvent e)
        {
            switch(e.keyCode)
            {
                case KeyCode.DownArrow:Move(1);break;
                case KeyCode.UpArrow:Move(-1);break;
                case KeyCode.PageDown:Move(10);break;
                case KeyCode.PageUp:Move(-10);break;
                case KeyCode.Return:case KeyCode.KeypadEnter:Choose();break;
                case KeyCode.Escape:Dismiss();break;
                default:return;
            }
            e.StopPropagation();
        }
        private void OnDestroy(){if(Last==this)Last=null;Closed?.Invoke();}
    }
}
