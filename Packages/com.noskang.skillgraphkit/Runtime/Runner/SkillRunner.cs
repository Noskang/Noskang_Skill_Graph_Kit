using System;
using System.Collections.Generic;
using System.Linq;

namespace Noskang.SkillGraph
{
    public enum ModuleResult { Continue, Pass, Fail }

    // What a module handler sees: the running instance, the node and its slot values for the instance owner.
    // Board slots read the instance's boards; Write puts a value into the key a 'board' slot names.
    public readonly struct ModuleCall
    {
        public SkillInstance Instance { get; }
        public BoundNode Node { get; }
        public SkillRunner Runner=>Instance.Runner;
        public ModuleCall(SkillInstance instance,BoundNode node){Instance=instance;Node=node;}
        public double Number(string slot)=>Node.Number(slot,Instance.Owner.Values,Instance);
        public int Integer(string slot)=>Node.Integer(slot,Instance.Owner.Values,Instance);
        public string Text(string slot)=>Node.Text(slot,Instance.Owner.Values,Instance);
        public object Read(string slot)=>Node.Value(Node.Module.SlotIndex(slot),Instance.Owner.Values,Instance);
        // The key a board slot names and the board it lives on (null when the slot names none or its scope has no board).
        public BoardKey Key(string slot)=>Node.BoardKey(slot);
        public Blackboard Board(string slot){var k=Key(slot);return k==null?null:Instance.ScopeBoard(k.Scope);}
        // Writes value to the key a board slot names. false when the slot names no key (an optional write slot left empty).
        public bool Write(string slot,object value){var k=Key(slot);var b=Board(slot);if(b==null)return false;b.Set(k.Name,value);return true;}
    }

    // The host implements modules. Unknown modules can be ignored; exit modules return Pass or Fail.
    public interface IModuleHandler
    {
        ModuleResult Run(ModuleCall call);
    }

    // One running bundle: a cast of a skill, a spawned effect or an applied buff. Timed nodes fire when Time passes them.
    // Board: the Cast board (a cast and every effect it spawns share one). Actor: the character's board the game passed in.
    public sealed class SkillInstance : IBoardScopes
    {
        public SkillRunner Runner { get; }
        public SkillOwner Owner { get; }
        public BoundBundle Bundle { get; }
        public SkillInstance Parent { get; }
        public Blackboard Board { get; }
        public Blackboard Actor { get; }
        public double Time { get; private set; }
        public bool Ended { get; private set; }
        // Free slot for the host (e.g. the GameObject that shows an effect).
        public object Tag { get; set; }
        private readonly List<(double at,BoundNode node)> pending=new List<(double,BoundNode)>();

        // The board of a scope: Cast, Actor, or a host scope through SkillRunner.Scopes (null = none).
        public Blackboard ScopeBoard(string scope)=>scope==SkillRunner.CastScope?Board:scope==SkillRunner.ActorScope?Actor:Runner.Scopes?.Invoke(this,scope);
        Blackboard IBoardScopes.Board(string scope)=>ScopeBoard(scope);

        internal SkillInstance(SkillRunner runner,SkillOwner owner,BoundBundle bundle,SkillInstance parent,Blackboard board,Blackboard actor)
        {
            Runner=runner;Owner=owner;Bundle=bundle;Parent=parent;Board=board;Actor=actor;
            foreach(var n in bundle.Nodes)
                if(runner.Modules.IsTimed(n.Event))
                {
                    double at=n.Time(owner.Values);
                    if(!double.IsNaN(at))pending.Add((at,n));
                }
            pending.Sort((a,b)=>a.at!=b.at?a.at.CompareTo(b.at):0);
        }

        internal void Advance(double dt)
        {
            if(Ended)return;
            Time+=dt;
            while(pending.Count>0&&pending[0].at<=Time+1e-9)
            {
                var (_,node)=pending[0];
                pending.RemoveAt(0);
                Runner.RunNode(this,node);
            }
        }

        public void End(){Ended=true;pending.Clear();}
    }

    // A deliberately small, single-player, frame-stepped runner for demos and editor previews.
    // It knows nothing about any game: the host handles modules and raises events such as "hit" on instances.
    public sealed class SkillRunner
    {
        public const string CastScope="Cast",ActorScope="Actor";
        public SkillData Data { get; }
        public ModuleSet Modules=>Data.Modules;
        public IModuleHandler Handler { get; }
        public IReadOnlyList<SkillInstance> Running=>running;
        public event Action<SkillInstance,BoundNode,ModuleResult> NodeRun;
        // Boards of scopes other than Cast and Actor (settings board.scopes), asked per instance. Null = such keys read their default.
        public Func<SkillInstance,string,Blackboard> Scopes { get; set; }
        private readonly List<SkillInstance> running=new List<SkillInstance>();

        public SkillRunner(SkillData data,IModuleHandler handler){Data=data;Handler=handler;}

        // Starts an owner's bundle and raises the start event (e.g. CastStart). actor: the caster's board, kept by the game
        // (one per character); null = a new board for this cast only.
        public SkillInstance Start(int ownerKey,Blackboard actor=null)
        {
            var owner=Data.Owners.TryGetValue(ownerKey,out var o)?o:throw new KeyNotFoundException("Unknown owner: "+ownerKey);
            return Begin(owner,owner.Bundle,null,Modules.StartEvent,actor);
        }

        // Starts another bundle under the same owner values and boards (spawned effects). firstEvent: e.g. Create.
        public SkillInstance Spawn(SkillInstance parent,int bundleKey,string firstEvent)
        {
            var bundle=Data.Bundles.TryGetValue(bundleKey,out var b)?b:throw new KeyNotFoundException("Unknown bundle: "+bundleKey);
            return Begin(parent.Owner,bundle,parent,firstEvent,parent.Actor);
        }

        // A buff gets its own Cast board; actor: the board of the character that has the buff.
        public SkillInstance StartBuff(int buffKey,string firstEvent,Blackboard actor=null)
        {
            var owner=Data.Owners.TryGetValue(buffKey,out var o)?o:throw new KeyNotFoundException("Unknown owner: "+buffKey);
            return Begin(owner,owner.Bundle,null,firstEvent,actor);
        }

        private SkillInstance Begin(SkillOwner owner,BoundBundle bundle,SkillInstance parent,string firstEvent,Blackboard actor)
        {
            var instance=new SkillInstance(this,owner,bundle,parent,parent?.Board??Data.NewBoard(CastScope),actor??Data.NewBoard(ActorScope));
            running.Add(instance);
            if(firstEvent!=null)Raise(instance,firstEvent);
            return instance;
        }

        // Runs the nodes of one event. Exit events only run nodes raised by the given source node.
        public void Raise(SkillInstance instance,string e,int from=-1)
        {
            if(instance.Ended)return;
            foreach(var node in instance.Bundle.On(e,from).ToList())RunNode(instance,node);
        }

        internal void RunNode(SkillInstance instance,BoundNode node)
        {
            if(instance.Ended)return;
            var result=Handler?.Run(new ModuleCall(instance,node))??ModuleResult.Continue;
            NodeRun?.Invoke(instance,node,result);
            if(result!=ModuleResult.Continue&&Modules.ExitEvents.Count==2)
                Raise(instance,Modules.ExitEvents[result==ModuleResult.Pass?0:1],node.Id);
        }

        public void Tick(double dt)
        {
            foreach(var instance in running.ToList())instance.Advance(dt);
            running.RemoveAll(i=>i.Ended);
        }
    }
}
