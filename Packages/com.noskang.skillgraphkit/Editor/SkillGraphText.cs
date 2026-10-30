using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;

namespace Noskang.SkillGraph.Editor
{
    // Editor UI strings in English and Korean. The language toggles from the toolbar and is kept per user (EditorPrefs).
    public static class SkillGraphText
    {
        public const string LanguagePref="Noskang.SkillGraph.Language";
        private static string language;
        public static string Language=>language??=EditorPrefs.GetString(LanguagePref,"en")=="ko"?"ko":"en";
        public static event Action LanguageChanged;
        public static void SetLanguage(string value,bool notify=true)
        {
            value=value=="ko"?"ko":"en";
            if(value==Language)return;
            language=value;EditorPrefs.SetString(LanguagePref,value);
            if(notify)LanguageChanged?.Invoke();
        }

        public static string T(string key)
        {
            if(!Table.TryGetValue(key,out var pair))return key;
            return Language=="ko"?pair.Ko:pair.En;
        }
        public static string F(string key,params object[] args)=>string.Format(CultureInfo.InvariantCulture,T(key),args);
        public static bool Has(string key)=>Table.ContainsKey(key);

        private static readonly Dictionary<string,(string En,string Ko)> Table=new Dictionary<string,(string,string)>
        {
            {"window.title",("Skill Graph Editor","스킬 그래프 편집기")},
            {"ok",("OK","확인")},{"cancel",("Cancel","취소")},
            {"pane.logic",("Logic","기능(판정)")},{"pane.presentation",("Presentation","연출")},
            {"kind.skill",("Skill","기술")},{"kind.effect",("Effect","효과 객체")},{"kind.buff",("Buff","버프")},
            {"line.logic",("logic","판정")},{"line.presentation",("presentation","연출")},
            {"stage.basic",("Basic","기본")},{"tier.n",("T{0}","{0}단계")},
            // toolbar
            {"toolbar.practice",("Practice","연습 공간")},{"toolbar.practiceTip",("You are editing a practice copy. Nothing here reaches the product data","연습용 사본을 고치는 중입니다. 여기서 고친 것은 제품 데이터에 들어가지 않습니다")},
            {"toolbar.ownerTip",("Choose a skill or buff","기술·버프 고르기")},
            {"view.logic",("Logic only","기능만")},{"view.logicTip",("Top (logic) graph only","위(기능) 창만")},
            {"view.both",("Both","둘 다")},{"view.bothTip",("Top 70 : bottom 30","위 70 : 아래 30")},
            {"view.presentation",("Presentation edit","연출 편집")},{"view.presentationTip",("Top 30 : bottom 70, logic dimmed","위 30 : 아래 70, 기능 흐리게")},
            {"view.fold",("Fold presentation","연출 접기")},{"view.unfold",("Unfold presentation","연출 펼치기")},{"view.foldTip",("Fold the bottom graph to one count line per column","아래 창을 열마다 개수 한 줄로 접기")},
            {"toolbar.sync",("Link pan","가로 연동")},{"toolbar.syncTip",("Pan and zoom the two graphs together","위아래 가로 이동·확대 연동")},
            {"toolbar.frame",("Frame","맞추기")},{"toolbar.frameTip",("Frame the selected node (F)","고른 노드에 맞추기(F)")},
            {"toolbar.all",("All","전체")},{"toolbar.allTip",("Frame everything (A)","전체 보기(A)")},
            {"toolbar.panel",("Panel","수치 패널")},{"toolbar.panelTip",("Side panel: values, node, resources","오른쪽 수치·노드 패널")},
            {"toolbar.undo",("Undo","되돌리기")},{"toolbar.undoTip",("Undo edit (Ctrl+Z)","편집 되돌리기(Ctrl+Z)")},
            {"toolbar.redo",("Redo","다시하기")},{"toolbar.redoTip",("Redo edit (Ctrl+Y)","편집 다시하기(Ctrl+Y)")},
            {"toolbar.save",("Save","저장")},{"toolbar.saveTip",("Review the change list, then write Excel","변경 목록 확인 뒤 Excel에 저장")},
            {"toolbar.more",("More","더 보기")},{"toolbar.languageTip",("Switch the editor language","편집기 글자 언어 바꾸기")},
            {"menu.export",("Build skill data (logic / presentation JSON)…","스킬 데이터 만들기(판정·연출 JSON)…")},
            {"menu.newSkill",("New skill…","새 기술 만들기…")},{"menu.newEffect",("New effect bundle…","새 효과 객체 묶음…")},{"menu.newBuff",("New buff…","새 버프 만들기…")},
            {"menu.duplicate",("Duplicate this skill…","이 기술 복제…")},{"menu.childBundle",("New bundle with this one as parent…","이 묶음을 부모로 새 묶음…")},
            {"menu.bundleDesc",("Edit bundle description…","묶음 설명 고치기…")},{"menu.bands",("Edit named bands…","구간 이름 띠 고치기…")},
            {"menu.toPractice",("Open practice workspace (product copy)","연습 공간 열기(제품 사본)")},{"menu.toProduct",("Back to the product workspace","제품 작업 공간으로")},
            {"menu.resetPractice",("Reset practice workspace","연습 공간 초기화")},{"menu.reload",("Reload (discard edits)","다시 읽기(편집 버림)")},
            {"menu.runLog",("Overlay a run log…","실행 기록 겹치기…")},{"menu.runLogOff",("Hide run log","실행 기록 끄기")},{"menu.runLogPick",("Run log (JSON Lines)","실행 기록(JSON Lines)")},
            // layers
            {"layer.title",("Edit layer","편집 층")},{"layer.tip",("Edits go to this layer. Skills affected: {0}","고른 층에 씁니다. 영향 기술 {0}개")},
            {"layer.tierTip",("Tier {0} {1} — edits go to this layer","{0}단계 {1} — 이 층에 씁니다")},
            {"layer.changed",("This layer changes: nodes {0} · slots {1}","이 층이 바꾼 것: 노드 {0} · 칸 {1}")},{"layer.changedValues",("values {0}","수치 {0}")},
            // status
            {"status.noOwner",("No skill","기술이 없습니다")},{"status.practice",("[Practice]","[연습 공간]")},{"status.layer",("layer {0}","편집 층 {0}")},
            {"status.rows",("node rows max {0}, avg {1}","노드 기본 표시 최대 {0}줄·평균 {1}줄")},{"status.size",("box {0}×{1}","묶음 {0}×{1}")},
            {"status.pending",("{0} unsaved edits","편집 {0}개(저장 전)")},{"status.aux",("aux file changed","보조 파일 변경")},
            {"status.keys",("F frame · A all · Ctrl+Z/Y · wheel zoom · middle drag pan","F 맞추기·A 전체·Ctrl+Z/Y·휠 확대·가운데 끌기 이동")},
            {"status.external",("Excel changed after the editor read it. Use More › Reload ({0} edits exist only on screen)","Excel이 편집기가 읽은 뒤 바뀌었습니다. '더 보기 › 다시 읽기'를 누르세요(편집 {0}개는 지금 화면에만 있음)")},
            {"status.modelFailed",("Could not build the edit model: {0}","편집 모델을 만들지 못했습니다: {0}")},
            {"status.opFailed",("The edit could not be applied","편집 동작을 적용하지 못했습니다")},{"status.opReverted",("Edit reverted: {0}","편집 동작을 되돌렸습니다: {0}")},
            {"status.applyFailed",("Could not apply the edit: {0}","편집을 적용하지 못했습니다: {0}")},
            {"status.dropBlocked",("Inherited node: its column (event) cannot change on layer {0}. Switch to the layer that defines it, or remove it with '-' and add it again","물려받은 노드라 이 층({0})에서 열(이벤트)을 바꿀 수 없습니다 — 노드를 정한 층으로 편집 층을 바꾸거나 '−' 후 '＋'")},
            {"status.removed",("Removed {0} nodes (Ctrl+Z to undo)","노드 {0}개를 뺐습니다(Ctrl+Z로 되돌리기)")},
            // boxes, columns, cards
            {"box.inherits",("inherits {0} {1}","{0} {1} 이어받음")},{"box.inheritsShort",("inherits {0}","{0} 이어받음")},{"box.nodes",("nodes {0}","노드 {0}")},
            {"box.expandTip",("Click to expand","눌러서 펼치기")},{"box.presentationTitle",("Presentation · {0}","연출 · {0}")},
            {"box.addColumn",("+ column","＋ 열")},{"box.addColumnTip",("Add a column (event condition). Right-click a column head to change or remove it","이 묶음에 열(이벤트 조건) 더하기 — 열 머리 오른쪽 단추로 바꾸기·지우기")},
            {"box.fold",("Fold","접기")},
            {"column.tip",("Column (event condition) — right-click: change, remove, add","열(이벤트 조건) — 오른쪽 단추: 열 바꾸기·열 지우기·열 더하기")},
            {"column.addPresentation",("+presentation","＋연출")},{"column.addPresentationTip",("Add a presentation node to this column","이 열(이벤트)에 연출 노드 더하기")},
            {"column.addTip",("Add a node to this column (search)","이 열(이벤트)에 노드 더하기(검색 창)")},
            {"column.keyframe",("← keyframe · {0} frame {1}","← 키프레임 · {0} {1}프레임")},{"column.motionStart",("(motion starts {0})","(모션 {0} 시작)")},
            {"column.keyframeMissing",("← keyframe not found {0}","← 키프레임을 찾지 못함 {0}")},{"column.empty",("empty column","빈 열")},
            {"column.change",("Change column… (time, event)","열 바꾸기… (시각·이벤트)")},{"column.clear",("Clear column — remove all nodes…","열 비우기 — 노드 모두 빼기…")},
            {"column.remove",("Remove column…","열 지우기…")},{"column.add",("Add column…","열 더하기…")},
            {"column.inheritedMark",("(inherited → '-' mark)","(물려받은 노드 → − 표시)")},{"column.baseNote",("This is a base column: the column stays, only its nodes go.","기본 열이라 열은 남고 노드만 뺍니다.")},
            {"column.removeEmpty",("Removes the empty column '{1}' of bundle {0}.","묶음 {0}의 열 '{1}'(빈 열)을 지웁니다.")},
            {"column.removeNodes",("Removes column '{1}' of bundle {0} with {2} nodes (logic {3} · presentation {4}) on this layer, including the nodes of exit columns they raise.","묶음 {0}의 열 '{1}'과 노드 {2}개(판정 {3} · 연출 {4})를 이 층에서 뺍니다. 검사 출구 열의 노드도 함께입니다.")},
            {"column.undoHint",("Ctrl+Z undoes this.","Ctrl+Z로 되돌릴 수 있습니다.")},
            {"column.clearTitle",("Clear column","열 비우기")},{"column.removeTitle",("Remove column","열 지우기")},{"column.clearButton",("Clear","비우기")},{"column.removeButton",("Remove","지우기")},
            {"column.removed",("Removed column '{0}' and {1} nodes (Ctrl+Z to undo)","열 '{0}'과 노드 {1}개를 뺐습니다(Ctrl+Z로 되돌리기)")},
            {"column.added",("Column added (an empty column in the aux file until a node moves in)","열을 더했습니다(노드를 넣기 전까지 보조 파일의 빈 열)")},
            {"column.changed",("Column changed (every node in it)","열을 바꿨습니다(그 열의 노드 모두)")},
            {"card.inheritedTip",("Inherited node: defined in {0}","물려받은 노드: 정한 묶음 {0}")},{"card.noteTip",("Has a note — see the node tab","메모 있음 — 노드 탭에서 전문")},
            {"card.removedTip",("Node removed by this bundle ('-'). Right-click › restore","이 묶음이 뺀 노드(−). 되돌리기로 다시 넣습니다")},
            {"card.compactTip",("One-line card of the Both view. See its slots in the node tab or the Presentation edit view","'둘 다' 보기의 한 줄 카드. 칸은 오른쪽 노드 탭이나 '연출 편집' 보기에서 봅니다")},
            {"card.unimplementedTip",("Module without a runtime: product data cannot use it","미구현 모듈: 제품 데이터에 쓸 수 없습니다")},
            {"card.defaults",("▸ {0} defaults","▸ 기본값 {0}개")},{"card.runs",("ran {0}×","실행 {0}회")},{"card.setSlots",("{0} slots set","칸 {0}")},
            {"chip.unusedTip",("Unused stage: click to show its slots","안 쓴 단계: 눌러서 칸 보기")},{"chip.close",("Close","접기")},
            {"chip.foldedTip",("{0} set slots folded (cards show at most {1} rows)","정한 칸 {0}개가 접혀 있음(카드는 {1}줄까지)")},
            {"row.required",("required","필요")},{"row.default",("(default)","(기본값)")},{"row.toDefault",("(set to default)","(기본값으로)")},{"row.missing",("(missing)","(없음)")},
            {"row.tierTag",("T{0}","{0}단계")},{"row.notRun",("not run","미실행")},{"row.notRunTip",("[not run] the runtime does not read this slot yet","[미실행] 지금 실행기가 읽지 않는 칸")},
            {"row.stage",("stage {0}","단계 {0}")},
            {"value.on",("on","켬")},{"value.off",("off","끔")},
            // node menu
            {"node.restore",("Restore — add it back","되돌리기 — 다시 넣기")},{"node.revertTo",("Revert — to {0}","되돌리기 — {0} 값으로")},{"node.revertLayer",("Revert — clear this layer's change","되돌리기 — 이 층 변경 지우기")},
            {"node.applyToParent",("Apply to parent…","부모에 적용…")},{"node.applyToParentNamed",("Apply to parent ({0})…","부모({0})에 적용…")},
            {"node.revertSlot",("Revert slot {0}","칸 {0} 되돌리기")},{"node.applySlot",("Apply slot {0} to parent…","칸 {0} 부모에 적용…")},
            {"node.remove",("Remove node (Delete)","노드 빼기 (Delete)")},{"node.removeInherited",("— '-' mark, this layer only","— − 표시, 이 층에서만")},
            {"node.up",("Move up","위로")},{"node.down",("Move down","아래로")},{"node.memo",("Memo…","메모…")},{"node.saveCombo",("Save as combo…","조합으로 저장…")},
            {"node.addHere",("Add node to this column…","이 열에 노드 더하기…")},
            {"impact.changes",("← value changes","← 값이 바뀜")},{"impact.title",("Apply to parent — impact","부모에 적용 — 영향 목록")},{"impact.apply",("Apply","적용")},
            {"impact.text",("Applies to parent bundle {0} {1}.\n\nAffected skills and buffs ({2}):\n{3}\n\n(Skills that set the same slot on their own layer keep their value)","부모 묶음 {0} {1}에 적용합니다.\n\n영향 받는 기술·버프 {2}개:\n{3}\n\n(같은 칸을 자기 층에서 정한 기술은 자기 값 유지)")},
            // save
            {"save.blockedTitle",("Cannot save","저장할 수 없습니다")},{"save.kept",("Your edits stay in the editor.","편집 내용은 편집기에 그대로 있습니다.")},
            {"save.errors",("There are {0} validation errors. Fix them in the validation list first.","검증 오류 {0}건이 있습니다. 아래 검증 목록을 먼저 고치세요.")},
            {"save.auxLine",("Aux file changes (folding, bands, memos, combos, empty columns)","보조 파일(접힘·구간 띠·메모·조합·빈 열) 변경")},
            {"save.prepareFailed",("Could not prepare the save: {0}","저장 준비에 실패했습니다: {0}")},
            {"save.row",("row {0}","{0}행")},{"save.insertTop",("new row (top)","새 행(맨 위)")},{"save.insertAfter",("new row (under row {0})","새 행({0}행 아래)")},
            {"save.deleteRow",("row {0} deleted","{0}행 지움")},{"save.blank",("(blank)","(빈 칸)")},{"save.failed",("Not saved","저장하지 못했습니다")},
            {"save.done",("Saved: {0}","저장했습니다: {0}")},{"save.title",("Save — change list","저장 — 변경 목록")},
            {"save.header",("{0} changes to write (changed cells only, new rows under their bundle, deleted rows pull rows up)","Excel에 쓰는 변경 {0}건(바뀐 칸만, 새 행은 묶음 마지막 행 아래, 지운 행은 아래 행을 올림)")},
            {"save.help",("Locks (Excel open) and outside changes are checked again before writing; one previous copy is kept in the backup folder and the file is read back to compare.","저장 전 잠금(Excel 열림)·외부 변경을 다시 검사하고, 직전 사본 1개를 백업 폴더에 둡니다. 저장 뒤 다시 읽어 편집과 같은지 확인합니다.")},
            {"export.pick",("Folder for skill data","스킬 데이터 폴더")},{"export.done",("Skill data written to {0}","스킬 데이터를 만들었습니다: {0}")},
            {"workspace.title",("Switch workspace","작업 공간 바꾸기")},{"workspace.discard",("Discards {0} unsaved edits.","저장하지 않은 편집 {0}개를 버립니다.")},{"workspace.switch",("Discard and switch","버리고 바꾸기")},
            {"workspace.resetText",("Rebuilds the practice workspace from the current product workbooks (what you made there is lost).","연습 공간을 지금 제품 통합 문서 사본으로 다시 만듭니다(연습에서 만든 것은 사라짐).")},{"workspace.reset",("Reset","초기화")},
            {"workspace.reloadText",("Discards {0} unsaved edits and reads Excel again.","저장하지 않은 편집 {0}개를 버리고 Excel을 다시 읽습니다.")},{"workspace.reload",("Reload","다시 읽기")},
            {"issues.title",("Validation","검증 목록")},{"issues.count",("Validation — errors {0} · warnings {1}","검증 목록 — 오류 {0} · 경고 {1}")},
            {"issues.stale",("[Note] the display file was made from a different module dictionary","[주의] 표시 파일이 지금 모듈 사전과 다른 사전에서 만들어졌습니다")},
            {"issues.none",("No errors or warnings","오류·경고 없음")},{"issues.goto",("→ go","→ 이동")},
            // side panel
            {"tab.values",("Values","수치")},{"tab.valuesTip",("Value table","수치 패널")},{"tab.node",("Node","노드")},{"tab.nodeTip",("Note, slots and memo of the selected node","고른 노드의 메모·칸")},
            {"tab.resources",("Resources","연출 리소스")},{"tab.resourcesTip",("Presentation resources per skin","스킨별 연출 리소스")},
            {"values.title",("Values {0} · {1}","수치 {0} · {1}")},{"values.buffTitle",("Buff values {0} · {1} (its own)","버프 수치 {0} · {1} (자체 값)")},
            {"values.add",("+ Add value","＋ 수치 더하기")},{"values.addTip",("New value in the chosen skill's value bundle (Object.Name)","고른 기술의 수치 묶음에 새 수치(이름은 객체.이름)")},
            {"values.addText",("Value name (Object.Name, e.g. Bolt.Damage) and value.","수치 이름(객체.이름, 예 Bolt.Damage)과 값을 적으세요.")},
            {"values.help",("Bold white = set by that tier's value bundle, grey = inherited. Click a column head to switch tier. Click a value to edit, right-click to revert or apply to parent. Picking a slot in a node marks its row.","흰 굵은 값 = 그 단계 수치 묶음이 정한 값, 회색 = 물려받은 값. 열 머리를 누르면 그래프도 그 단계로 바뀝니다. 값을 눌러 고치고, 오른쪽 단추로 되돌리기·부모에 적용. 노드에서 칸을 고르면 그 행이 강조됩니다.")},
            {"values.passHelp",("'passed' values are handed to the buff by the node that applies it (the buff's own values are below).","'넘김'은 버프를 거는 노드가 넘기는 값입니다(버프 자체 값은 아래).")},
            {"values.name",("Name","이름")},{"values.value",("Value","값")},{"values.desc",("Description","설명(쓰는 곳)")},{"values.passed",("passed","넘김")},
            {"values.headTip",("{0} — click to switch to this tier","{0} — 누르면 이 단계로")},{"values.missing",("Not in this skill's value bundle","이 기술 수치 묶음에 없음")},
            {"values.cellTip",("Set in bundle {0} · click to edit, right-click to revert or apply to parent","정한 묶음 {0} · 누르면 고침, 오른쪽 단추로 되돌리기·부모에 적용")},
            {"values.revertTo",("Revert — to parent {0}","되돌리기 — 부모 {0} 값으로")},{"values.applyToParent",("Apply to parent {0} (affects: {1})","부모 {0}에 적용(영향: {1})")},
            {"values.notOwn",("Revert — this tier does not set it","되돌리기 — 이 단계가 정한 값이 아님")},{"values.delete",("Delete this value","이 수치 지우기")},
            {"values.editDesc",("Edit description…","설명(쓰는 곳) 고치기…")},{"values.descTitle",("Value description","수치 설명")},
            {"values.editTitle",("Edit value","수치 고치기")},{"values.editText",("{0} · {1}\n(written to value bundle {2})","{0} · {1}\n(이 단계 수치 묶음 {2}에 씁니다)")},
            {"node.pick",("Pick a node. Clicking a slot marks its row in the values tab; here you edit the slot's source and value.","노드를 고르세요. 노드의 칸을 누르면 수치 패널에서 그 행이 강조되고, 여기서 칸의 출처·값을 고칩니다.")},
            {"node.id",("node {0}","노드 {0}")},{"node.removedText",("This bundle removed the node ('-'). Right-click the node › restore.","이 묶음이 뺀 노드(−)입니다. 노드에서 오른쪽 단추 › 되돌리기로 다시 넣습니다.")},
            {"node.where",("bundle {0} · defined in {1} · writes to {2}","묶음 {0} · 정한 묶음 {1} · 쓰는 층 {2}")},{"node.added",("(+ added here)","(＋ 이 묶음이 더함)")},{"node.unimplemented",("[no runtime]","[미구현 모듈]")},
            {"node.note",("Note","메모(표)")},{"node.none",("(none)","(없음)")},{"node.moduleNote",("Module","모듈 설명")},
            {"node.memoTitle",("Memo (aux file)","메모(보조 파일)")},{"node.memoSave",("Save memo","메모 저장")},{"node.memoTip",("One line under the node header","노드 머리 아래 한 줄")},
            {"node.event",("event {0}","이벤트 {0}")},{"node.from",("source #{0}","발생 노드 #{0}")},{"node.order",("order {0}","순서 {0}")},{"node.priority",("priority {0}","우선 {0}")},
            {"node.dragHint",("(drag to another column to change the event)","(다른 열로 끌면 이벤트가 바뀜)")},
            {"node.keyframeTime",("Time = keyframe {0} ({1} frame {2})","시각 조건 = 키프레임 {0} ({1} {2}프레임)")},
            {"node.keyframes",("Animation keyframes","애니메이션 키프레임")},{"node.motion",("{0} — motion starts {1} · speed {2}","{0} — 모션 시작 {1} · 재생 속도 {2}")},
            {"node.suggest",("Link suggestions (Object.Slot)","값 연결 후보(수치 이름 규칙 객체.칸)")},{"node.newValue",("(new value)","(새 수치)")},
            {"node.link",("Link","연결")},{"node.linkTip",("Link to this value","이 수치로 연결")},{"node.linkNewTip",("Create the value as 0 and link it","수치를 0으로 만들고 연결")},
            {"node.linkAll",("Link all","모두 연결")},{"node.linkAllTip",("Link every suggestion at once (undoable)","후보를 한 번에 연결(되돌리기 가능)")},
            {"node.suggestDesc",("Made by a link suggestion: set the value","값 연결 제안으로 만듦: 값을 정하세요")},
            {"node.slots",("Slots — source (← value / ■ fixed / default) and value","칸 — 출처(← 수치 참조 / ■ 고정 / 기본값)와 값")},
            {"node.removeShort",("Remove node","노드 빼기")},{"node.removeTip",("Remove on this layer (inherited nodes get '-')","이 층에서 빼기(물려받은 노드는 − 표시)")},
            {"node.revertShort",("Revert","되돌리기")},{"node.revertTip",("Clear this layer's change to the node","이 층의 이 노드 변경 지우기")},
            {"slot.notApplicable",("Not used by this kind","이 종류에서는 쓰지 않는 칸")},{"slot.default",("Default","기본값")},{"slot.ref",("← Value","← 수치 참조")},{"slot.fixed",("■ Fixed","■ 고정")},
            {"slot.requiredValue",("(required — needs a value)","(필수 — 값이 필요)")},{"slot.defaultValue",("default {0}","기본값 {0}")},
            {"slot.apply",("Apply","적용")},{"slot.applyTip",("Writes this slot on layer {0}","이 칸을 층 {0}에 씁니다")},{"slot.revertTip",("Revert this slot on this layer","이 층의 이 칸 되돌리기")},
            {"res.title",("Presentation resources","연출 리소스")},{"res.pick",("Pick a node in the bottom (presentation) graph.","아래(연출) 창에서 노드를 고르세요.")},
            {"res.noKey",("This node has no presentation key.","이 노드에는 연출 키가 없습니다.")},{"res.defaultSkin",("Default skin","기본 스킨")},{"res.skin",("Skin {0}","스킨 {0}")},
            {"res.clip",("Motion (Clip)","모션(Clip)")},{"res.effect",("Effect","효과 그림(Effect)")},{"res.sound",("Sound","소리(Sound)")},
            {"res.play",("▶ Play sound","▶ 소리 듣기")},{"res.playTip",("Preview in the editor","편집기에서 미리 듣기")},
            {"res.help",("Edit the resource table in Excel. Effects and sounds preview when a project asset has the same name.","연출 리소스 표는 Excel에서 고칩니다. 같은 이름의 프로젝트 자산이 있으면 그림·소리를 미리 봅니다.")},
            // create
            {"new.name",("Name","이름")},{"new.desc",("Description","설명(기획 문장)")},{"new.ownerNumber",("Owner number","주인 번호")},
            {"new.moduleParent",("Parent module bundle","부모 모듈 묶음")},{"new.valueParent",("Parent value bundle","부모 수치 묶음")},
            {"new.skillText",("Creates owner row, module bundle and value bundle with the same serial. A parent bundle is inherited (e.g. the shared base).","메타·모듈 묶음·수치 묶음을 같은 뒤 번호로 만듭니다. 부모 묶음을 적으면 그 묶음을 이어받습니다(예 공통 바탕).")},
            {"new.effectText",("A box for what a skill spawns (projectiles, hit areas). A parent bundle is inherited. Put the new key in a spawn node to see the box.","기술이 만들어 내는 투사체·판정 영역의 노드를 담을 상자를 만듭니다. 부모 묶음을 적으면 그 노드를 이어받습니다. 생성 노드의 '효과' 칸에 새 Key를 적으면 상자가 보입니다.")},
            {"new.effectDone",("Made effect bundle {0} — put this key in a spawn node","새 효과 객체 묶음 {0}을(를) 만들었습니다 — 생성 노드에 이 Key를 적으세요")},
            {"new.buffText",("Creates buff row, buff bundle and value bundle with the same serial. Put the new key in an apply-buff node.","버프 메타·버프 묶음·수치 묶음을 같은 뒤 번호로 만듭니다. 버프 걸기 노드의 '버프' 칸에 새 Key를 적습니다.")},
            {"new.childText",("A new bundle inheriting {0} {1}. Point an owner's module bundle at it to use it.","묶음 {0} {1}을(를) 이어받는 새 묶음. 기술 메타의 모듈 묶음을 바꾸면 그 기술이 씁니다.")},
            {"new.childDefault",("{0} variant","{0} 변형")},{"new.descText",("Box title = the group description column","묶음 제목 = 그룹 설명 열")},
            {"dup.title",("Duplicate skill","기술 복제")},{"dup.text",("Copies {0} {1} under new keys (owner row, own bundle, value bundle; parents stay).\nCopy the effect bundles it spawns too?","{0} {1}을(를) 새 Key로 복사합니다(메타·자기 묶음·수치 묶음, 부모 묶음은 그대로).\n이 기술이 만드는 효과 객체 묶음도 함께 복사할까요?")},
            {"dup.withEffects",("Copy effects too","효과 묶음도 복사")},{"dup.only",("Skill only","기술만 복사")},{"dup.nameText",("New name (reference only)","새 이름(참고용)")},{"dup.defaultName",("{0} copy","{0} 복사")},
            {"bands.text",("One per line: name=first column..last column (columns: {0})","한 줄에 하나: 이름=시작 열..끝 열 (열 키: {0})")},{"bands.field",("Bands","띠")},
            {"memo.title",("Node memo","노드 메모")},{"memo.text",("Bundle {0} node {1} — saved in the aux file","묶음 {0} 노드 {1} — 보조 파일에 저장")},{"memo.field",("Memo","메모")},
            {"combo.title",("Save as combo","조합으로 저장")},{"combo.text",("Keeps the {0} selected nodes in the combo list (aux file)","고른 노드 {0}개를 '조합 넣기' 목록에 남깁니다(보조 파일)")},{"combo.name",("Combo name","조합 이름")},
            // search
            {"search.title",("Add node · {0}","노드 더하기 · {0}")},{"search.titlePresentation",("Add presentation · {0}","연출 더하기 · {0}")},
            {"search.combos",("Insert combo","조합 넣기")},{"search.combo",("{0} (nodes {1})","{0} (노드 {1})")},
            {"search.unimplemented",("(no runtime)","(미구현)")},{"search.unsupported",("(unsupported)","(미지원)")},{"search.guess",("[guess]","[추정]")},
            {"search.addNode",("Add node","노드 더하기")},
            {"search.unimplementedDialog",("{0} ({1}) has no runtime yet, so product data cannot use it.","{0}({1})는 아직 실행기가 없어 제품 데이터에 넣을 수 없습니다.")},
            {"search.unsupportedDialog",("{0} is not supported by the runtime (supported: {1}).","{0}는 실행기가 지원하지 않습니다(지원: {1}).")},
            // column dialog
            {"columnDialog.addTitle",("Add column","열 더하기")},{"columnDialog.changeTitle",("Change column","열 바꾸기")},
            {"columnDialog.addText",("Adds a column (event condition) to bundle {0}. It is an empty column in the aux file until a node moves in.","묶음 {0}에 열(이벤트 조건)을 더합니다. 노드를 넣기 전까지는 보조 파일의 빈 열입니다.")},
            {"columnDialog.changeText",("Changes column '{1}' of bundle {0}. Every logic and presentation node in it changes too (inherited nodes: time only).","묶음 {0}의 열 '{1}'을 바꿉니다. 그 열의 판정·연출 노드가 모두 함께 바뀝니다(물려받은 노드는 시각만).")},
            {"columnDialog.event",("Event","이벤트")},{"columnDialog.time",("Time ({0} or value name)","시각({0} 또는 수치 이름)")},{"columnDialog.subject",("Subject","주체")},
            {"columnDialog.subjectPick",("Pick subject","주체 고르기")},{"columnDialog.valueName",("Value name","수치 이름")},{"columnDialog.pickValue",("(pick a value)","(수치 고르기)")},
            {"columnDialog.keyframe",("Keyframe","키프레임")},{"columnDialog.pickKeyframe",("(pick a keyframe)","(키프레임 고르기)")},{"columnDialog.offset",("Offset ({0}, ±)","오프셋({0}, ±)")},
            {"columnDialog.noMotion",("This bundle has no motion node to pick keyframes from.","이 묶음에는 키프레임을 고를 모션 재생 노드가 없습니다.")},
            {"columnDialog.motionNote",("Motion #{0}: {1}","모션 #{0}: {1}")},{"columnDialog.math",("{0} frame {1} ÷ {2} fps","{0} {1}프레임 ÷ {2}프레임/초")},
            {"columnDialog.negative",("Keyframe + offset is before zero (KeyframeOffsetNegative).","키프레임 + 오프셋이 0초보다 앞입니다(KeyframeOffsetNegative).")},
            {"columnDialog.noExits",("This bundle has no node with pass/fail exits.","이 묶음에 통과/실패 출구가 있는 노드가 없습니다.")},{"columnDialog.source",("Source node","발생 노드")},
            // backend
            {"backend.noConfig",("Settings file not found: {0}. Set -skillgraphConfig, SKILLGRAPH_CONFIG or put skillgraph.json in the project folder.","설정 파일이 없습니다: {0}. -skillgraphConfig·SKILLGRAPH_CONFIG로 지정하거나 프로젝트 폴더에 skillgraph.json을 두세요.")},
            {"backend.noPython",("Cannot start Python ({0}): {1}. Set -skillgraphPython or SKILLGRAPH_PYTHON (Python 3.10+ with openpyxl; nothing is installed automatically).","Python을 시작하지 못했습니다({0}): {1}. -skillgraphPython·SKILLGRAPH_PYTHON으로 지정하세요(Python 3.10+ · openpyxl, 자동 설치하지 않음).")},
            {"backend.timeout",("The editor backend did not answer within {0} s.","편집기 백엔드가 {0}초 안에 답하지 않았습니다.")},
            {"backend.ended",("The editor backend exited.","편집기 백엔드가 끝났습니다.")},{"backend.order",("Editor backend replies are out of order.","편집기 백엔드 응답 순서가 맞지 않습니다.")},
        };
    }
}
