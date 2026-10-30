using System.Collections.Generic;
using System.IO;
using System.Linq;
using Noskang.SkillGraph;
using UnityEngine;

namespace SkillGraphDemo
{
    // Plays the demo skills from the exported data with the package's small runner. Press 1-9 to cast (hold for Charge Beam).
    // Everything is primitives and text: the point is that the timing and behaviour come from Excel + the graph, not from code.
    public sealed class DemoRunner : MonoBehaviour,IModuleHandler
    {
        private sealed class EffectView
        {
            public SkillInstance Instance;public GameObject Go;public float Life=1,Speed,Radius=0.5f,Width,Height,From,Until=-1;public bool Box;
            public readonly HashSet<Dummy> Hit=new HashSet<Dummy>();public Dummy Last;public Vector3 Dir;
        }
        private sealed class Dummy { public GameObject Go;public float Hp=100;public float Defense; }
        private sealed class Charging { public SkillInstance Instance;public BoundNode Node;public KeyCode Key;public float Start; }

        public SkillRunner Runner { get; private set; }
        public SkillData Data { get; private set; }
        private readonly List<EffectView> effects=new List<EffectView>();
        private readonly List<Dummy> dummies=new List<Dummy>();
        private readonly List<string> log=new List<string>();
        private readonly List<SkillOwner> skills=new List<SkillOwner>();
        private GameObject caster;private EffectView pending;private Charging charging;private KeyCode castKey;
        private Vector3 dashFrom,dashTo;private float dashTime,dashLeft;
        private float attackBonus;

        // Loads exported data: StreamingAssets/SkillData/skills.manifest.json -> logic file.
        public static SkillData LoadData(ModuleSet set,string folder)
        {
            var manifest=JsonNode.Parse(File.ReadAllBytes(Path.Combine(folder,"skills.manifest.json")));
            var presentation=JsonNode.Parse(File.ReadAllBytes(Path.Combine(folder,manifest["presentationFile"].AsText())));
            var names=presentation["valueNames"].AsArray().Select(x=>x.AsText()).ToList();
            return SkillData.Load(File.ReadAllBytes(Path.Combine(folder,manifest["logicFile"].AsText())),set,names);
        }

        private void Start()
        {
            Data=LoadData(new DemoModules().Build(),Path.Combine(Application.streamingAssetsPath,"SkillData"));
            Runner=new SkillRunner(Data,this);
            Runner.NodeRun+=(i,n,r)=>Log($"{i.Bundle.Key}#{n.Id} {n.Event} {n.Module.Name}{(r!=ModuleResult.Continue?" → "+r:"")}");
            skills.AddRange(Data.Owners.Values.Where(o=>o.List=="skills").OrderBy(o=>o.Key));
            caster=GameObject.CreatePrimitive(PrimitiveType.Capsule);caster.name="Caster";caster.transform.position=new Vector3(0,1,0);
            for(int i=0;i<3;i++)
            {
                var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name="Dummy "+(i+1);go.transform.position=new Vector3(3+i*2.5f,1,0);
                go.GetComponent<Renderer>().material.color=new Color(.7f,.3f,.3f);dummies.Add(new Dummy{Go=go});
            }
        }

        private void Log(string text){log.Add($"{Time.time:0.00}s  {text}");if(log.Count>14)log.RemoveAt(0);}

        private void Update()
        {
            for(int i=0;i<skills.Count&&i<9;i++)
            {
                var key=KeyCode.Alpha1+i;
                if(Input.GetKeyDown(key)){castKey=key;Log("cast "+skills[i].Key);Runner.Start(skills[i].Key);}
            }
            if(charging!=null)
            {
                float held=Time.time-charging.Start;var values=charging.Instance.Owner.Values;
                if(!Input.GetKey(charging.Key)||held>=charging.Node.Number("MaxTime",values))
                {
                    var c=charging;charging=null;bool pass=held>=c.Node.Number("MinTime",values);
                    Runner.Raise(c.Instance,Runner.Modules.ExitEvents[pass?0:1],c.Node.Id);
                }
            }
            if(dashLeft>0){dashLeft-=Time.deltaTime;caster.transform.position=Vector3.Lerp(dashTo,dashFrom,Mathf.Max(0,dashLeft)/dashTime);}
            Runner.Tick(Time.deltaTime);
            foreach(var e in effects.ToList())
            {
                if(e.Instance.Ended){Destroy(e.Go);effects.Remove(e);continue;}
                e.Go.transform.position+=e.Dir*e.Speed*Time.deltaTime;
                if(e.Instance.Time>=e.Life){Runner.Raise(e.Instance,"LifeEnd");e.Instance.End();continue;}
                bool active=e.Instance.Time>=e.From&&(e.Until<0||e.Instance.Time<=e.Until);
                if(!active)continue;
                foreach(var d in dummies)
                {
                    if(e.Hit.Contains(d))continue;
                    var delta=d.Go.transform.position-e.Go.transform.position;
                    bool inside=e.Box?Mathf.Abs(delta.x)<=e.Width/2+0.5f&&Mathf.Abs(delta.z)<=e.Height/2+0.5f:delta.magnitude<=e.Radius+0.5f;
                    if(!inside)continue;
                    e.Hit.Add(d);e.Last=d;Runner.Raise(e.Instance,"Hit");
                    if(e.Instance.Ended)break;
                }
            }
        }

        public ModuleResult Run(ModuleCall call)
        {
            if(call.Instance.Tag==null&&pending!=null){call.Instance.Tag=pending;pending.Instance=call.Instance;}
            var view=call.Instance.Tag as EffectView;
            switch(call.Node.Module.Name)
            {
                case "SpawnEffect":
                    int count=Mathf.Max(1,call.Integer("Count"));
                    for(int i=0;i<count;i++)
                    {
                        var origin=view!=null?view.Go.transform.position:caster.transform.position;
                        var dir=Quaternion.Euler(0,(i-(count-1)/2f)*(float)call.Number("AngleStep"),0)*Vector3.right;
                        var go=GameObject.CreatePrimitive(PrimitiveType.Sphere);go.transform.localScale=Vector3.one*0.4f;
                        go.transform.position=origin+dir*(float)call.Number("OffsetX");Destroy(go.GetComponent<Collider>());
                        pending=new EffectView{Go=go,Dir=dir};effects.Add(pending);
                        Runner.Spawn(call.Instance,call.Integer("BundleKey"),"Create");
                        pending=null;
                    }
                    break;
                case "SetLifetime":if(view!=null)view.Life=(float)call.Number("Duration");break;
                case "MoveProjectile":if(view!=null)view.Speed=(float)call.Number("Speed");break;
                case "HitShape":
                    if(view==null)break;
                    view.Box=call.Text("Shape")=="Box";view.Radius=(float)call.Number("Radius");view.Width=(float)call.Number("Width");view.Height=(float)call.Number("Height");
                    view.Go.transform.localScale=view.Box?new Vector3(view.Width,0.2f,view.Height):Vector3.one*Mathf.Max(0.2f,view.Radius*2);
                    break;
                case "HitWindow":if(view!=null){view.From=(float)call.Number("From");view.Until=(float)call.Number("Until");}break;
                case "Damage":
                    if(view?.Last==null)break;
                    float amount=(float)call.Number("Amount")*(1+attackBonus)-view.Last.Defense;
                    view.Last.Hp-=Mathf.Max(0,amount);Log($"{view.Last.Go.name} -{amount:0.#} ({view.Last.Hp:0.#} left)");
                    break;
                case "Destroy":call.Instance.End();break;
                case "ApplyStatus":
                    var buff=call.Integer("BuffKey");string target=call.Text("Target");
                    if(target=="Allies"&&view?.Last!=null)break;
                    var b=Runner.StartBuff(buff,"StatusApply");Log($"buff {buff} on {target} for {call.Number("Duration"):0.#}s");
                    StartCoroutine(Expire(b,(float)call.Number("Duration")));
                    break;
                case "StatModify":
                    if(call.Text("Stat")=="Attack")attackBonus+=(float)call.Number("Value");
                    break;
                case "SelfMove":
                    dashFrom=caster.transform.position;dashTo=dashFrom+Vector3.right*(float)call.Number("Distance");dashTime=dashLeft=Mathf.Max(0.01f,(float)call.Number("Duration"));
                    break;
                case "Charge":
                    charging=new Charging{Instance=call.Instance,Node=call.Node,Key=castKey,Start=Time.time};
                    break;
            }
            return ModuleResult.Continue;
        }

        private System.Collections.IEnumerator Expire(SkillInstance buff,float seconds)
        {
            yield return new WaitForSeconds(seconds);
            Runner.Raise(buff,"StatusExpire");buff.End();attackBonus=0;Log("buff expired");
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10,10,520,Screen.height-20));
            GUILayout.Label("SkillGraph Kit demo — press 1-9 (hold for Charge Beam)");
            for(int i=0;i<skills.Count&&i<9;i++)GUILayout.Label($"{i+1}  {skills[i].Key}");
            GUILayout.Space(8);
            foreach(var d in dummies)GUILayout.Label($"{d.Go.name}: {d.Hp:0.#} HP");
            GUILayout.Space(8);
            foreach(var line in log)GUILayout.Label(line);
            GUILayout.EndArea();
        }
    }
}
