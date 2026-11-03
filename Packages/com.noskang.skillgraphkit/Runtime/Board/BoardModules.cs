using System;
using System.Collections.Generic;
using System.Linq;
using static Noskang.SkillGraph.ModuleSet;

namespace Noskang.SkillGraph
{
    // Standard board modules a host may add to its module set: BoardSet, BoardAdd, BoardCheck, BoardClear.
    //   declarations: BoardModules.AddTo(builder) after the builder's events and exits;  display: BoardModules.AddDisplay(displaySet)
    //   SkillRunner:  new SkillRunner(data, BoardModules.Handler(gameHandler))
    //   another runner: BoardModules.Run(node, values, scopes, out result) for each node it runs (false = not a board module).
    public static class BoardModules
    {
        public const string Set="BoardSet",Add="BoardAdd",Check="BoardCheck",Clear="BoardClear",OpEnum="BOARD_OP",Group="board",Family="Board";
        // BoardCheck operators; the editor shows them as ≥ > = ≠ ≤ <.
        public static readonly IReadOnlyList<string> Ops=Array.AsReadOnly(new[]{"AtLeast","Above","Equal","NotEqual","AtMost","Below"});
        private static readonly string[] Symbols={"≥",">","=","≠","≤","<"};
        private const SlotRole S=SlotRole.Structure;

        public static bool Is(string module)=>module==Set||module==Add||module==Check||module==Clear;

        // Declares the four modules (and the BOARD_OP enum). events: where they may go (none = every event declared so far).
        public static Builder AddTo(Builder b,params string[] events)
        {
            var on=events!=null&&events.Length>0?events:b.events.ToArray();
            b.Enum(OpEnum,Ops.ToArray());
            b.Module(new ModuleDeclaration(Set,ModuleLine.Logic,Group,on,new[]{Slot("Key","board:int,float,bool","",S,null),Slot("Value","float","",S,null)}));
            b.Module(new ModuleDeclaration(Add,ModuleLine.Logic,Group,on,
                new[]{Slot("Key","board:int,float","",S,null),Slot("Amount","float","",S,1.0),Slot("Min","float","",S,0.0),Slot("Max","float","",S,null)}));
            b.Module(new ModuleDeclaration(Check,ModuleLine.Logic,Group,on,
                new[]{Slot("Key","board:read,int,float,bool","",S,null),Slot("Op","enum:"+OpEnum,"",S,"AtLeast"),Slot("Value","float","",S,null)},new[]{"Pass","Fail"}));
            b.Module(new ModuleDeclaration(Clear,ModuleLine.Logic,Group,on,new[]{Slot("Key","board","",S,null)}));
            return b;
        }

        private static LocalText T(string en,string ko)=>new LocalText(en,ko);

        // Editor text for the four modules (family Board, operator symbols). Call where the host adds its own modules, in the same order.
        public static ModuleDisplaySet AddDisplay(ModuleDisplaySet d)
        {
            if(!d.Families.Any(f=>f.Name==Family))d.Family(Family,T("Board","보드"),"#3F8C8C","▣");
            d.EnumLabel(OpEnum,Ops.Select((op,i)=>(op,T(Symbols[i],Symbols[i]))).ToArray());
            d.Module(Set,T("Set board key","보드 키 넣기"),Family,T("Puts a value into a board key","보드 키에 값을 넣음"))
                .Slot("Key",T("Key","키"),T("Board key to set","넣을 보드 키"))
                .Slot("Value",T("Value","값"),T("New value (a bool key takes 0 or 1)","새 값(bool 키는 0 또는 1)"))
                .Stage("Basic","Key","Value");
            d.Module(Add,T("Add to board key","보드 키 더하기"),Family,T("Adds to a number key and keeps it between Min and Max","숫자 키에 더하고 최소~최대 사이로 둠"))
                .Slot("Key",T("Key","키"),T("Board key to change","바꿀 보드 키"))
                .Slot("Amount",T("Amount","더할 값"),T("Added each time (negative subtracts)","한 번에 더하는 값(음수는 뺌)"))
                .Slot("Min",T("Min","최소"),T("Lowest value","가장 작은 값"))
                .Slot("Max",T("Max","최대"),T("Highest value","가장 큰 값"))
                .Stage("Basic","Key","Amount","Min","Max");
            d.Module(Check,T("Check board key","보드 키 검사"),Family,T("Compares a key with a value: pass or fail","키를 값과 비교해 통과 또는 실패"))
                .Slot("Key",T("Key","키"),T("Board key to compare","비교할 보드 키"))
                .Slot("Op",T("Compare","비교"),T("How to compare","비교하는 방법"))
                .Slot("Value",T("Value","값"),T("Compared with","견줄 값"))
                .Stage("Basic","Key","Op","Value");
            d.Module(Clear,T("Clear board key","보드 키 비우기"),Family,T("Puts a key back to its default","키를 기본값으로 되돌림"))
                .Slot("Key",T("Key","키"),T("Board key to clear","비울 보드 키"))
                .Stage("Basic","Key");
            return d;
        }

        // Runs a board module node on the given boards. false = not a board module (the host runs it).
        // A key whose scope has no board: Set / Add / Clear do nothing, Check fails.
        public static bool Run(BoundNode node,ValueTable values,IBoardScopes scopes,out ModuleResult result)
        {
            result=ModuleResult.Continue;
            var name=node.Module.Name;
            if(!Is(name))return false;
            var key=node.BoardKey("Key");var board=key==null?null:scopes?.Board(key.Scope);
            switch(name)
            {
                case Set:board?.Set(key.Name,node.Number("Value",values,scopes));break;
                case Add:
                    if(board==null)break;
                    double v=board.Number(key.Name)+node.Number("Amount",values,scopes);
                    board.Set(key.Name,Math.Min(node.Number("Max",values,scopes),Math.Max(node.Number("Min",values,scopes),v)));
                    break;
                case Check:
                    result=board!=null&&Compare(board.Number(key.Name),node.Text("Op",values,scopes),node.Number("Value",values,scopes))?ModuleResult.Pass:ModuleResult.Fail;
                    break;
                case Clear:board?.Clear(key.Name);break;
            }
            return true;
        }

        public static bool Compare(double a,string op,double b)
        {
            const double e=1e-9;
            switch(op)
            {
                case "Above":return a>b+e;
                case "Equal":return Math.Abs(a-b)<=e;
                case "NotEqual":return Math.Abs(a-b)>e;
                case "AtMost":return a<=b+e;
                case "Below":return a<b-e;
                default:return a>=b-e;
            }
        }

        // A SkillRunner handler: board modules run here, every other module goes to next.
        public static IModuleHandler Handler(IModuleHandler next)=>new Chain(next);
        private sealed class Chain : IModuleHandler
        {
            private readonly IModuleHandler next;
            public Chain(IModuleHandler next){this.next=next;}
            public ModuleResult Run(ModuleCall call)
                =>BoardModules.Run(call.Node,call.Instance.Owner.Values,call.Instance,out var r)?r:next?.Run(call)??ModuleResult.Continue;
        }
    }
}
