using Noskang.SkillGraph;
using static Noskang.SkillGraph.ModuleSet;

namespace SkillGraphDemo
{
    // The demo game's module dictionary: a small real-time action module set.
    // Tools › SkillGraph Kit › Export Module Dictionary writes SkillData/Generated/module_dictionary.json and module_display.json
    // from this class. A test checks that the export is byte-for-byte the file the Python demo generator writes.
    public sealed class DemoModules : ModuleSetProvider
    {
        private const SlotRole B=SlotRole.Balance,S=SlotRole.Structure;
        private const ModuleLine Logic=ModuleLine.Logic,Show=ModuleLine.Presentation;
        private static string[] E(params string[] events)=>events;

        public override ModuleSet Build()=>new ModuleSet.Builder()
            .Events("CastStart","Moment","Create","Step","Alarm","Hit","LifeEnd","Destroy","Cancel","CheckPass","CheckFail","StatusApply","StatusExpire","StatusRemove","OnHurt")
            .Kind("Skill","CastStart","Moment","Cancel","CheckPass","CheckFail")
            .Kind("Effect","Create","Step","Alarm","Hit","LifeEnd","Destroy","CheckPass","CheckFail")
            .Kind("Buff","StatusApply","StatusExpire","StatusRemove","Step","Alarm","CheckPass","CheckFail","OnHurt")
            .Presentation("CastStart","Moment","Create","Hit","Destroy","CheckPass","CheckFail","StatusApply","StatusExpire")
            .Subscriptions(new[]{"OnHurt"},"Self","Ally","Enemy","Any")
            .Timed("Moment","Alarm").Exits("CheckPass","CheckFail").Start("CastStart")
            .Enum("ATTACH","Effect","Body","Ground")
            .Enum("DAMAGE_KIND","Normal","Pierce")
            .Enum("MOVE_MODE","Dash","Leap","Teleport")
            .Enum("SHAPE","Circle","Box")
            .Enum("STAT","Attack","Defense","MoveSpeed")
            .Enum("STAT_OP","Add","Multiply")
            .Enum("TARGET","Self","HitTarget","Allies")
            .Enum("WHILE","Owner","Charging","Released")
            .Module("CastLock",Logic,E("CastStart"),Slot("Duration","time","s",B,null,"cast"),Slot("MoveLockUntil","time","s",B,0,"cast"))
            .Module("SpawnEffect",Logic,E("CastStart","Moment","Create","Alarm","Hit","LifeEnd","CheckPass","CheckFail"),
                Slot("BundleKey","key:moduleBundle","",S,null,"cast"),Slot("Count","int","",B,1,"cast"),Slot("OffsetX","float","m",B,0,"cast"),Slot("AngleStep","float","deg",B,0,"cast"))
            .Module("SelfMove",Logic,E("CastStart","Moment"),Slot("Mode","enum:MOVE_MODE","",S,"Dash","cast"),Slot("Distance","float","m",B,null,"cast"),Slot("Duration","time","s",B,null,"cast"))
            .Module(new ModuleDeclaration("Charge",Logic,"common",E("CastStart"),new[]{Slot("MaxTime","time","s",B,null,"cast"),Slot("MinTime","time","s",B,0,"cast")},new[]{"Pass","Fail"}))
            .Module("ApplyStatus",Logic,E("CastStart","Moment","Create","Hit","CheckPass","CheckFail","StatusApply"),
                Slot("BuffKey","key:buff","",S,null,"cast"),Slot("Target","enum:TARGET","",S,"HitTarget","cast"),Slot("Duration","time","s",B,null,"cast"))
            .Module("StatModify",Logic,E("StatusApply"),Slot("Stat","enum:STAT","",S,null,"cast"),Slot("Op","enum:STAT_OP","",S,"Add","cast"),Slot("Value","float","",B,null,"cast"))
            .Module("SetLifetime",Logic,E("Create"),Slot("Duration","time","s",B,null,"spawn"))
            .Module("MoveProjectile",Logic,E("Create"),Slot("Speed","float","m/s",B,null,"spawn"),Slot("Range","float","m",B,0,"spawn"))
            .Module("HitShape",Logic,E("Create"),Slot("Shape","enum:SHAPE","",S,"Circle","spawn"),Slot("Radius","float","m",B,0,"spawn"),Slot("Width","float","m",B,0,"spawn"),Slot("Height","float","m",B,0,"spawn"))
            .Module("HitWindow",Logic,E("Create"),Slot("From","time","s",B,0,"spawn"),Slot("Until","time","s",B,null,"spawn"))
            .Module("Damage",Logic,E("Hit"),Slot("Amount","float","",B,null,"hit"),Slot("Kind","enum:DAMAGE_KIND","",S,"Normal","hit",new[]{"Normal"}))
            .Module(new ModuleDeclaration("Knockback",Logic,"common",E("Hit"),new[]{Slot("Force","float","m",B,null,"hit")},implemented:false))
            .Module("Destroy",Logic,E("Hit","Alarm","LifeEnd","CheckPass","CheckFail"))
            .Module("PlayMotion",Show,E("CastStart","Moment"),Slot("PresentationKey","key:presentation","",S,null,"cast"),Slot("Speed","float","x",S,1.0,"cast"),
                Slot("While","enum:WHILE","",S,"Owner","cast"),Slot("Until","time","s",S,0,"cast"),Slot("LoopFrom","time","s",S,0,"cast"),Slot("LoopPeriod","time","s",S,0,"cast"))
            .Module("PlaySound",Show,E("CastStart","Moment","Create","Hit","Destroy","CheckPass","CheckFail","StatusApply"),Slot("PresentationKey","key:presentation","",S,null,"cast"))
            .Module("ShowVisual",Show,E("CastStart","Moment","Create","Hit","Destroy","StatusApply","StatusExpire"),Slot("PresentationKey","key:presentation","",S,null,"cast"),Slot("Attach","enum:ATTACH","",S,"Effect","cast"))
            .Build();

        private static LocalText T(string en,string ko)=>new LocalText(en,ko);

        public override ModuleDisplaySet BuildDisplay(ModuleSet set)
        {
            var d=new ModuleDisplaySet()
                .Family("CastFlow",T("Cast & flow","시전·흐름"),"#4E6C8E","◷")
                .Family("SpawnLife",T("Spawn & life","생성·수명"),"#A7862C","✦")
                .Family("Move",T("Movement","이동"),"#3E8A57","→")
                .Family("HitDamage",T("Hit & damage","판정·피해"),"#AE4646","✕")
                .Family("StatusBuff",T("Status & buffs","상태·버프"),"#7552A8","◆")
                .Family("Presentation",T("Presentation","연출"),"#C0742C","▶")
                .Event("CastStart",T("Cast start","시전 시작")).Event("Moment",T("At time","시각")).Event("Create",T("Created","생성")).Event("Step",T("Every tick","매 박자"))
                .Event("Alarm",T("Alarm","알람")).Event("Hit",T("Hit","명중")).Event("LifeEnd",T("Life end","수명 끝")).Event("Destroy",T("Destroyed","소멸"))
                .Event("Cancel",T("Cancelled","취소")).Event("CheckPass",T("Pass","통과")).Event("CheckFail",T("Fail","실패")).Event("StatusApply",T("Applied","적용"))
                .Event("StatusExpire",T("Expired","만료")).Event("StatusRemove",T("Removed","제거")).Event("OnHurt",T("When hurt","맞을 때"));

            d.Module("CastLock",T("Cast lock","시전 잠금"),"CastFlow",T("Locks actions while casting","시전 동안 다른 행동을 막음"))
                .Slot("Duration",T("Duration","길이"),T("How long the cast lasts","시전이 이어지는 시간"))
                .Slot("MoveLockUntil",T("Move lock","이동 잠금"),T("Movement is locked until this time","이 시각까지 걷지 못함"))
                .Stage("Basic","Duration","MoveLockUntil");
            d.Module("SpawnEffect",T("Spawn effect","효과 생성"),"SpawnLife",T("Creates an effect object from another bundle","다른 묶음의 효과 객체를 만듦"))
                .Slot("BundleKey",T("Effect","효과"),T("Effect bundle to create","만들 효과 묶음"))
                .Slot("Count",T("Count","개수"),T("How many to create","만드는 개수"))
                .Slot("OffsetX",T("Forward offset","앞 거리"),T("Distance in front of the caster","시전자 앞쪽 거리"))
                .Slot("AngleStep",T("Angle step","각도 간격"),T("Angle between copies","여러 개일 때 사이 각도"))
                .Stage("Basic","BundleKey","Count","OffsetX","AngleStep");
            d.Module("SelfMove",T("Self move","자기 이동"),"Move",T("Moves the caster","시전자를 움직임"))
                .Slot("Mode",T("Mode","방식"),T("How to move","움직이는 방식"))
                .Slot("Distance",T("Distance","거리"),T("Distance","거리"))
                .Slot("Duration",T("Duration","시간"),T("Time to cover the distance","거리를 가는 시간"))
                .Stage("Basic","Mode","Distance","Duration");
            d.Module("Charge",T("Charge","충전"),"CastFlow",T("Hold to charge; passes when held long enough","누르고 모음. 충분히 모으면 통과"))
                .Slot("MaxTime",T("Max time","최대"),T("Charging stops here","이 시각에 충전이 끝남"))
                .Slot("MinTime",T("Min time","최소"),T("Shorter charges fail","이보다 짧으면 실패"))
                .Stage("Basic","MaxTime","MinTime");
            d.Module("ApplyStatus",T("Apply buff","버프 걸기"),"StatusBuff",T("Applies a buff","버프를 건다"))
                .Slot("BuffKey",T("Buff","버프"),T("Buff to apply","걸 버프"))
                .Slot("Target",T("Target","대상"),T("Who gets it","받는 대상"))
                .Slot("Duration",T("Duration","지속"),T("Buff duration","버프 지속 시간"))
                .Stage("Basic","BuffKey","Target","Duration");
            d.Module("StatModify",T("Stat modify","능력치 변경"),"StatusBuff",T("Changes a stat while the buff lasts","버프 동안 능력치를 바꿈"))
                .Slot("Stat",T("Stat","능력치"),T("Stat to change","바꿀 능력치"))
                .Slot("Op",T("Operation","연산"),T("Add or multiply","더하기 또는 곱하기"))
                .Slot("Value",T("Value","값"),T("Amount","양"))
                .Stage("Basic","Stat","Op","Value");
            d.Module("SetLifetime",T("Lifetime","수명"),"SpawnLife",T("Effect disappears after this time","이 시간이 지나면 효과가 사라짐"))
                .Slot("Duration",T("Duration","수명"),T("Lifetime","수명"))
                .Stage("Basic","Duration");
            d.Module("MoveProjectile",T("Projectile","투사체"),"Move",T("Flies straight forward","앞으로 곧게 날아감"))
                .Slot("Speed",T("Speed","속도"),T("Flight speed","나는 속도"))
                .Slot("Range",T("Range","사거리"),T("0 = until lifetime ends","0이면 수명까지"))
                .Stage("Basic","Speed","Range");
            d.Module("HitShape",T("Hit shape","판정 모양"),"HitDamage",T("Area that can hit","맞히는 영역"))
                .Slot("Shape",T("Shape","모양"),T("Circle or box","원 또는 상자"))
                .Slot("Radius",T("Radius","반지름"),T("Circle radius","원 반지름"))
                .Slot("Width",T("Width","너비"),T("Box width","상자 너비"))
                .Slot("Height",T("Height","높이"),T("Box height","상자 높이"))
                .Stage("Basic","Shape","Radius").Advanced("Box","Width","Height")
                .Kind("Shape").When("Circle","",true,"Radius").When("Box","",true,"Width","Height");
            d.Module("HitWindow",T("Active window","켜짐 구간"),"HitDamage",T("The hit shape only works inside this window","이 구간에만 판정이 켜짐"))
                .Slot("From",T("From","시작"),T("Window start","켜지는 시각"))
                .Slot("Until",T("Until","끝"),T("Window end","꺼지는 시각"))
                .Stage("Basic","From","Until");
            d.Module("Damage",T("Damage","피해"),"HitDamage",T("Deals damage on hit","맞히면 피해를 줌"))
                .Slot("Amount",T("Amount","피해량"),T("Damage dealt","주는 피해"))
                .Slot("Kind",T("Kind","종류"),T("Damage kind","피해 종류"))
                .Stage("Basic","Amount","Kind");
            d.Module("Knockback",T("Knockback","밀치기"),"HitDamage",T("Pushes the target (no runtime yet)","대상을 밀어냄(실행기 없음)"))
                .Slot("Force",T("Force","힘"),T("Push distance","미는 거리"))
                .Stage("Basic","Force");
            d.Module("Destroy",T("Destroy","소멸"),"SpawnLife",T("Removes the effect","효과를 없앰"))
                .Stage("Basic");
            d.Module("PlayMotion",T("Play motion","모션 재생"),"Presentation",T("Plays a body animation","몸 애니메이션을 재생"))
                .Slot("PresentationKey",T("Motion","모션"),T("Motion resource","모션 리소스"))
                .Slot("Speed",T("Speed","속도"),T("Playback speed","재생 속도"))
                .Slot("While",T("While","동안"),T("When it plays","재생하는 동안"))
                .Slot("Until",T("Until","끝"),T("0 = clip end","0이면 클립 끝"))
                .Slot("LoopFrom",T("Loop from","반복 시작"),T("Loop start","반복 시작 시각"))
                .Slot("LoopPeriod",T("Loop period","반복 주기"),T("0 = no loop","0이면 반복 없음"))
                .Stage("Basic","PresentationKey","Speed").Advanced("Timing","While","Until","LoopFrom","LoopPeriod");
            d.Module("PlaySound",T("Play sound","소리 재생"),"Presentation",T("Plays a sound","소리를 냄"))
                .Slot("PresentationKey",T("Sound","소리"),T("Sound resource","소리 리소스"))
                .Stage("Basic","PresentationKey");
            d.Module("ShowVisual",T("Show visual","그림 표시"),"Presentation",T("Shows a visual effect","효과 그림을 보여 줌"))
                .Slot("PresentationKey",T("Visual","그림"),T("Visual resource","그림 리소스"))
                .Slot("Attach",T("Attach","붙는 곳"),T("Where it follows","따라가는 대상"))
                .Stage("Basic","PresentationKey","Attach");
            return d;
        }
    }
}
