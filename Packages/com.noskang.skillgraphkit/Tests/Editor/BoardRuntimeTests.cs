using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static Noskang.SkillGraph.ModuleSet;

namespace Noskang.SkillGraph.Tests
{
    // Boards at run time on a small made-up module set and hand-written logic data (no demo, no Python).
    public sealed class BoardRuntimeTests
    {
        // Skill 100: at cast start BoardCheck Combo >= 2 -> pass: BoardClear Combo, fail: spawn effect 200.
        // Effect 200 on hit: BoardAdd Combo +1 (max 3), then Deal reads Combo (Board source) and writes what it dealt to Hit.Dealt (Cast).
        private static ModuleSet Set()=>BoardModules.AddTo(new Builder()
            .Events("CastStart","Create","Hit","CheckPass","CheckFail")
            .Kind("Skill","CastStart","CheckPass","CheckFail").Kind("Effect","Create","Hit")
            .Exits("CheckPass","CheckFail").Start("CastStart")
            .Module("Spawn",ModuleLine.Logic,new[]{"CastStart","CheckPass","CheckFail"},Slot("BundleKey","int","",SlotRole.Structure,null))
            .Module("Deal",ModuleLine.Logic,new[]{"Hit"},Slot("Amount","float","",SlotRole.Balance,null),Slot("AmountTo","board","",SlotRole.Structure,""))).Build();

        private static string Node(int id,int e,string from,int order,int module,string links)
            =>$"{{\"id\":{id},\"event\":{e},\"at\":null,\"from\":{from},\"priority\":0,\"order\":{order},\"module\":{module},\"links\":[{links}]}}";
        // Modules: Spawn 0, Deal 1, BoardSet 2, BoardAdd 3, BoardCheck 4, BoardClear 5. Events: CastStart 0, Create 1, Hit 2, CheckPass 3, CheckFail 4.
        private static SkillData Data(ModuleSet set)=>SkillData.Load(JsonWriter.Utf8("{\"format\":\"SkillGraph Logic 1\",\"dictionaryHash\":\""+set.Hash+"\","+
            "\"boardKeys\":[{\"name\":\"Combo.Count\",\"type\":\"int\",\"scope\":\"Actor\",\"default\":0},{\"name\":\"Hit.Dealt\",\"type\":\"float\",\"scope\":\"Cast\",\"default\":0.0}],"+
            "\"bundles\":[{\"key\":100,\"kind\":0,\"nodes\":["+Node(1,0,"null",10,4,"[0,\"board\",0],[2,\"fixed\",2]")+","+Node(2,3,"1",10,5,"[0,\"board\",0]")+","+Node(3,4,"1",10,0,"[0,\"fixed\",200]")+"]},"+
            "{\"key\":200,\"kind\":1,\"nodes\":["+Node(1,2,"null",10,3,"[0,\"board\",0],[3,\"fixed\",3]")+","+Node(2,2,"null",20,1,"[0,\"board\",0],[1,\"board\",1]")+"]}],"+
            "\"values\":[{\"key\":1,\"entries\":[]}],\"modifiers\":[],\"skills\":[{\"key\":1,\"bundle\":100,\"values\":1}]}"),set);

        private sealed class Game : IModuleHandler
        {
            public readonly List<SkillInstance> Effects=new List<SkillInstance>();
            public ModuleResult Run(ModuleCall call)
            {
                if(call.Node.Module.Name=="Spawn")Effects.Add(call.Runner.Spawn(call.Instance,call.Integer("BundleKey"),"Create"));
                if(call.Node.Module.Name=="Deal")call.Write("AmountTo",call.Number("Amount")*10);
                return ModuleResult.Continue;
            }
        }

        [Test]public void BoardKeepsTypedValuesAndReportsChanges()
        {
            var keys=new[]{new BoardKey(0,"Combo.Count","int","Actor",0),new BoardKey(1,"Combo.Ready","bool","Actor",false),
                           new BoardKey(2,"Combo.Name","text","Actor",""),new BoardKey(3,"Hit.Dealt","float","Cast",0.0)};
            var board=new Blackboard(keys,"Actor");var changes=new List<BoardChange>();board.Changed+=changes.Add;
            Assert.That(board.Keys.Select(k=>k.Name),Is.EqualTo(new[]{"Combo.Count","Combo.Name","Combo.Ready"}),"keys of its own scope, name order");
            Assert.That((board.Get("Combo.Count"),board.Get("Combo.Ready"),board.Get("Combo.Name")),Is.EqualTo(((object)0,(object)false,(object)"")),"defaults");
            board.Set("Combo.Count",2.6);board.Set("Combo.Ready",1);board.Set("Combo.Count",3);
            Assert.That((board.Get("Combo.Count"),board.Flag("Combo.Ready")),Is.EqualTo(((object)3,true)),"normalized to the key type");
            Assert.That(changes.Select(c=>(c.Key,c.Old,c.New)),Is.EqualTo(new[]{("Combo.Count",(object)0,(object)3),("Combo.Ready",false,true)}),"one change per key that moved (2.6 rounds to 3)");
            Assert.That(board.Has("Hit.Dealt"),Is.False);Assert.That(()=>board.Set("Hit.Dealt",1),Throws.TypeOf<KeyNotFoundException>());
            // Sync: send Values(), the other side loads them; unlisted keys go back to the default.
            var sent=board.Values();
            Assert.That(sent.Select(p=>p.Key),Is.EqualTo(new[]{"Combo.Count","Combo.Ready"}),"only keys away from their default");
            var other=new Blackboard(keys,"Actor");var seen=new List<string>();other.Changed+=c=>seen.Add(c.Key);
            other.Set("Combo.Name","x");seen.Clear();
            other.Load(sent.Concat(new[]{new KeyValuePair<string,object>("Old.Key",1)}));
            Assert.That((other.Get("Combo.Count"),other.Get("Combo.Ready"),other.Get("Combo.Name")),Is.EqualTo(((object)3,(object)true,(object)"")),"a round trip; unknown keys skipped");
            Assert.That(seen,Is.EquivalentTo(new[]{"Combo.Count","Combo.Ready","Combo.Name"}));
            other.Set("Combo.Count",1);seen.Clear();other.Load(sent,false);
            Assert.That((seen.Count,other.Integer("Combo.Count")),Is.EqualTo((0,3)),"notify: false loads silently");
            board.Clear("Combo.Count");Assert.That(board.Get("Combo.Count"),Is.EqualTo(0));
        }

        [Test]public void RunnerSharesTheCastBoardAndKeepsTheActorBoard()
        {
            var set=Set();Assert.That(set.Check(),Is.Empty);
            var data=Data(set);var game=new Game();var runner=new SkillRunner(data,BoardModules.Handler(game));
            var results=new List<string>();runner.NodeRun+=(i,n,r)=>{if(n.Module.Name==BoardModules.Check)results.Add(r.ToString());};
            var actor=data.NewBoard(SkillRunner.ActorScope);
            for(int cast=1;cast<=2;cast++)
            {
                var root=runner.Start(1,actor);var effect=game.Effects.Last();
                Assert.That(effect.Board,Is.SameAs(root.Board),"an effect shares the Cast board of its cast");
                Assert.That(effect.Actor,Is.SameAs(actor));
                runner.Raise(effect,"Hit");
                Assert.That(actor.Integer("Combo.Count"),Is.EqualTo(cast),"BoardAdd on the Actor board");
                Assert.That(root.Board.Number("Hit.Dealt"),Is.EqualTo(cast*10),"Deal read Combo (Board source) and wrote AmountTo");
            }
            var third=runner.Start(1,actor);
            Assert.That(results,Is.EqualTo(new[]{"Fail","Fail","Pass"}),"BoardCheck Combo >= 2 takes the exits");
            Assert.That((actor.Integer("Combo.Count"),game.Effects.Count,third.Board.Number("Hit.Dealt")),Is.EqualTo((0,2,0.0)),"pass clears; a new cast has a new Cast board");
            runner.Start(1);runner.Raise(game.Effects.Last(),"Hit");
            Assert.That(actor.Integer("Combo.Count"),Is.EqualTo(0),"without an actor board the cast gets its own");
        }

        [Test]public void BoardModulesRunOutsideTheSkillRunner()
        {
            // A host runner (e.g. a server) calls BoardModules.Run with its own boards.
            var set=Set();var data=Data(set);var actor=data.NewBoard("Actor");var cast=data.NewBoard("Cast");
            var scopes=new Scopes{Actor=actor,Cast=cast};var values=data.Owners[1].Values;
            var add=data.Bundles[200].Nodes.Single(n=>n.Module.Name==BoardModules.Add);var deal=data.Bundles[200].Nodes.Single(n=>n.Module.Name=="Deal");
            for(int i=0;i<5;i++)Assert.That(BoardModules.Run(add,values,scopes,out _),Is.True);
            Assert.That(actor.Integer("Combo.Count"),Is.EqualTo(3),"kept at Max");
            Assert.That((deal.BoardKey("AmountTo").Name,deal.BoardKey("Amount").Scope,deal.Number("Amount",values,scopes)),Is.EqualTo(("Hit.Dealt","Actor",3.0)));
            Assert.That(deal.Number("Amount",values),Is.EqualTo(0),"without scopes a Board slot reads the default");
            Assert.That(BoardModules.Run(deal,values,scopes,out _),Is.False,"not a board module");
            var check=data.Bundles[100].Nodes.Single(n=>n.Module.Name==BoardModules.Check);
            Assert.That(BoardModules.Run(check,values,scopes,out var r)&&r==ModuleResult.Pass,Is.True);
            Assert.That(BoardModules.Run(check,values,new Scopes(),out r)&&r==ModuleResult.Fail,Is.True,"no board for the scope: the check fails");
            Assert.That(BoardModules.Ops.Select(op=>BoardModules.Compare(2,op,2)),Is.EqualTo(new[]{true,false,true,false,true,false}));
        }
        private sealed class Scopes : IBoardScopes
        {
            public Blackboard Actor,Cast;
            public Blackboard Board(string scope)=>scope=="Actor"?Actor:scope=="Cast"?Cast:null;
        }

        [Test]public void DataWithoutBoardKeysHasNone()
        {
            var set=Set();
            var data=SkillData.Load(JsonWriter.Utf8("{\"format\":\"SkillGraph Logic 1\",\"dictionaryHash\":\""+set.Hash+"\",\"bundles\":[],\"values\":[],\"modifiers\":[]}"),set);
            Assert.That((data.BoardKeys.Count,data.NewBoard("Actor").Keys.Count),Is.EqualTo((0,0)));
        }
    }
}
