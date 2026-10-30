using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using static Noskang.SkillGraph.Editor.SkillGraphText;

namespace Noskang.SkillGraph.Editor
{
    // Capture command: opens the editor in states described by a JSON spec and saves PNGs (for manuals and screenshots).
    //   Unity.exe -projectPath <project> -executeMethod Noskang.SkillGraph.Editor.SkillGraphCapture.Run -skillgraphCapture <spec.json>
    // spec: {"quit":true,"shots":[{"name","out","owner","layer","view":"Logic|Both|PresentationEdit","workspace","language":"en|ko","ops":[...],"expand":[bundle],
    //        "chips":["bundle/node/stage"],"select":{"bundle","node","bottom"},"slot","panel":"values|node|resources|none","size":[w,h],
    //        "frame":"all|select|main|box:<bundle>","zoom":scale,"issues":false,"scrollTo":value name,"panelWidth":points,
    //        "callouts":[{"n":1,"bundle","node"[,"slot"]}|{"n":2,"column","bundle"}|{"n":3,"target":"layer|views|panel|issues|toolbar|status"}|{"n":4,"rect":[x,y,w,h]}],
    //        "click": target or [targets] (red 'click here' box), "crop": {"around":"click"|target|[...],"margin":48,"min":[w,h]} or false,
    //        "open": {"menu":"more"|"node"|"column"|"value",...} | {"search":{"bundle","column"[,"bottom"]},"path":[...]} | {"window":"newSkill|newEffect|newBuff|addValue|editValue|duplicateSkill|childBundle|saveCombo|column|valueAction|save",...}}]}
    // Targets: callout targets, {"column","bundle","part":"plus"}, {"addColumn":bundle}, {"text","in","within","match","nth","up","fit"},
    //          {"field":label}, {"button":"ok|cancel|save"}, {"menuItem":text}, {"searchItem":text}, {"window":true}, {"menu":true}.
    // Context menus are native OS menus in Unity 6: they cannot be read from the window buffer and block the editor while open,
    // so the same items are drawn inside the window instead (capture report: menu = "drawn").
    // Needs a visible editor (batchmode has no window buffer). Operations stay on screen only; dialogs are never confirmed.
    public static class SkillGraphCapture
    {
        private sealed class Shot { public JsonNode Spec;public string Out; }
        private static List<Shot> shots;private static int index;private static int phase;private static double next;private static bool quit;private static SkillGraphEditorWindow window;private static readonly List<string> written=new List<string>();
        private static readonly List<object> report=new List<object>();private static HashSet<EditorWindow> before;private static readonly List<EditorWindow> aux=new List<EditorWindow>();
        private static Queue<string> keys;private static string menuMode;private static int searchRetry;private static string userLanguage;
        // 1 pt padding inside the box (3 pt covers neighbouring text); rows up to DenseRow pt high get the border half outside.
        public static readonly Color32 BoxColor=new Color32(0xE5,0x39,0x35,255);public const int BoxWidth=3,BoxPad=1,CropMargin=48,EdgeReach=24,DenseRow=26;
        public static string ReportPath=>Path.GetFullPath("Library/SkillGraphKit/capture-last.json");

        public static void Run()
        {
            var spec=SkillGraphPaths.Option("-skillgraphCapture");
            if(spec==null){Debug.LogError("[SkillGraph capture] -skillgraphCapture <spec.json> is required");return;}
            Start(spec);
        }
        [MenuItem("Tools/SkillGraph Kit/Run Capture Spec…",priority=20)]
        public static void RunFromMenu(){string p=EditorUtility.OpenFilePanel("Capture spec (JSON)",Path.GetFullPath("."),"json");if(p.Length>0)Start(p);}
        public static void Start(string specPath)
        {
            var spec=JsonNode.Parse(File.ReadAllBytes(specPath));
            quit=spec.Get("quit")?.Bool??false;
            string baseDir=Path.GetDirectoryName(Path.GetFullPath(specPath));
            shots=spec["shots"].AsArray().Select(x=>new Shot{Spec=x,Out=Path.GetFullPath(Path.Combine(baseDir,x["out"].AsText()))}).ToList();
            index=0;phase=0;next=EditorApplication.timeSinceStartup+2;written.Clear();report.Clear();aux.Clear();window=null;userLanguage=Language;
            EditorApplication.update-=Step;EditorApplication.update+=Step;
            Debug.Log("[SkillGraph capture] "+shots.Count+" shots: "+specPath);
        }
        private static void Step()
        {
            if(EditorApplication.timeSinceStartup<next)return;
            try
            {
                if(index>=shots.Count)
                {
                    EditorApplication.update-=Step;SetLanguage(userLanguage,false);Debug.Log("[SkillGraph capture] done: "+string.Join(", ",written));
                    Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
                    File.WriteAllText(ReportPath,SkillGraphJson.Write(new Dictionary<string,object>{{"written",written.Cast<object>().ToList()},{"shots",report}}),new UTF8Encoding(false));
                    if(quit)EditorApplication.Exit(0);return;
                }
                var shot=shots[index];var x=shot.Spec;
                if(phase==0)
                {
                    var size=x.Get("size") is JsonNode sz?new Vector2((float)sz[0].Number,(float)sz[1].Number):new Vector2(1600,1000);
                    // A floating window of our own, so its size can be set (docked windows cannot be resized).
                    if(window==null){window=ScriptableObject.CreateInstance<SkillGraphEditorWindow>();window.titleContent=new GUIContent(T("window.title"));window.Show();}
                    window.position=new Rect(40,40,size.x,size.y);window.Focus();
                    window.ApplyCapture(x);phase=1;next=EditorApplication.timeSinceStartup+2.5;return;
                }
                if(phase==1)
                {
                    window.FrameForCapture(x.Get("frame")?.TextOr("main")??"main",(float)(x.Get("zoom")?.Number??1));window.ScrollPanelForCapture(x);
                    phase=2;next=EditorApplication.timeSinceStartup+1.5;return;
                }
                if(phase==2)
                {
                    window.DrawCallouts(x.Get("callouts")?.AsArray()??Array.Empty<JsonNode>());window.Repaint();
                    CloseAux();before=new HashSet<EditorWindow>(Resources.FindObjectsOfTypeAll<EditorWindow>());keys=null;menuMode=null;searchRetry=0;
                    if(x.Get("open") is JsonNode o&&!o.IsNull){window.OpenForCapture(o,out keys,out menuMode);phase=3;next=EditorApplication.timeSinceStartup+1.2;}
                    else{phase=4;next=EditorApplication.timeSinceStartup+1.0;}
                    return;
                }
                if(phase==3)
                {
                    // Find the windows that opened (search, prompt, column or save window).
                    if(aux.Count==0)aux.AddRange(Resources.FindObjectsOfTypeAll<EditorWindow>().Where(w=>w!=null&&w!=window&&!before.Contains(w)));
                    if(keys!=null&&keys.Count>0)
                    {
                        var sw=aux.FirstOrDefault(w=>w is SearchWindow);
                        // A leftover search window makes SearchWindow.Open only close it; close leftovers and open once more.
                        if(sw==null&&searchRetry<1){searchRetry++;foreach(var w in Resources.FindObjectsOfTypeAll<SearchWindow>())w.Close();aux.Clear();before=new HashSet<EditorWindow>(Resources.FindObjectsOfTypeAll<EditorWindow>());window.OpenForCapture(x["open"],out keys,out menuMode);next=EditorApplication.timeSinceStartup+1.2;return;}
                        if(sw==null)throw new InvalidOperationException("Search window not found");
                        sw.SendEvent(Event.KeyboardEvent(keys.Dequeue()));sw.Repaint();next=EditorApplication.timeSinceStartup+0.35;return;
                    }
                    foreach(var w in aux)w.Repaint();phase=4;next=EditorApplication.timeSinceStartup+0.8;return;
                }
                window.Repaint();Write(window,x,shot.Out);written.Add(shot.Out);
                CloseAux();window.ClearMenuForCapture();window.DrawCallouts(Array.Empty<JsonNode>());
                Debug.Log("[SkillGraph capture] "+(index+1)+"/"+shots.Count+" "+shot.Out+(menuMode!=null?" (menu "+menuMode+")":""));
                index++;phase=0;next=EditorApplication.timeSinceStartup+0.5;
            }
            catch(Exception e){Debug.LogError("[SkillGraph capture] failed: "+e);EditorApplication.update-=Step;CloseAux();SetLanguage(userLanguage,false);if(quit)EditorApplication.Exit(3);}
        }
        private static void CloseAux(){foreach(var w in aux)if(w!=null)w.Close();aux.Clear();}

        // Compose the editor window with any window that opened, draw the click boxes, crop around them and save.
        private static void Write(SkillGraphEditorWindow w,JsonNode x,string path)
        {
            float ppp=EditorGUIUtility.pixelsPerPoint;var main=GrabCanvas(w);
            var layers=new List<(CaptureImage.Canvas,Vector2Int)>();var hosts=new List<EditorWindow>();
            foreach(var a in aux.Where(a=>a!=null)){try{layers.Add((GrabCanvas(a),Px(a.position.position-w.position.position,ppp)));hosts.Add(a);}catch(Exception e){Debug.LogWarning("[SkillGraph capture] cannot read window: "+a.GetType().Name+" "+e.Message);}}
            var (canvas,origin)=CaptureImage.Compose(main,layers,new Color32(0x1E,0x1F,0x22,255));
            RectInt ToCanvas(EditorWindow host,Rect local)
            {
                var off=host==null||host==w?Vector2.zero:host.position.position-w.position.position;
                return new RectInt(origin.x+Mathf.RoundToInt((local.x+off.x)*ppp),origin.y+Mathf.RoundToInt((local.y+off.y)*ppp),Mathf.RoundToInt(local.width*ppp),Mathf.RoundToInt(local.height*ppp));
            }
            var clicks=new List<RectInt>();
            if(x.Get("click") is JsonNode click&&!click.IsNull)
                foreach(var t in click.Kind==JsonKind.Array?click.AsArray():new[]{click})
                {
                    var hit=w.ResolveCaptureTarget(t,hosts);if(hit==null){Debug.LogWarning("[SkillGraph capture] click target not found: "+SkillGraphJson.Write(t));continue;}
                    clicks.Add(ToCanvas(hit.Value.Host,hit.Value.Rect));
                }
            int pad=Mathf.RoundToInt(BoxPad*ppp),width=Math.Max(1,Mathf.RoundToInt(BoxWidth*ppp));
            // Dense rows (slot rows, menu rows, fields, buttons) get the border half outside so neighbouring text stays visible.
            int dense=Mathf.RoundToInt(DenseRow*ppp);
            var boxes=CaptureImage.Boxes(clicks.Select(r=>CaptureImage.Grow(r,r.height<=dense?(width+1)/2:pad+width)).ToList(),0,canvas.Width,canvas.Height);
            foreach(var r in boxes)CaptureImage.DrawBox(canvas,r,width,BoxColor);
            var crop=CropRect(w,x,clicks,hosts,canvas,ppp,ToCanvas,out var around);
            if(crop!=null)
            {
                // Move crop edges that cut through text to the nearest blank line, never into the targets or boxes.
                var keep=CaptureImage.Union(around.Concat(boxes).ToList());
                canvas=CaptureImage.Crop(canvas,CaptureImage.CleanEdges(canvas,crop.Value,keep,Mathf.RoundToInt(EdgeReach*ppp)));
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,CaptureImage.EncodePng(canvas));
            report.Add(new Dictionary<string,object>{{"name",x.Get("name")?.TextOr(null)??Path.GetFileNameWithoutExtension(path)},{"out",path},{"size",new List<object>{canvas.Width,canvas.Height}},
                {"menu",menuMode},{"windows",hosts.Select(h=>(object)h.GetType().Name).ToList()},{"clicks",clicks.Select(r=>(object)new List<object>{r.x,r.y,r.width,r.height}).ToList()},{"ppp",(double)ppp}});
        }
        private static RectInt? CropRect(SkillGraphEditorWindow w,JsonNode x,List<RectInt> clicks,List<EditorWindow> hosts,CaptureImage.Canvas canvas,float ppp,Func<EditorWindow,Rect,RectInt> toCanvas,out List<RectInt> around)
        {
            around=new List<RectInt>();
            var crop=x.Get("crop");bool has=crop!=null;
            if(has&&crop.Kind==JsonKind.Bool&&!crop.Bool||!has&&clicks.Count==0)return null;
            int margin=has&&crop.Kind==JsonKind.Object&&crop.Get("margin") is JsonNode m?m.AsInt():CropMargin;
            var spec=has&&crop.Kind==JsonKind.Object?crop.Get("around"):null;
            foreach(var t in spec==null?new JsonNode[0]:spec.Kind==JsonKind.Array?spec.AsArray().ToArray():new[]{spec})
            {
                if(t.Kind==JsonKind.String&&t.AsText()=="click"){around.AddRange(clicks);continue;}
                var hit=w.ResolveCaptureTarget(t,hosts);if(hit!=null)around.Add(toCanvas(hit.Value.Host,hit.Value.Rect));
            }
            if(spec==null)around.AddRange(clicks);
            if(around.Count==0)return null;
            Vector2Int? min=has&&crop.Kind==JsonKind.Object&&crop.Get("min") is JsonNode mn?new Vector2Int(Mathf.RoundToInt((float)mn[0].Number*ppp),Mathf.RoundToInt((float)mn[1].Number*ppp)):(Vector2Int?)null;
            return CaptureImage.CropRect(canvas.Width,canvas.Height,around,Mathf.RoundToInt(margin*ppp),min);
        }
        private static Vector2Int Px(Vector2 points,float ppp)=>new Vector2Int(Mathf.RoundToInt(points.x*ppp),Mathf.RoundToInt(points.y*ppp));

        // Reads the window's own backbuffer through the internal GUIView.GrabPixels, so overlapping programs never get captured.
        // When the internal API is missing the capture fails rather than falling back to reading the screen.
        public static void Grab(EditorWindow w,string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,CaptureImage.EncodePng(GrabCanvas(w)));
        }
        public static CaptureImage.Canvas GrabCanvas(EditorWindow w)
        {
            const BindingFlags any=BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance;
            var parent=typeof(EditorWindow).GetField("m_Parent",any)?.GetValue(w)??throw new InvalidOperationException("Editor window view (m_Parent) not found");
            MethodInfo grab=null;for(var type=parent.GetType();type!=null&&grab==null;type=type.BaseType)grab=type.GetMethod("GrabPixels",any,null,new[]{typeof(RenderTexture),typeof(Rect)},null);
            if(grab==null)throw new InvalidOperationException("GUIView.GrabPixels not found (check the Unity version)");
            float ppp=EditorGUIUtility.pixelsPerPoint;var r=w.position;int width=Mathf.RoundToInt(r.width*ppp),height=Mathf.RoundToInt(r.height*ppp);
            // Copy the buffer values as they are (linear RT and texture, no colour space conversion, which would wash colours out).
            var rt=new RenderTexture(width,height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);rt.Create();
            grab.Invoke(parent,new object[]{rt,new Rect(0,0,width,height)});
            var previous=RenderTexture.active;RenderTexture.active=rt;
            var tex=new Texture2D(width,height,TextureFormat.RGB24,false,true);tex.ReadPixels(new Rect(0,0,width,height),0,0);tex.Apply();RenderTexture.active=previous;rt.Release();UnityEngine.Object.DestroyImmediate(rt);
            var px=tex.GetPixels32();UnityEngine.Object.DestroyImmediate(tex);
            // Texture rows go bottom-up; on APIs that read the buffer top-down (graphicsUVStartsAtTop) the rows are already top-down.
            return SystemInfo.graphicsUVStartsAtTop?new CaptureImage.Canvas(width,height,px):CaptureImage.Canvas.FromBottomUp(width,height,px);
        }
    }

    // Image helpers on a top-down pixel canvas: compose windows, draw boxes, crop. Kept apart from windows so tests run without a screen.
    public static class CaptureImage
    {
        public sealed class Canvas
        {
            public readonly int Width,Height;public readonly Color32[] Pixels;
            public Canvas(int width,int height,Color32[] pixels){Width=width;Height=height;Pixels=pixels;}
            public Canvas(int width,int height,Color32 fill){Width=width;Height=height;Pixels=Enumerable.Repeat(fill,width*height).ToArray();}
            public static Canvas FromBottomUp(int width,int height,Color32[] px){var top=new Color32[px.Length];for(int y=0;y<height;y++)Array.Copy(px,y*width,top,(height-1-y)*width,width);return new Canvas(width,height,top);}
            public Color32 this[int x,int y]=>Pixels[y*Width+x];
        }
        public static RectInt Grow(RectInt r,int by)=>new RectInt(r.x-by,r.y-by,r.width+by*2,r.height+by*2);
        public static RectInt Union(IReadOnlyList<RectInt> rs)
        {int x0=rs.Min(r=>r.x),y0=rs.Min(r=>r.y);return new RectInt(x0,y0,rs.Max(r=>r.xMax)-x0,rs.Max(r=>r.yMax)-y0);}
        // Click boxes: grow each target and clamp it inside the canvas (all four sides visible); overlapping boxes merge into one.
        public static List<RectInt> Boxes(IReadOnlyList<RectInt> targets,int by,int width,int height)
        {
            var list=targets.Select(r=>{var g=Grow(r,by);int x0=Math.Max(0,g.x),y0=Math.Max(0,g.y),x1=Math.Min(width,g.xMax),y1=Math.Min(height,g.yMax);return new RectInt(x0,y0,Math.Max(1,x1-x0),Math.Max(1,y1-y0));}).ToList();
            for(bool merged=true;merged;)
            {
                merged=false;
                for(int i=0;i<list.Count&&!merged;i++)for(int j=i+1;j<list.Count&&!merged;j++)
                    if(list[i].Overlaps(list[j])){list[i]=Union(new[]{list[i],list[j]});list.RemoveAt(j);merged=true;}
            }
            return list;
        }
        // A crop edge that cuts through text moves to the nearest blank line: outward first (up to reach), then inward
        // without entering keep, else it stays. Edges at the canvas border never move.
        public static RectInt CleanEdges(Canvas c,RectInt r,RectInt keep,int reach)
        {
            int x0=r.x,y0=r.y,x1=r.xMax,y1=r.yMax;
            if(y0>0)y0=Edge(y=>Blank(c,x0,x1,y,true),y0,-1,0,keep.y-1,reach);
            if(y1<c.Height)y1=Edge(y=>Blank(c,x0,x1,y,true),y1-1,1,c.Height-1,keep.yMax,reach)+1;
            if(x0>0)x0=Edge(x=>Blank(c,y0,y1,x,false),x0,-1,0,keep.x-1,reach);
            if(x1<c.Width)x1=Edge(x=>Blank(c,y0,y1,x,false),x1-1,1,c.Width-1,keep.xMax,reach)+1;
            return new RectInt(x0,y0,x1-x0,y1-y0);
        }
        private static int Edge(Func<int,bool> blank,int e,int dir,int outer,int inner,int reach)
        {
            if(blank(e))return e;
            for(int k=1;k<=reach;k++){int v=e+dir*k;if(dir<0?v<outer:v>outer)break;if(blank(v))return v;}
            for(int k=1;k<=reach;k++){int v=e-dir*k;if(dir<0?v>inner:v<inner)break;if(blank(v))return v;}
            return e;
        }
        // A line is blank when at most 0.2% (min 2) of its pixels differ from the line's median luminance by more than 48 (box pixels excluded).
        private static bool Blank(Canvas c,int from,int to,int at,bool row)
        {
            var hist=new int[256];int n=to-from;if(n<=0)return true;
            var lum=new int[n];
            for(int i=0;i<n;i++){var p=row?c[from+i,at]:c[at,from+i];lum[i]=(p.r*299+p.g*587+p.b*114)/1000;hist[lum[i]]++;}
            int med=0;for(int acc=0;med<255&&(acc+=hist[med])<(n+1)/2;med++){}
            int ink=0;
            for(int i=0;i<n;i++){var p=row?c[from+i,at]:c[at,from+i];var b=SkillGraphCapture.BoxColor;if(Math.Abs(lum[i]-med)>48&&!(p.r==b.r&&p.g==b.g&&p.b==b.b))ink++;}
            return ink<=Math.Max(2,n/500);
        }
        // Lays other window images over the main canvas at their offsets, growing the canvas as needed. Returns where main landed.
        public static (Canvas Canvas,Vector2Int Origin) Compose(Canvas main,IReadOnlyList<(Canvas Image,Vector2Int At)> layers,Color32 background)
        {
            int x0=0,y0=0,x1=main.Width,y1=main.Height;
            foreach(var (img,at) in layers){x0=Math.Min(x0,at.x);y0=Math.Min(y0,at.y);x1=Math.Max(x1,at.x+img.Width);y1=Math.Max(y1,at.y+img.Height);}
            var origin=new Vector2Int(-x0,-y0);var outCanvas=new Canvas(x1-x0,y1-y0,background);
            Blit(outCanvas,main,origin);foreach(var (img,at) in layers)Blit(outCanvas,img,origin+at);
            return (outCanvas,origin);
        }
        private static void Blit(Canvas dst,Canvas src,Vector2Int at){for(int y=0;y<src.Height;y++)Array.Copy(src.Pixels,y*src.Width,dst.Pixels,(y+at.y)*dst.Width+at.x,src.Width);}
        // Border only; parts outside the canvas are skipped.
        public static void DrawBox(Canvas c,RectInt r,int width,Color32 color)
        {
            for(int y=r.y;y<r.yMax;y++)for(int x=r.x;x<r.xMax;x++)
            {
                if(x<0||y<0||x>=c.Width||y>=c.Height)continue;
                if(x<r.x+width||x>=r.xMax-width||y<r.y+width||y>=r.yMax-width)c.Pixels[y*c.Width+x]=color;
            }
        }
        // Rectangle around all targets plus margin, grown to a minimum size around its centre, clamped to the canvas.
        public static RectInt CropRect(int width,int height,IReadOnlyList<RectInt> around,int margin,Vector2Int? min)
        {
            int x0=around.Min(r=>r.x)-margin,y0=around.Min(r=>r.y)-margin,x1=around.Max(r=>r.xMax)+margin,y1=around.Max(r=>r.yMax)+margin;
            if(min!=null)
            {
                if(x1-x0<min.Value.x){int g=min.Value.x-(x1-x0);x0-=g/2;x1+=g-g/2;}
                if(y1-y0<min.Value.y){int g=min.Value.y-(y1-y0);y0-=g/2;y1+=g-g/2;}
                // At a canvas edge, push to the other side to keep the minimum size.
                if(x0<0){x1=Math.Min(width,x1-x0);x0=0;}if(x1>width){x0=Math.Max(0,x0-(x1-width));x1=width;}
                if(y0<0){y1=Math.Min(height,y1-y0);y0=0;}if(y1>height){y0=Math.Max(0,y0-(y1-height));y1=height;}
            }
            x0=Math.Max(0,x0);y0=Math.Max(0,y0);x1=Math.Min(width,x1);y1=Math.Min(height,y1);
            return new RectInt(x0,y0,Math.Max(1,x1-x0),Math.Max(1,y1-y0));
        }
        public static Canvas Crop(Canvas c,RectInt r)
        {var px=new Color32[r.width*r.height];for(int y=0;y<r.height;y++)Array.Copy(c.Pixels,(r.y+y)*c.Width+r.x,px,y*r.width,r.width);return new Canvas(r.width,r.height,px);}
        public static byte[] EncodePng(Canvas c)
        {
            var bottom=new Color32[c.Pixels.Length];for(int y=0;y<c.Height;y++)Array.Copy(c.Pixels,y*c.Width,bottom,(c.Height-1-y)*c.Width,c.Width);
            var tex=new Texture2D(c.Width,c.Height,TextureFormat.RGB24,false,true);tex.SetPixels32(bottom);tex.Apply();
            try{return tex.EncodeToPNG();}finally{UnityEngine.Object.DestroyImmediate(tex);}
        }
    }

    public sealed partial class SkillGraphEditorWindow
    {
        private VisualElement callouts;private ToolbarMenu moreMenu;private DropdownMenu captureMenu;private Vector2 captureMenuAt;private VisualElement captureMenuView;
        // Applies a capture shot's state to the window (never saved). Operations only change what is on screen.
        public void ApplyCapture(JsonNode x)
        {
            // Every shot sets its language (default English) so shots never depend on the previous one; the user's choice is restored at the end.
            string before=Language;SetLanguage(x.Get("language")?.TextOr("en")??"en",false);
            if(before!=Language){titleContent=new GUIContent(T("window.title"));CreateGUI();}
            s.workspace=x.Get("workspace")?.TextOr("product")??"product";
            ops.Clear();redo.Clear();baseFiles=null;expanded.Clear();chips.Clear();selected=null;selectedSlot=null;runs=null;
            if(x.Get("ops") is JsonNode o)foreach(var op in o.AsArray())ops.Add(op.Keys.ToDictionary(k=>k,k=>(object)op.Fields[k]));
            s.owner=x.Get("owner")?.AsInt()??s.owner;s.layer=x.Get("layer") is JsonNode l&&!l.IsNull?l.AsInt():0;
            s.view=x.Get("view") is JsonNode v?(View)Enum.Parse(typeof(View),v.AsText()):View.Both;
            if(x.Get("expand") is JsonNode e)foreach(var k in e.AsArray())expanded.Add(k.AsInt());
            if(x.Get("chips") is JsonNode c)foreach(var k in c.AsArray())chips.Add(k.AsText());
            string panelTab=x.Get("panel")?.TextOr("values")??"values";s.panel=panelTab!="none";if(s.panel)s.tab=panelTab;
            s.sync=x.Get("sync")?.Bool??true;s.fold=x.Get("fold")?.Bool??false;
            if(x.Get("runlog") is JsonNode rl)runs=CountRunLog(File.ReadLines(Path.GetFullPath(rl.AsText())));
            // "issues": false hides the validation list for this shot (more room for the bottom graph).
            if(issueList?.parent!=null)issueList.parent.style.display=x.Get("issues") is JsonNode iv&&!iv.Bool?DisplayStyle.None:DisplayStyle.Flex;
            // "panelWidth": side panel width in points (default 330), e.g. to show every tier column.
            if(outer?.fixedPane!=null)outer.fixedPane.style.width=(float)(x.Get("panelWidth")?.Number??330);
            Refresh();
            var owner=model?.OwnerOf(s.owner);if(owner!=null&&s.layer==0)s.layer=owner.Bundle;
            if(x.Get("select") is JsonNode sel&&!sel.IsNull)
            {
                bool bottomSel=sel.Get("bottom")?.Bool??false;
                var card=layout.Cards.FirstOrDefault(k=>k.Bundle==sel["bundle"].AsInt()&&k.Node==sel["node"].AsInt()&&k.Presentation==bottomSel);
                if(card!=null)
                {
                    selected=card;selectedSlot=x.Get("slot")?.TextOr(null);
                    if(selectedSlot!=null){var n=Node(card);var sv=n?.Slot(selectedSlot);if(sv!=null&&sv.Source=="ref")MarkValue(SkillGraphModel.Text(sv.Value));}
                    // Selecting clears the chosen slot in OnSelect, so put it back.
                    string keepSlot=selectedSlot;(bottomSel?bottom:top).Select(card);selectedSlot=keepSlot;Highlight(card);if(s.panel)s.tab=panelTab;
                }
            }
            ApplyView();ApplyPanel();BuildPanel();BuildToolbar();
        }
        // Value cell of row "name" in tier column "stage" (1-based, default: the current tier) and that column's owner.
        private SkillGraphModel.Owner ValueCellForCapture(JsonNode o,out Label cell)
        {
            var nameLabel=panelBody.Query<Label>(className:"sg-vtable__name").ToList().FirstOrDefault(l=>l.text==o["name"].AsText())??throw new InvalidOperationException("No such value row: "+o["name"].AsText());
            var cells=nameLabel.parent.Query<Label>(className:"sg-vtable__cell").ToList();var current=model.OwnerOf(s.owner);
            var stageOwners=current.Stage!=null&&current.Stage.Family.Length>1?current.Stage.Family.Select(k=>model.OwnerOf(k)).Where(x=>x!=null).ToList():new List<SkillGraphModel.Owner>{current};
            int col=o.Get("stage") is JsonNode st?st.AsInt()-1:Math.Max(0,stageOwners.FindIndex(x=>x.Key==current.Key));
            cell=cells[col];return stageOwners[col];
        }
        // "scrollTo": scroll the values tab until that row shows.
        public void ScrollPanelForCapture(JsonNode x)
        {
            if(!(x.Get("scrollTo") is JsonNode st))return;
            var label=panelBody.Query<Label>(className:"sg-vtable__name").ToList().FirstOrDefault(l=>l.text==st.AsText());
            if(label==null)throw new InvalidOperationException("No such value row: "+st.AsText());
            ((ScrollView)panelBody).ScrollTo(label.parent.parent);
        }
        public void FrameForCapture(string how,float zoom=1f)
        {
            if(layout==null)return;
            if(how=="select"&&selected!=null){(selected.Presentation?bottom:top).FrameSelection();return;}
            if(how=="all"){top.FrameAll();bottom.FrameAll();return;}
            // Default: 100% (or zoom) with the main box's top-left in view; "box:<bundle>" aims at another box.
            var main=how.StartsWith("box:",StringComparison.Ordinal)?layout.Boxes.FirstOrDefault(b=>b.Bundle==int.Parse(how.Substring(4),CultureInfo.InvariantCulture)):layout.Boxes.FirstOrDefault(b=>b.Main);if(main==null)return;
            top.UpdateViewTransform(new Vector3(-main.Top.x*zoom+16,-main.Top.y*zoom+34,0),Vector3.one*zoom);
            bottom.UpdateViewTransform(new Vector3(-main.Top.x*zoom+16,-main.Bottom.y*zoom+(s.fold?26:34),0),Vector3.one*zoom);
        }
        // Numbered callouts: yellow frame and number over nodes, columns, toolbar or panel.
        public void DrawCallouts(IReadOnlyList<JsonNode> list)
        {
            callouts?.RemoveFromHierarchy();callouts=null;if(list.Count==0)return;
            callouts=new VisualElement{pickingMode=PickingMode.Ignore};callouts.style.position=Position.Absolute;callouts.style.left=0;callouts.style.top=0;callouts.style.right=0;callouts.style.bottom=0;
            rootVisualElement.Add(callouts);
            foreach(var c in list)
            {
                Rect? world=CalloutRect(c,out bool row);
                if(world==null)continue;
                var local=rootVisualElement.WorldToLocal(world.Value);
                // Slot rows and bottom column heads are not grown vertically, so the frame does not cover neighbouring rows.
                var frame=new VisualElement{pickingMode=PickingMode.Ignore};frame.AddToClassList("sg-callout");
                float pad=row?0:3;frame.style.left=local.x-3;frame.style.top=local.y-pad;frame.style.width=local.width+6;frame.style.height=local.height+pad*2;callouts.Add(frame);
                var num=new Label(c["n"].AsInt().ToString(CultureInfo.InvariantCulture)){pickingMode=PickingMode.Ignore};num.AddToClassList("sg-callout__num");num.style.left=Mathf.Max(2,local.x-14);num.style.top=Mathf.Max(2,local.y-14);callouts.Add(num);
            }
        }
        // Where a target sits in the window (world coordinates). row = a slot row or bottom column head (not grown vertically).
        private Rect? CalloutRect(JsonNode c,out bool row)
        {
            row=false;
            if(c.Get("rect") is JsonNode r)return new Rect((float)r[0].Number,(float)r[1].Number,(float)r[2].Number,(float)r[3].Number);
            if(c.Get("target") is JsonNode t)return t.AsText() switch{"layer"=>layerBar?.worldBound,"panel"=>panel?.worldBound,"issues"=>issueList?.parent?.worldBound,"toolbar"=>toolbar?.worldBound,"views"=>toolbar?.worldBound,"status"=>statusBar?.worldBound,_=>null};
            bool bottomSel=c.Get("bottom")?.Bool??false;var canvas=bottomSel?bottom:top;
            if(c.Get("node") is JsonNode n)
            {
                var card=layout.Cards.FirstOrDefault(k=>k.Bundle==c["bundle"].AsInt()&&k.Node==n.AsInt()&&k.Presentation==bottomSel);if(card==null)return null;
                // Measure the real card / row elements (layout rectangles sit a few points higher than the drawn border).
                var el=canvas.graphElements.ToList().OfType<CardElement>().FirstOrDefault(k=>ReferenceEquals(k.Card,card));
                var rect=card.Rect;
                if(c.Get("slot") is JsonNode sl)
                {
                    int i=card.Rows.FindIndex(x=>x.Slot==sl.AsText());
                    if(i>=0)
                    {
                        row=true;var rows=el?.Query(className:"sg-row").ToList();if(rows!=null&&i<rows.Count)return rows[i].worldBound;
                        rect=new Rect(rect.x,rect.y+SkillGraphLayout.HeadRow+(card.Memo!=null?SkillGraphLayout.MemoRow:0)+i*SkillGraphLayout.Row,rect.width,SkillGraphLayout.Row);
                    }
                }
                else if(el!=null)return el.worldBound;
                return canvas.contentViewContainer.LocalToWorld(rect);
            }
            if(c.Get("column") is JsonNode col)
            {
                var box=layout.Boxes.FirstOrDefault(k=>k.Bundle==c["bundle"].AsInt());var cl=box?.Columns.FirstOrDefault(k=>k.Key==col.AsText());if(cl==null)return null;
                if(c.Get("part")?.TextOr("")=="plus"){var plus=canvas.HeadOf(cl)?.Q(className:"sg-colhead__add");return plus?.worldBound;}
                var rr=bottomSel?box.Bottom:box.Top;row=bottomSel;
                return canvas.contentViewContainer.LocalToWorld(bottomSel?new Rect(cl.X-4,rr.y+SkillGraphLayout.BottomTitle,SkillGraphLayout.NodeWidth+8,SkillGraphLayout.BottomHeader)
                    :new Rect(cl.X-4,rr.y+SkillGraphLayout.TitleHeight-4,SkillGraphLayout.NodeWidth+8,SkillGraphLayout.HeaderHeight+8));
            }
            if(c.Get("box") is JsonNode bx){var box=layout.Boxes.FirstOrDefault(k=>k.Bundle==bx.AsInt());return box==null?(Rect?)null:canvas.contentViewContainer.LocalToWorld(bottomSel?box.Bottom:box.Top);}
            if(c.Get("addColumn") is JsonNode ac)
            {var box=layout.Boxes.FirstOrDefault(k=>k.Bundle==ac.AsInt());if(box==null)return null;var area=canvas.contentViewContainer.LocalToWorld(box.Top);return FindText(top,T("box.addColumn"),"exact",0,area)?.worldBound;}
            return null;
        }
        // Find by text: a text element (toolbar button, tab, menu row). A styled button parent wins over its label.
        private static VisualElement FindText(VisualElement root,string text,string match,int nth,Rect? within)
        {
            if(root==null)return null;
            var hits=root.Query<TextElement>().ToList().Where(e=>e.resolvedStyle.display!=DisplayStyle.None&&e.worldBound.width>0&&Matches(e.text,text,match)
                &&(within==null||within.Value.Contains(e.worldBound.center))).ToList();
            if(nth>=hits.Count)return null;var hit=hits[nth];var p=hit.parent;
            return p!=null&&(p is ToolbarMenu||p is Button||p.ClassListContains("sg-tool"))?p:hit;
        }
        private static bool Matches(string have,string want,string match)
        {
            if(have==null)return false;have=have.Trim();
            return match switch{"prefix"=>have.StartsWith(want,StringComparison.Ordinal),"contains"=>have.Contains(want),_=>have==want};
        }
        // Click target -> (window holding it, rectangle inside that window). Host null = the editor window.
        // Window images start at the content's top-left, so UI element positions (panel space) are converted to content space.
        public (EditorWindow Host,Rect Rect)? ResolveCaptureTarget(JsonNode t,IReadOnlyList<EditorWindow> hosts)
        {
            var hit=ResolveRaw(t,hosts);if(hit==null)return null;
            return hit.Value.Item1==null?(null,rootVisualElement.WorldToLocal(hit.Value.Item2)):hit;
        }
        private (EditorWindow,Rect)? ResolveRaw(JsonNode t,IReadOnlyList<EditorWindow> hosts)
        {
            if(t.Get("text") is JsonNode text)
            {
                string match=t.Get("match")?.TextOr("exact")??"exact";int nth=t.Get("nth")?.AsInt()??0;
                string where=t.Get("in")?.TextOr("main")??"main";
                Rect? within=t.Get("within") is JsonNode wt?CalloutRect(wt,out _):null;
                if(where=="menu")return MenuItemRect(text.AsText(),match,hosts);
                VisualElement root=where switch{"toolbar"=>toolbar,"panel"=>panel,"status"=>statusBar,"issues"=>issueList?.parent,"layer"=>layerBar,_=>rootVisualElement};
                var e=FindText(root,text.AsText(),match,nth,within);if(e==null)return null;
                // "up": n = n parents up (e.g. slot label -> whole slot row).
                for(int up=t.Get("up")?.AsInt()??0;up>0&&e.parent!=null;up--)e=e.parent;
                // "fit": shrink a row-wide text element to its text width (left aligned).
                var bound=e.worldBound;
                if(t.Get("fit")?.Bool==true&&e is TextElement te)
                {var size=te.MeasureTextSize(te.text,0,VisualElement.MeasureMode.Undefined,0,VisualElement.MeasureMode.Undefined);bound.width=Mathf.Min(bound.width,size.x+te.resolvedStyle.paddingLeft+te.resolvedStyle.paddingRight+2);}
                return (null,bound);
            }
            if(t.Has("menu")&&captureMenuView!=null)return (null,captureMenuView.worldBound);
            if(t.Get("menuItem") is JsonNode mi)return MenuItemRect(mi.AsText(),t.Get("match")?.TextOr("prefix")??"prefix",hosts);
            if(t.Get("field") is JsonNode fl)return DialogRect(fl.AsText(),hosts);
            if(t.Get("button") is JsonNode bt)return DialogRect(bt.AsText(),hosts);
            if(t.Has("window")){var h=hosts.FirstOrDefault();return h==null?((EditorWindow,Rect)?)null:(h,new Rect(0,0,h.position.width,h.position.height));}
            if(t.Get("searchItem") is JsonNode si)return SearchRow(si.AsText(),t,hosts);
            var r=CalloutRect(t,out _);return r==null?((EditorWindow,Rect)?)null:(null,r.Value);
        }
        private static (EditorWindow,Rect)? DialogRect(string name,IReadOnlyList<EditorWindow> hosts)
        {
            foreach(var h in hosts)
            {
                var rects=h is Prompt p?p.CaptureRects:h is ColumnDialog d?d.CaptureRects:h is SkillGraphSaveDialog sd?sd.CaptureRects:null;
                if(rects!=null&&rects.TryGetValue(name,out var r))return (h,r);
            }
            return null;
        }
        // Menu row: in a real menu window its text element; in a drawn menu the row inside the editor window.
        private (EditorWindow,Rect)? MenuItemRect(string text,string match,IReadOnlyList<EditorWindow> hosts)
        {
            if(captureMenuView!=null){var e=FindText(captureMenuView,text,match,0,null);return e==null?((EditorWindow,Rect)?)null:(null,(e.parent==captureMenuView?e:e.parent).worldBound);}
            foreach(var h in hosts)
            {
                var e=FindText(h.rootVisualElement,text,match,0,null);if(e==null)continue;
                var row=e;while(row.parent!=null&&row.parent!=h.rootVisualElement&&row.parent.worldBound.height<=e.worldBound.height+12)row=row.parent;
                return (h,h.rootVisualElement.WorldToLocal(row.worldBound));
            }
            return null;
        }
        // Search window rows: the search window is IMGUI, so the row position is computed ("rows" = [first row y, row height], default [52, 20]).
        private static (EditorWindow,Rect)? SearchRow(string label,JsonNode t,IReadOnlyList<EditorWindow> hosts)
        {
            var sw=hosts.FirstOrDefault(h=>h is SearchWindow);if(sw==null||searchLevel==null)return null;
            int i=searchLevel.FindIndex(x=>x.StartsWith(label,StringComparison.Ordinal));if(i<0)return null;
            float y0=52,h=20;if(t.Get("rows") is JsonNode rows){y0=(float)rows[0].Number;h=(float)rows[1].Number;}
            return (sw,new Rect(1,y0+i*h,sw.position.width-2,h));
        }
        private static List<string> searchLevel;

        // Opens a menu, the search window or a dialog for a shot. Menus are drawn (OS menus cannot be captured); other windows really open.
        public void OpenForCapture(JsonNode o,out Queue<string> keyQueue,out string mode)
        {
            keyQueue=null;mode=null;captureMenu=null;searchLevel=null;
            bool bottomSel=o.Get("bottom")?.Bool??false;var canvas=bottomSel?bottom:top;
            if(o.Get("menu") is JsonNode menu)
            {
                string kind=menu.AsText();Rect anchor;var dm=new DropdownMenu();
                if(kind=="more"){anchor=moreMenu.worldBound;dm=moreMenu.menu;captureMenuAt=new Vector2(anchor.x,anchor.yMax);}
                else if(kind=="node")
                {
                    var card=layout.Cards.First(k=>k.Bundle==o["bundle"].AsInt()&&k.Node==o["node"].AsInt()&&k.Presentation==bottomSel);
                    anchor=canvas.contentViewContainer.LocalToWorld(card.Rect);OnNodeMenu(card,dm);captureMenuAt=new Vector2(anchor.x+40,anchor.y+20);
                }
                else if(kind=="column")
                {
                    var box=layout.Boxes.First(k=>k.Bundle==o["bundle"].AsInt());var col=box.Columns.First(k=>k.Key==o["column"].AsText());
                    anchor=canvas.HeadOf(col).worldBound;OnColumnMenu(box,col,dm);captureMenuAt=new Vector2(anchor.x+20,anchor.center.y);
                }
                else if(kind=="value")
                {
                    var owner=ValueCellForCapture(o,out var cell);
                    ValueMenuItems(owner,o["name"].AsText(),model.ResolveValue(owner.Values,o["name"].AsText()),(label,act,on)=>dm.AppendAction(label,_=>{},on?DropdownMenuAction.Status.Normal:DropdownMenuAction.Status.Disabled));
                    anchor=cell.worldBound;captureMenuAt=new Vector2(anchor.center.x,anchor.center.y);
                }
                else throw new InvalidOperationException("Unknown menu: "+kind);
                captureMenu=dm;mode="drawn";DrawMenuForCapture();return;
            }
            if(o.Get("search") is JsonNode se)
            {
                var box=layout.Boxes.First(k=>k.Bundle==se["bundle"].AsInt());var col=box.Columns.First(k=>k.Key==se["column"].AsText());
                bool b=se.Get("bottom")?.Bool??false;var plus=(b?bottom:top).HeadOf(col)?.Q(className:"sg-colhead__add");
                OnAdd(box,col,plus?.worldBound.center??Vector2.zero);
                // Each path step: arrow down to the entry, arrow right to enter it.
                var provider=CreateInstance<SkillGraphSearch>();provider.Init(this,box,col,b);var tree=provider.CreateSearchTree(new SearchWindowContext(Vector2.zero));
                keyQueue=new Queue<string>();int level=1,start=1;searchLevel=Children(tree,start,level);
                if(o.Get("path") is JsonNode path)
                    foreach(var step in path.AsArray())
                    {
                        var kids=Children(tree,start,level);int i=kids.FindIndex(x=>x.StartsWith(step.AsText(),StringComparison.Ordinal));if(i<0)throw new InvalidOperationException("No such search entry: "+step.AsText());
                        for(int k=0;k<i;k++)keyQueue.Enqueue("down");keyQueue.Enqueue("right");
                        start=IndexOfChild(tree,start,level,i)+1;level++;searchLevel=Children(tree,start,level);
                    }
                return;
            }
            if(o.Get("window") is JsonNode win)
            {
                string kind=win.AsText();
                if(kind=="newSkill"||kind=="newEffect"||kind=="newBuff"||kind=="addValue"||kind=="editValue"||kind=="duplicateSkill"||kind=="childBundle"||kind=="saveCombo")
                {
                    if(kind=="newSkill")NewSkill(false);else if(kind=="newEffect")NewEffectBundle();else if(kind=="newBuff")NewBuff();
                    else if(kind=="duplicateSkill")DuplicatePrompt(model.OwnerOf(s.owner),true);else if(kind=="childBundle")NewChildBundle();
                    else if(kind=="saveCombo")SaveCombo(layout.Cards.First(k=>k.Bundle==o["bundle"].AsInt()&&k.Node==o["node"].AsInt()&&k.Presentation==bottomSel));
                    else if(kind=="editValue")EditValuePrompt(model.OwnerOf(o.Get("owner")?.AsInt()??s.owner),o["name"].AsText());else AddValuePrompt(model.OwnerOf(s.owner));
                    var p=Prompt.Last;
                    if(o.Get("values") is JsonNode vals)p.SetCaptureValues(vals.Fields.ToDictionary(k=>k.Key,k=>k.Value.AsText()));CenterOnCapture(p);
                }
                else if(kind=="column")
                {
                    var box=layout.Boxes.First(k=>k.Bundle==o["bundle"].AsInt());var col=o.Get("column") is JsonNode cc&&!cc.IsNull?box.Columns.First(k=>k.Key==cc.AsText()):null;
                    ColumnDialog.Show(this,box,col,ColumnTarget(box));var d=ColumnDialog.Last;
                    d.SetCapture(o.Get("event")?.TextOr(null),o.Get("value")?.TextOr(null),o.Get("from")?.AsInt());CenterOnCapture(d);
                }
                else if(kind=="valueAction")
                {
                    // A value cell menu row that opens a prompt (rows that edit right away are refused).
                    var owner=ValueCellForCapture(o,out _);string name=o["name"].AsText(),item=o["item"].AsText();Action run=null;
                    ValueMenuItems(owner,name,model.ResolveValue(owner.Values,name),(label,act,on)=>{if(on&&label.StartsWith(item,StringComparison.Ordinal))run=act;});
                    if(run==null)throw new InvalidOperationException("No such value menu row: "+item);
                    var last=Prompt.Last;int count=ops.Count;run();
                    if(Prompt.Last==last||ops.Count!=count)throw new InvalidOperationException("Row does not open a prompt: "+item);
                    var p=Prompt.Last;if(o.Get("values") is JsonNode vals)p.SetCaptureValues(vals.Fields.ToDictionary(k=>k.Key,k=>k.Value.AsText()));CenterOnCapture(p);
                }
                else if(kind=="save")
                {
                    // Only the save window shows (its Save button does nothing here). Locks or validation errors fail the shot.
                    var res=Request("changes",ops);if(!res["ok"].Bool)throw new InvalidOperationException(res["error"].AsText());
                    if(res["blocked"].AsArray().Count>0||res["errors"].AsInt()>0)throw new InvalidOperationException("The save window cannot open (lock or validation errors)");
                    var lines=res["changes"].AsArray().Select(ChangeLine).ToList();if(res["aux"].Bool)lines.Add(T("save.auxLine"));
                    SkillGraphSaveDialog.Show(this,lines,()=>{});CenterOnCapture(SkillGraphSaveDialog.Last);
                }
                else throw new InvalidOperationException("Unknown window: "+kind);
                return;
            }
            throw new InvalidOperationException("open needs menu, search or window");
        }
        private static List<string> Children(List<SearchTreeEntry> tree,int start,int level)
        {
            var list=new List<string>();for(int i=start;i<tree.Count&&tree[i].level>=level;i++)if(tree[i].level==level)list.Add(tree[i].content.text);return list;
        }
        private static int IndexOfChild(List<SearchTreeEntry> tree,int start,int level,int nth)
        {
            int seen=-1;for(int i=start;i<tree.Count&&tree[i].level>=level;i++)if(tree[i].level==level&&++seen==nth)return i;return -1;
        }
        private void CenterOnCapture(EditorWindow w){var p=w.position;w.position=new Rect(position.x+(position.width-p.width)/2,position.y+(position.height-p.height)/2,p.width,p.height);}
        // Instead of the OS menu: the same items (names, disabled state, separators) drawn inside the window in Unity's menu style.
        public void DrawMenuForCapture()
        {
            ClearMenuForCapture();if(captureMenu==null)return;
            var view=new VisualElement{pickingMode=PickingMode.Ignore};view.style.position=Position.Absolute;view.style.backgroundColor=(Color)new Color32(0x2D,0x2D,0x2D,255);
            view.style.borderTopWidth=view.style.borderBottomWidth=view.style.borderLeftWidth=view.style.borderRightWidth=1;
            view.style.borderTopColor=view.style.borderBottomColor=view.style.borderLeftColor=view.style.borderRightColor=(Color)new Color32(0x19,0x19,0x19,255);
            view.style.paddingTop=view.style.paddingBottom=4;
            foreach(var item in captureMenu.MenuItems())
            {
                if(item is DropdownMenuSeparator){var line=new VisualElement{pickingMode=PickingMode.Ignore};line.style.height=1;line.style.marginTop=line.style.marginBottom=4;line.style.marginLeft=line.style.marginRight=8;line.style.backgroundColor=(Color)new Color32(0x45,0x45,0x45,255);view.Add(line);continue;}
                if(!(item is DropdownMenuAction a))continue;a.UpdateActionStatus(null);if((a.status&DropdownMenuAction.Status.Hidden)!=0)continue;
                bool off=(a.status&DropdownMenuAction.Status.Disabled)!=0;
                var label=new Label(((a.status&DropdownMenuAction.Status.Checked)!=0?"✓ ":"")+a.name){pickingMode=PickingMode.Ignore};
                label.style.height=22;label.style.paddingLeft=22;label.style.paddingRight=18;label.style.unityTextAlign=TextAnchor.MiddleLeft;label.style.fontSize=12;
                label.style.color=(Color)(off?new Color32(0x6E,0x6E,0x6E,255):new Color32(0xD2,0xD2,0xD2,255));view.Add(label);
            }
            var at=rootVisualElement.WorldToLocal(captureMenuAt);view.style.left=at.x;view.style.top=at.y;rootVisualElement.Add(view);captureMenuView=view;
            // Push the menu back inside the window when it overflows, like Unity menus do.
            view.RegisterCallback<GeometryChangedEvent>(_=>{var root=rootVisualElement.layout;var r=view.layout;
                if(r.xMax>root.width-4)view.style.left=Mathf.Max(4,root.width-r.width-4);if(r.yMax>root.height-4)view.style.top=Mathf.Max(4,root.height-r.height-4);});
        }
        public void ClearMenuForCapture(){captureMenuView?.RemoveFromHierarchy();captureMenuView=null;}
    }
}
