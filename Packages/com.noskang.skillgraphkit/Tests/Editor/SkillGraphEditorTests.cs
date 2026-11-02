using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Noskang.SkillGraph.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Noskang.SkillGraph.Tests
{
    // Layout and editor rules on the demo edit model (Fixtures/demo_model.json, written by the Python backend from Samples~/Demo).
    // No Python is needed: the fixture is kept in sync by the Python test suite.
    public sealed class SkillGraphEditorTests
    {
        private static SkillGraphModel model;
        private static SkillGraphModel Model()
        {
            if(model!=null)return model;
            SkillGraphText.SetLanguage("en",false);
            var path=Path.Combine(SkillGraphPaths.PackageRoot,"Tests","Editor","Fixtures","demo_model.json");
            return model=SkillGraphModel.Parse(JsonNode.Parse(File.ReadAllBytes(path)));
        }
        private static SkillGraphLayout Layout(int owner,bool expandAll=false,HashSet<string> chips=null)
        {
            var m=Model();
            var o=new SkillGraphLayout.Options{Owner=owner,Layer=m.OwnerOf(owner).Bundle,OpenChips=chips??new HashSet<string>()};
            if(expandAll)foreach(var b in m.Bundles.Keys)o.Expanded.Add(b);
            return new SkillGraphLayout(m,o);
        }

        [Test]public void ModelReadsSettingsAndDisplay()
        {
            var m=Model();
            Assert.That(m.Issues.Where(i=>i.Level=="error"),Is.Empty);
            Assert.That((m.Owners.Count,m.Bundles.Count),Is.EqualTo((11,19)));
            Assert.That(m.Layers,Is.EqualTo(new[]{"Base","Owner skill","Tier"}));
            Assert.That((m.TimeUnit,m.TimeGrid),Is.EqualTo(("s",0.025)));
            Assert.That(m.TimedEvents,Is.EqualTo(new[]{"Moment","Alarm"}));
            Assert.That(m.ExitEvents,Is.EqualTo(new[]{"CheckPass","CheckFail"}));
            Assert.That(m.Families.Count,Is.EqualTo(6));
            Assert.That(m.Module("HitShape").Label.Get("ko"),Is.EqualTo("판정 모양"));
            Assert.That(m.Module("HitShape").Slot("Radius").Label.Get("en"),Is.EqualTo("Radius"));
            Assert.That(m.OwnerNumber(2100205),Is.EqualTo(2));
        }

        [Test]public void ColumnsCardsAndSpawnEdge()
        {
            var l=Layout(2100101);var main=l.Boxes.Single(b=>b.Main);
            Assert.That(main.Columns.Select(c=>c.Key),Is.EqualTo(new[]{"CastStart","Moment:@10:Swing"}),"start column, then the keyframe column");
            Assert.That((main.Columns[1].Label,main.Columns[1].KeyframePart,main.Columns[1].RestPart),Is.EqualTo(("Swing : 0.25s","Swing"," : 0.25s")));
            var effect=l.Boxes.Single(b=>b.Bundle==2200002);
            Assert.That(effect.Collapsed,Is.True,"effect boxes start collapsed");
            Assert.That(l.Edges.Count(e=>e.Kind=="spawn"&&e.ToBundle==2200002),Is.EqualTo(1));
            var open=Layout(2100101,true);var eb=open.Boxes.Single(b=>b.Bundle==2200002);
            Assert.That(eb.Collapsed,Is.False);
            Assert.That(eb.Columns.Select(c=>c.Key),Is.EqualTo(new[]{"Create","Hit"}));
            var inherited=l.Cards.Single(c=>c.Bundle==2200101&&c.Module=="CastLock");
            Assert.That(inherited.Inherited,Is.Not.Null,"inherited node from the base layer is tagged");
        }

        [Test]public void NoOverlapForEveryOwnerWithAllBoxesOpen()
        {
            foreach(var owner in Model().Owners)
            {
                var l=Layout(owner.Key,true);
                foreach(var line in new[]{false,true})
                {
                    var cards=l.Cards.Where(c=>c.Presentation==line).ToList();
                    for(int i=0;i<cards.Count;i++)for(int j=i+1;j<cards.Count;j++)
                        Assert.That(cards[i].Rect.Overlaps(cards[j].Rect),Is.False,$"{owner.Key}: {cards[i].Bundle}/{cards[i].Node} overlaps {cards[j].Bundle}/{cards[j].Node}");
                    var boxes=l.Boxes.Select(b=>line?b.Bottom:b.Top).Where(r=>r.width>0).ToList();
                    for(int i=0;i<boxes.Count;i++)for(int j=i+1;j<boxes.Count;j++)Assert.That(boxes[i].Overlaps(boxes[j]),Is.False,$"{owner.Key}: boxes overlap");
                }
                Assert.That(l.PresentationOutsideLogicColumns(),Is.Empty,owner.Key+": presentation cards share the logic columns");
            }
        }

        [Test]public void BuffBoxReadsItsOwnValues()
        {
            var l=Layout(2100204,true);
            var stat=l.Cards.Single(c=>c.Bundle==2200221&&c.Module=="StatModify");
            Assert.That(stat.Rows.Single(r=>r.Slot=="Value").Text,Is.EqualTo("Guard.Defense 10"),"the buff's own value bundle, not the skill's");
            Assert.That(l.Boxes.Single(b=>b.Main).Top.width,Is.GreaterThanOrEqualTo(SkillGraphLayout.NodeWidth*2),"one-column boxes are still two columns wide");
        }

        [Test]public void KindRulesFilterSlotsAndSuffix()
        {
            var l=Layout(2100102,true);
            var shape=l.Cards.Single(c=>c.Bundle==2200112&&c.Module=="HitShape");
            Assert.That(shape.Suffix,Is.EqualTo("Box"),"the kind value shows in the header");
            Assert.That(shape.Rows.Select(r=>r.Slot),Has.No.Member("Radius"),"circle-only slot hidden for a box");
            var m=Model();var node=m.Bundles[2200112].Node(2);
            Assert.That(SkillGraphLayout.Applicable(m.Module("HitShape"),node),Is.EquivalentTo(new[]{"Shape","Width","Height"}));
        }

        [Test]public void StageChipsFoldAndOpen()
        {
            var l=Layout(2100101);
            var motion=l.Cards.Single(c=>c.Bundle==2200101&&c.Module=="PlayMotion");
            var chip=motion.Chips.Single(c=>c.Stage=="Timing");
            Assert.That((chip.Unused,chip.Advanced),Is.EqualTo((true,true)),"unset advanced stage = unused chip");
            Assert.That(motion.Rows.Select(r=>r.Slot),Is.EqualTo(new[]{"PresentationKey"}));
            var opened=Layout(2100101,false,new HashSet<string>{SkillGraphLayout.Options.Chip(2200101,10,"Timing")});
            var again=opened.Cards.Single(c=>c.Bundle==2200101&&c.Module=="PlayMotion");
            Assert.That(again.Rows.Select(r=>r.Slot),Is.EqualTo(new[]{"PresentationKey","While","Until","LoopFrom","LoopPeriod"}));
            var shape=Layout(2100101,true).Cards.Single(c=>c.Bundle==2200002&&c.Module=="HitShape");
            Assert.That(shape.Chips,Is.Empty,"box-only stage is not shown for a circle");
        }

        [Test]public void ExitColumnsStaircase()
        {
            var l=Layout(2100205);var main=l.Boxes.Single(b=>b.Main);
            Assert.That(main.Columns.Select(c=>c.Key),Is.EqualTo(new[]{"CastStart","CheckPass:10","CheckFail:10"}),"exit columns follow their check node, pass first");
            var charge=l.Cards.Single(c=>c.Bundle==2200205&&c.Node==10&&!c.Presentation);
            var pass=l.Cards.Where(c=>c.Column=="CheckPass:10"&&!c.Presentation).OrderBy(c=>c.Rect.y).First();
            Assert.That(pass.Rect.y,Is.EqualTo(charge.Rect.y),"first exit card lines up with the check node");
            Assert.That(l.Edges.Count(e=>e.Kind=="pass"),Is.EqualTo(1));
            Assert.That(l.Edges.Count(e=>e.Kind=="fail"),Is.EqualTo(1));
            Assert.That(charge.Exits,Is.EqualTo(new[]{"Pass","Fail"}));
        }

        [Test]public void TierFamilyLayersAndRemovedNodes()
        {
            var m=Model();var t3=m.OwnerOf(2100203);
            Assert.That(t3.Stage.Family,Is.EqualTo(new[]{2100201,2100202,2100203}));
            Assert.That(t3.Chain,Is.EqualTo(new[]{2200001,2200201,2200203}));
            var l=Layout(2100203);
            var ghost=l.Cards.Single(c=>c.Bundle==2200203&&c.Node==12);
            Assert.That(ghost.Removed,Is.True,"node removed with '-' stays as a faded card");
            var added=l.Cards.Single(c=>c.Bundle==2200203&&c.Node==100);
            Assert.That(added.Added,Is.True);
            var t2=Layout(2100202);
            var row=t2.Cards.Single(c=>c.Bundle==2200202&&c.Node==11&&!c.Presentation).Rows.Single(r=>r.Slot=="AngleStep");
            Assert.That((row.Symbol,row.Text),Is.EqualTo(("←","Bolt.Spread 8deg")));
        }

        [Test]public void DropColumnAndEventEditable()
        {
            var m=Model();var l=Layout(2100201);var main=l.Boxes.Single(b=>b.Main);
            var col=SkillGraphLayout.DropColumn(main,main.Columns[1].X+SkillGraphLayout.NodeWidth/2+3);
            Assert.That(col.Key,Is.EqualTo(main.Columns[1].Key));
            Assert.That(SkillGraphEditorWindow.EventEditable(m.Bundles[2200201].Node(11),2200201),Is.True);
            Assert.That(SkillGraphEditorWindow.EventEditable(m.Bundles[2200202].Node(11),2200202),Is.False,"inherited node keeps its event on the tier layer");
            var nodes=SkillGraphEditorWindow.ColumnNodes(m,2200205,"CastStart");
            Assert.That(nodes.Select(n=>n.Id).OrderBy(x=>x),Is.EqualTo(new[]{1,10,11,12,13}),"column removal scope includes exit columns of removed checks");
        }

        [Test]public void RunLogOverlayCounts()
        {
            var runs=SkillGraphEditorWindow.CountRunLog(new[]{"{\"bundle\":2200101,\"node\":11}","","{\"bundle\":2200101,\"node\":11}","{\"bundle\":2200002,\"node\":3}"});
            Assert.That(runs[(2200101,11)],Is.EqualTo(2));
            var l=new SkillGraphLayout(Model(),new SkillGraphLayout.Options{Owner=2100101,Layer=2200101,Runs=runs});
            Assert.That(l.Cards.Single(c=>c.Bundle==2200101&&c.Node==11).Runs,Is.EqualTo(2));
        }

        [Test]public void ColumnValueParsingAndKeyframeValue()
        {
            Assert.That(SkillGraphEditorWindow.ParseColumnValue("0.5s"),Is.EqualTo(0.5));
            Assert.That(SkillGraphEditorWindow.ParseColumnValue("Bolt.Release"),Is.EqualTo("Bolt.Release"));
            Assert.That(SkillGraphModel.KeyframeValue("@10:Swing",0.1),Is.EqualTo("@10:Swing+0.1"));
            Assert.That(Model().TryKeyframe(2200101,"@10:Swing-0.05",out _,out var key),Is.True);
            Assert.That(key.Seconds,Is.EqualTo(0.2).Within(1e-9));
        }

        // A temporary settings file through SKILLGRAPH_CONFIG; the user's language choice and the variable are restored afterwards.
        private static void WithSettings(string json,Action body)
        {
            string before=Environment.GetEnvironmentVariable("SKILLGRAPH_CONFIG");
            bool hadPref=UnityEditor.EditorPrefs.HasKey(SkillGraphText.LanguagePref);string pref=UnityEditor.EditorPrefs.GetString(SkillGraphText.LanguagePref,"en");
            string file=Path.Combine(Path.GetTempPath(),"skillgraph-test-"+Guid.NewGuid().ToString("N")+".json");
            try{File.WriteAllText(file,json);Environment.SetEnvironmentVariable("SKILLGRAPH_CONFIG",file);SkillGraphSettings.Reload();body();}
            finally
            {
                Environment.SetEnvironmentVariable("SKILLGRAPH_CONFIG",before);SkillGraphSettings.Reload();
                if(hadPref)UnityEditor.EditorPrefs.SetString(SkillGraphText.LanguagePref,pref);else UnityEditor.EditorPrefs.DeleteKey(SkillGraphText.LanguagePref);
                SkillGraphText.ResetLanguage();File.Delete(file);
            }
        }

        [Test]public void SettingsTextOverridesExitNamesAndFirstLanguage()
        {
            var l=Layout(2100205);var charge=l.Cards.Single(c=>c.Bundle==2200205&&c.Node==10&&!c.Presentation);
            WithSettings("{\"language\":\"ko\",\"editor\":{\"text\":{\"ko\":{\"menu.reload\":\"다시 불러오기\",\"exit."+charge.Module+".CheckPass\":\"쏘기\",\"column.time.Moment\":\"\"}}}}",()=>
            {
                UnityEditor.EditorPrefs.DeleteKey(SkillGraphText.LanguagePref);SkillGraphText.ResetLanguage();
                Assert.That(SkillGraphText.Language,Is.EqualTo("ko"),"before the user picks one, the settings language is used");
                Assert.That(SkillGraphText.T("menu.reload"),Is.EqualTo("다시 불러오기"));
                Assert.That(SkillGraphText.T("menu.toProduct"),Is.EqualTo("제품 작업 공간으로"),"other keys keep the table");
                Assert.That(Model().ExitLabel(charge.Module,"CheckPass"),Is.EqualTo("쏘기"));
                Assert.That(Model().ExitLabel(charge.Module,"CheckFail"),Is.EqualTo(Model().EventLabel("CheckFail")),"no override: the event's name");
                Assert.That(Model().ExitLabel("NoSuchModule","CheckPass"),Is.EqualTo(Model().EventLabel("CheckPass")));
                Assert.That(Model().TimedColumnLine("Moment"),Is.EqualTo(""),"an empty override hides the line");
                Assert.That(Model().TimedColumnLine("Alarm"),Is.EqualTo(Model().EventLabel("Alarm")));
                SkillGraphText.SetLanguage("en",false);
                Assert.That(SkillGraphText.T("menu.reload"),Is.EqualTo("Reload (discard edits)"),"overrides are per language");
                SkillGraphText.ResetLanguage();
                Assert.That(SkillGraphText.Language,Is.EqualTo("en"),"the user's choice wins over the settings language");
            });
            WithSettings("{}",()=>{UnityEditor.EditorPrefs.DeleteKey(SkillGraphText.LanguagePref);SkillGraphText.ResetLanguage();Assert.That(SkillGraphText.Language,Is.EqualTo("en"),"default English");});
            SkillGraphText.SetLanguage("en",false);
        }

        [Test]public void SeparateDialogKeysDefaultToTheMenuText()
        {
            foreach(var lang in new[]{"en","ko"})
            {
                SkillGraphText.SetLanguage(lang,false);
                Assert.That(SkillGraphText.T("export.title"),Is.EqualTo(SkillGraphText.T("menu.export")));
                Assert.That(SkillGraphText.T("workspace.resetTitle"),Is.EqualTo(SkillGraphText.T("menu.resetPractice")));
                Assert.That(SkillGraphText.T("bundleDesc.title"),Is.EqualTo(SkillGraphText.T("menu.bundleDesc").TrimEnd('…')));
                Assert.That(SkillGraphText.T("bands.title"),Is.EqualTo(SkillGraphText.T("menu.bands").TrimEnd('…')));
                Assert.That(SkillGraphText.T("column.addHere"),Is.EqualTo(SkillGraphText.T("node.addHere")));
                Assert.That(SkillGraphText.T("values.addTitle"),Is.EqualTo(SkillGraphText.T("values.add")));
                Assert.That(SkillGraphText.T("values.titleTiers"),Is.EqualTo(SkillGraphText.T("values.title")));
                Assert.That(SkillGraphText.T("values.descField"),Is.EqualTo(SkillGraphText.T("values.desc")));
                Assert.That(SkillGraphText.T("bundleDesc.field"),Is.EqualTo(SkillGraphText.T("new.desc")));
                Assert.That(SkillGraphText.T("combo.desc"),Is.EqualTo(SkillGraphText.T("new.desc")));
            }
            SkillGraphText.SetLanguage("en",false);
        }

        [Test]public void DisplaySettingsBoxWidthUnitsAndSummaries()
        {
            var unit=new SkillGraphModel.SlotInfo{Name="Reach",Type="float",Unit="m"};var three=JsonNode.Parse("3");
            float Width(int span)=>SkillGraphLayout.BoxPad*2+span*SkillGraphLayout.NodeWidth+(span-1)*SkillGraphLayout.ColumnGap;
            void Check(int span,string reach,Func<string,bool> summary)
            {
                // Every open box is at least span columns wide (and exactly as wide as its columns beyond that).
                var open=Model().Owners.SelectMany(o=>Layout(o.Key,true).Boxes).Where(b=>!b.Collapsed).ToList();
                Assert.That(open.Any(b=>b.Columns.Count<span),Is.True,"some box is narrower than the minimum");
                foreach(var b in open)Assert.That(b.Top.width,Is.EqualTo(Width(Math.Max(span,b.Columns.Count))).Within(0.01),"box "+b.Bundle);
                var l=Layout(2100101);
                Assert.That(l.ValueText(unit,three),Is.EqualTo(reach));
                var box=Layout(2100101).Boxes.Single(b=>b.Bundle==2200002);Assert.That(box.Collapsed,Is.True);Assert.That(summary(box.Summary),Is.True,box.Summary);
            }
            WithSettings("{}",()=>
            {
                SkillGraphText.SetLanguage("en",false);
                Check(2,"3m",t=>t=="Duration 0.1s · Radius 1.2m · Amount 12 · nodes 3");
                Assert.That(SkillGraphSettings.NewSkillModuleParent,Is.Null);
            });
            WithSettings("{\"editor\":{\"boxMinColumns\":3,\"valueUnits\":[\"s\"],\"boxSummary\":[{\"module\":\"Damage\",\"slot\":\"Amount\",\"label\":\"Hit\"}],\"newSkillModuleParent\":2200001}}",()=>
            {
                SkillGraphText.SetLanguage("en",false);
                Check(3,"3",t=>t=="Hit 12 · nodes 3");
                Assert.That(SkillGraphSettings.NewSkillModuleParent,Is.EqualTo(2200001));
            });
            Assert.That(SkillGraphEditorWindow.KeyTail(2300201,2300204),Is.EqualTo("4"));
            Assert.That(SkillGraphEditorWindow.KeyTail(2300201,2300210),Is.EqualTo("10"));
            Assert.That(SkillGraphEditorWindow.KeyTail(5,5),Is.EqualTo("5"));
            SkillGraphText.SetLanguage("en",false);
        }

        [Test]public void CompactCardRangeAndPlainSlots()
        {
            // The motion node of bundle 2200101 with Until set to 0.4 (the first Until slot in the fixture).
            var path=Path.Combine(SkillGraphPaths.PackageRoot,"Tests","Editor","Fixtures","demo_model.json");
            string text=File.ReadAllText(path);int at=text.IndexOf("\"name\": \"Until\"",StringComparison.Ordinal);
            int src=text.IndexOf("\"source\": \"default\"",at,StringComparison.Ordinal),val=text.IndexOf("\"value\": 0",at,StringComparison.Ordinal);
            text=text.Substring(0,src)+"\"source\": \"fixed\""+text.Substring(src+"\"source\": \"default\"".Length,val-src-"\"source\": \"default\"".Length)+"\"value\": 0.4"+text.Substring(val+"\"value\": 0".Length);
            var m=SkillGraphModel.Parse(JsonNode.Parse(System.Text.Encoding.UTF8.GetBytes(text)));
            string Card()=>new SkillGraphLayout(m,new SkillGraphLayout.Options{Owner=2100101,Layer=m.OwnerOf(2100101).Bundle,CompactPresentation=true})
                .Cards.Single(c=>c.Bundle==2200101&&c.Node==10&&c.Presentation).Summary;
            WithSettings("{}",()=>{SkillGraphText.SetLanguage("en",false);var t=Card();Assert.That(t,Does.Not.Contain("0–0.4s"));Assert.That(t,Does.EndWith("2 slots set"));});
            WithSettings("{\"editor\":{\"compactCard\":{\"range\":[\"LoopFrom\",\"Until\"],\"plain\":[\"While\"]}}}",()=>
            {
                SkillGraphText.SetLanguage("en",false);
                var t=Card().Split(new[]{" · "},StringSplitOptions.None);
                Assert.That(t.Length,Is.EqualTo(3),string.Join("|",t));Assert.That(t[0],Does.StartWith("2500101"));Assert.That(t[1],Is.EqualTo("0–0.4s"));Assert.That(t[2],Is.EqualTo("2 slots set"));
            });
            SkillGraphText.SetLanguage("en",false);
        }

        [Test]public void ExportCommandsFromSettings()
        {
            WithSettings("{}",()=>Assert.That(SkillGraphSettings.ExportCommands(),Is.Null,"default: the package exporter"));
            WithSettings("{\"editor\":{\"export\":{\"confirm\":true,\"commands\":[[\"tools/build.py\",\"--out\",\"{out}\"],[\"tools/check.py\",\"{project}\"]]}}}",()=>
            {
                var cmds=SkillGraphSettings.ExportCommands();
                Assert.That(cmds.Count,Is.EqualTo(2));
                Assert.That(SkillGraphSettings.ExportConfirm,Is.True);
                Assert.That(SkillGraphSettings.NeedsFolder(cmds),Is.True);
                Assert.That(SkillGraphSettings.Expand(cmds[0],"D:/Out"),Is.EqualTo(new[]{"tools/build.py","--out","D:/Out"}));
                Assert.That(SkillGraphSettings.Expand(cmds[1],null)[1],Is.EqualTo(Path.GetFullPath(".")));
                Assert.That(SkillGraphSettings.NeedsFolder(cmds.Skip(1)),Is.False);
            });
            Assert.That(SkillGraphEditorWindow.QuoteArg("plain"),Is.EqualTo("plain"));
            Assert.That(SkillGraphEditorWindow.QuoteArg("a b"),Is.EqualTo("\"a b\""));
            Assert.That(SkillGraphEditorWindow.QuoteArg("C:\\x y\\"),Is.EqualTo("\"C:\\x y\\\\\""),"trailing backslash doubled before the closing quote");
            Assert.That(SkillGraphEditorWindow.QuoteArg("say \"hi\""),Is.EqualTo("\"say \\\"hi\\\"\""));
            Assert.That(SkillGraphEditorWindow.QuoteArg(""),Is.EqualTo("\"\""));
        }

        [Test]public void CaptureWindowLeavesTheEditorSessionAlone()
        {
            string p=Path.GetFullPath("Library/SkillGraphKit/session-product.json");bool had=File.Exists(p);string before=had?File.ReadAllText(p):null;
            const string saved="{\"workspace\":\"product\",\"ops\":\"[{\\\"op\\\":\\\"setValue\\\"}]\"}";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(p));File.WriteAllText(p,saved);
                var w=ScriptableObject.CreateInstance<SkillGraphEditorWindow>();
                try{Assert.That(w.Ops.Count,Is.EqualTo(1),"an editor window restores the session");}finally{UnityEngine.Object.DestroyImmediate(w);}
                File.WriteAllText(p,saved);
                var c=SkillGraphEditorWindow.CreateCaptureWindow();
                try{Assert.That(c.Ops.Count,Is.EqualTo(0),"a capture window starts from an empty session");}finally{UnityEngine.Object.DestroyImmediate(c);}
                Assert.That(File.ReadAllText(p),Is.EqualTo(saved),"and never writes it");
            }
            finally{if(had)File.WriteAllText(p,before);else File.Delete(p);}
        }
        [Test]public void RuntimeAssemblyIsInPlayerBuildsByDefault()
        {
            // The runtime assembly drops out of player builds only when a project defines SKILLGRAPH_EDITOR_ONLY.
            var asm=File.ReadAllText(Path.Combine(SkillGraphPaths.PackageRoot,"Runtime","Noskang.SkillGraph.asmdef"));
            StringAssert.Contains("UNITY_EDITOR || !SKILLGRAPH_EDITOR_ONLY",asm);
            var player=UnityEditor.Compilation.CompilationPipeline.GetAssemblies(UnityEditor.Compilation.AssembliesType.PlayerWithoutTestAssemblies).Select(a=>a.name);
            Assert.That(player,Does.Contain("Noskang.SkillGraph"));
        }

        [Test]public void TextTableCoversEveryKeyInBothLanguages()
        {
            foreach(var lang in new[]{"en","ko"})
            {
                SkillGraphText.SetLanguage(lang,false);
                Assert.That(SkillGraphText.T("window.title"),Is.Not.EqualTo("window.title"));
            }
            SkillGraphText.SetLanguage("en",false);
            var root=Path.Combine(SkillGraphPaths.PackageRoot,"Editor");
            var keys=Directory.GetFiles(root,"*.cs",SearchOption.AllDirectories).SelectMany(f=>System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(f),"\\b[TF]\\(\"([a-zA-Z.]+)\"").Cast<System.Text.RegularExpressions.Match>()).Select(x=>x.Groups[1].Value).Where(k=>!k.EndsWith(".")).Distinct();
            Assert.That(keys.Where(k=>!SkillGraphText.Has(k)),Is.Empty,"missing UI strings");
        }

        [Test]public void CaptureComposeBoxCropAndPng()
        {
            var bg=new Color32(30,30,30,255);var main=new CaptureImage.Canvas(40,30,bg);var pop=new CaptureImage.Canvas(10,10,new Color32(200,200,200,255));
            var (canvas,origin)=CaptureImage.Compose(main,new[]{(pop,new Vector2Int(-5,25))},new Color32(0,0,0,255));
            Assert.That((canvas.Width,canvas.Height,origin),Is.EqualTo((45,35,new Vector2Int(5,0))));
            Assert.That(canvas[0,25].r,Is.EqualTo(200));
            CaptureImage.DrawBox(canvas,new RectInt(10,5,10,8),2,SkillGraphCapture.BoxColor);
            Assert.That(canvas[10,5].r,Is.EqualTo(SkillGraphCapture.BoxColor.r));Assert.That(canvas[14,9].r,Is.EqualTo(30),"inside stays");
            var crop=CaptureImage.CropRect(canvas.Width,canvas.Height,new[]{new RectInt(10,5,10,8)},4,new Vector2Int(30,30));
            Assert.That((crop.width,crop.height),Is.EqualTo((30,30)));
            var png=CaptureImage.EncodePng(CaptureImage.Crop(canvas,crop));
            Assert.That(png.Take(4),Is.EqualTo(new byte[]{0x89,0x50,0x4E,0x47}));
        }

        [Test]public void CaptureBoxesMergeClampAndCleanEdges()
        {
            var boxes=CaptureImage.Boxes(new[]{new RectInt(0,0,10,10),new RectInt(8,8,10,10),new RectInt(50,50,5,5)},2,56,56);
            Assert.That(boxes.Count,Is.EqualTo(2),"overlapping boxes merge");
            Assert.That(boxes.All(b=>b.x>=0&&b.y>=0&&b.xMax<=56&&b.yMax<=56),Is.True,"boxes stay inside the canvas");
            var c=new CaptureImage.Canvas(60,60,new Color32(30,30,30,255));
            for(int x=0;x<60;x+=3)c.Pixels[20*60+x]=new Color32(250,250,250,255);  // a line of 'text' (a uniform line counts as blank)
            var r=CaptureImage.CleanEdges(c,new RectInt(10,20,30,20),new RectInt(20,30,5,5),6);
            Assert.That(r.y,Is.Not.EqualTo(20),"edge moved off the text line");
        }
    }
}
