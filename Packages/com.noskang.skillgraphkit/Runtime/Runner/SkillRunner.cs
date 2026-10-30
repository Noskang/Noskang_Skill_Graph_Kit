using System;
using System.Collections.Generic;
using System.Linq;

namespace Noskang.SkillGraph
{
    public enum ModuleResult { Continue, Pass, Fail }

    // What a module handler sees: the running instance, the node and its slot values for the instance owner.
    public readonly struct ModuleCall
    {
        public SkillInstance Instance { get; }
        public BoundNode Node { get; }
        public SkillRunner Runner=>Instance.Runner;
        public ModuleCall(SkillInstance instance,BoundNode node){Instance=instance;Node=node;}
        public double Number(string slot)=>Node.Number(slot,Instance.Owner.Values);
        public int Integer(string slot)=>Node.Integer(slot,Instance.Owner.Values);
        public string Text(string slot)=>Node.Text(slot,Instance.Owner.Values);
    }

    // The host implements modules. Unknown modules can be ignored; exit modules return Pass or Fail.
    public interface IModuleHandler
    {
        ModuleResult Run(ModuleCall call);
    }

    // One running bundle: a cast of a skill, a spawned effect or an applied buff. Timed nodes fire when Time passes them.
    public sealed class SkillInstance
    {
        public SkillRunner Runner { get; }
        public SkillOwner Owner { get; }
        public BoundBundle Bundle { get; }
        public SkillInstance Parent { get; }
        public double Time { get; private set; }
        public bool Ended { get; private set; }
        // Free slot for the host (e.g. the GameObject that shows an effect).
        public object Tag { get; set; }
        private readonly List<(double at,BoundNode node)> pending=new List<(double,BoundNode)>();

        internal SkillInstance(SkillRunner runner,SkillOwner owner,BoundBundle bundle,SkillInstance parent)
        {
            Runner=runner;Owner=owner;Bundle=bundle;Parent=parent;
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
        public SkillData Data { get; }
        public ModuleSet Modules=>Data.Modules;
        public IModuleHandler Handler { get; }
        public IReadOnlyList<SkillInstance> Running=>running;
        public event Action<SkillInstance,BoundNode,ModuleResult> NodeRun;
        private readonly List<SkillInstance> running=new List<SkillInstance>();

        public SkillRunner(SkillData data,IModuleHandler handler){Data=data;Handler=handler;}

        // Starts an owner's bundle and raises the start event (e.g. CastStart).
        public SkillInstance Start(int ownerKey)
        {
            var owner=Data.Owners.TryGetValue(ownerKey,out var o)?o:throw new KeyNotFoundException("Unknown owner: "+ownerKey);
            return Begin(owner,owner.Bundle,null,Modules.StartEvent);
        }

        // Starts another bundle under the same owner values (spawned effects, applied buffs). firstEvent: e.g. Create.
        public SkillInstance Spawn(SkillInstance parent,int bundleKey,string firstEvent)
        {
            var bundle=Data.Bundles.TryGetValue(bundleKey,out var b)?b:throw new KeyNotFoundException("Unknown bundle: "+bundleKey);
            return Begin(parent.Owner,bundle,parent,firstEvent);
        }

        public SkillInstance StartBuff(int buffKey,string firstEvent)
        {
            var owner=Data.Owners.TryGetValue(buffKey,out var o)?o:throw new KeyNotFoundException("Unknown owner: "+buffKey);
            return Begin(owner,owner.Bundle,null,firstEvent);
        }

        private SkillInstance Begin(SkillOwner owner,BoundBundle bundle,SkillInstance parent,string firstEvent)
        {
            var instance=new SkillInstance(this,owner,bundle,parent);
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
