using System.Collections.Generic;
using System.IO;
using System.Linq;
using Noskang.SkillGraph;
using Noskang.SkillGraph.Editor;
using NUnit.Framework;

namespace SkillGraphDemo.Tests
{
    public sealed class DemoTests
    {
        private static string Sample(string file)=>Path.Combine(SkillGraphPaths.PackageRoot,"Samples~","Demo","Data","Generated",file);
        private static string Project(string file)=>Path.GetFullPath(Path.Combine("SkillData","Generated",file));

        // C# declarations are the source of truth: their export must equal the files the Python demo generator writes.
        [Test]public void DeclarationsExportTheSameDictionaryBytes()
        {
            var provider=new DemoModules();var set=provider.Build();var display=provider.BuildDisplay(set);
            Assert.That(set.Check(),Is.Empty);
            Assert.That(display.Check(set),Is.Empty);
            var dict=JsonWriter.Utf8(set.ExportJson());
            Assert.That(dict,Is.EqualTo(File.ReadAllBytes(Sample("module_dictionary.json"))),"C# dictionary export = Samples~ dictionary");
            Assert.That(dict,Is.EqualTo(File.ReadAllBytes(Project("module_dictionary.json"))),"C# dictionary export = DemoProject dictionary");
            Assert.That(JsonWriter.Utf8(display.ExportJson(set.Hash)),Is.EqualTo(File.ReadAllBytes(Sample("module_display.json"))));
        }

        [Test]public void JsonNumbersMatchPython()
        {
            Assert.That(new[]{1.0,0.025,14.0,0.1,100.0,2.5,1e-05,1e16,-0.5}.Select(JsonWriter.Number),
                Is.EqualTo(new[]{"1.0","0.025","14.0","0.1","100.0","2.5","1e-05","1e+16","-0.5"}));
        }

        private sealed class Recorder : IModuleHandler
        {
            public readonly List<string> Calls=new List<string>();public ModuleResult Charge=ModuleResult.Pass;
            public ModuleResult Run(ModuleCall call)
            {
                Calls.Add(call.Instance.Bundle.Key+"#"+call.Node.Id+" "+call.Node.Module.Name);
                if(call.Node.Module.Name=="SpawnEffect")call.Runner.Spawn(call.Instance,call.Integer("BundleKey"),"Create");
                return call.Node.Module.Name=="Charge"?Charge:ModuleResult.Continue;
            }
        }
        private static SkillData Data()=>DemoRunner.LoadData(new DemoModules().Build(),Path.GetFullPath("Assets/StreamingAssets/SkillData"));

        [Test]public void RunnerFollowsTimesAndKeyframes()
        {
            var rec=new Recorder();var runner=new SkillRunner(Data(),rec);
            runner.Start(2100101);
            Assert.That(rec.Calls,Is.EqualTo(new[]{"2200101#1 CastLock"}),"cast start: only the logic node (presentation is separate data)");
            runner.Tick(0.2);Assert.That(rec.Calls.Count,Is.EqualTo(1));
            runner.Tick(0.05);
            Assert.That(rec.Calls.Skip(1).First(),Is.EqualTo("2200101#11 SpawnEffect"),"the hit spawns on the Swing keyframe (0.25 s)");
            Assert.That(rec.Calls,Has.Member("2200002#2 HitShape"),"the spawned effect ran its Create nodes");
        }

        [Test]public void ChargeExitsPickTheBranch()
        {
            foreach(var (result,effect) in new[]{(ModuleResult.Pass,2200215),(ModuleResult.Fail,2200216)})
            {
                var rec=new Recorder{Charge=result};var runner=new SkillRunner(Data(),rec);
                runner.Start(2100205);
                Assert.That(rec.Calls.Any(c=>c.StartsWith(effect+"#")),Is.True,result+" spawns "+effect);
            }
        }

        [Test]public void TierValuesAndModifiers()
        {
            var data=Data();var set=data.Modules;
            int damage=data.ValueNames.ToList().IndexOf("Bolt.Damage");
            Assert.That(data.Owners[2100201].Values.Number(damage),Is.EqualTo(25*1.1).Within(1e-9),"tier 1 with the +10% modifier");
            Assert.That(data.Owners[2100202].Values.Number(damage),Is.EqualTo(30*1.1).Within(1e-9),"tier 2 overrides the value");
            Assert.That(set.Hash,Is.EqualTo(JsonNode.Parse(File.ReadAllBytes(Path.GetFullPath("Assets/StreamingAssets/SkillData/skills.manifest.json")))["dictionarySha256"].AsText()));
        }
    }
}
