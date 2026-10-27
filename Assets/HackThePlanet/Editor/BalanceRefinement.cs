using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using static HackThePlanet.Editor.DefaultGameplayAssets;

namespace HackThePlanet.Editor
{
    public static class BalanceRefinement
    {
        public static void Apply()
        {
            var root=Resources.Load<GameDefinition>("HackThePlanet/Gameplay/Game");
            ActionDefinition Tool(string id)=>root.actions.Single(a=>a.id==id);
            TechnologyDefinition Tech(string id)=>root.technologies.Single(t=>t.id==id);
            var scan=Tool("scan");var hack=Tool("hack");var download=Tool("download");
            scan.presentationPriority=30;hack.presentationPriority=20;download.presentationPriority=0;
            foreach(var id in new[]{"bridge","backdoor"})Tool(id).presentationPriority=10;
            foreach(var id in new[]{"disrupt","blackout"})Tool(id).presentationPriority=15;
            scan.duration=N(1.6f);scan.baseTrace=N(.6f);scan.visible=Not(State(NodeState.Scanned));
            hack.duration=B(Arithmetic.Divide,B(Arithmetic.Add,N(2.2f),B(Arithmetic.Multiply,I(NumberInput.Security),N(1.15f))),S("compute"));
            hack.visible=Not(State(NodeState.Controlled));
            download.duration=B(Arithmetic.Divide,Choose(Compare(I(NumberInput.NodeStorage),Comparison.Greater,N(0)),I(NumberInput.NodeStorage),Choose(State(NodeState.Objective),N(5.5f),N(4))),S("bandwidth"));
            download.visible=All(Any(State(NodeState.Objective),State(NodeState.Bonus)),Not(State(NodeState.Collected)),Not(Tag("securityhub")));
            var preparationTime=Mod("duration",Arithmetic.Multiply,N(.85f),All(Action(hack),State(NodeState.Scanned)));preparationTime.id="scan-preparation-time";
            var preparationTrace=Mod("baseTrace",Arithmetic.Multiply,N(.8f),All(Action(hack),State(NodeState.Scanned)));preparationTrace.id="scan-preparation-trace";
            root.modifiers=root.modifiers.Where(m=>m.id!="scan-preparation-time"&&m.id!="scan-preparation-trace").Concat(new[]{preparationTime,preparationTrace}).ToArray();
            scan.description="Reveal nearby routes and prepare this target: scanned systems take 15% less hack time and 20% less hack trace.";
            hack.description="Take control to extend your access. Scan first for a cleaner, faster intrusion.";
            download.description="Secure the selected objective or optional archive, then extract to bank its rewards.";
            foreach(var id in new[]{"disrupt","blackout"})
            {
                var action=Tool(id);action.duration=N(1.2f);action.baseTrace=N(1);
                foreach(var status in action.effects.OfType<ApplyStatusEffect>())status.duration=N(45);
                action.visible=All(Tag(id=="disrupt"?"scanner":"securityhub"),State(NodeState.DefenseActive));
            }
            Tool("blackout").requirements=new[]{Req(State(NodeState.Known),"Scan to discover this hub."),Req(State(NodeState.DefenseActive),"This defense is already offline."),Req(Compare(I(NumberInput.ControlledDistance),Comparison.LessOrEqual,N(3)),"Requires a discovered route within three hops of controlled access.")};
            var bridge=Tool("bridge");bridge.duration=N(1.2f);bridge.useLimit=N(2);
            bridge.visible=All(Tag("firewall"),Not(State(NodeState.Controlled)),new ResearchCondition{technology=Tech("bridge"),equipped=true});
            bridge.requirements=new[]{Req(State(NodeState.Known),"Discover this firewall first."),Req(State(NodeState.Reachable),"Control a connected neighbor first."),Req(Not(State(NodeState.Controlled)),"You already control this system."),Req(State(NodeState.Scanned),"Scan this firewall to prepare a bridge.")};
            var backdoor=Tool("backdoor");backdoor.duration=N(.8f);
            backdoor.visible=All(Any(Tag("workstation"),Tag("relay")),Not(State(NodeState.Controlled)),new ResearchCondition{technology=Tech("backdoor"),equipped=true});
            backdoor.requirements=new[]{Req(State(NodeState.Known),"Discover this system first."),Req(Not(State(NodeState.Controlled)),"You already control this system."),Req(Compare(I(NumberInput.ControlledDistance),Comparison.LessOrEqual,N(3)),"Requires a discovered route within three hops of controlled access.")};
            foreach(var effect in Tool("decoy").effects)
            {
                if(effect is TraceEffect trace)trace.amount=N(-12);
                if(effect is ApplyStatusEffect protection)protection.duration=N(10);
            }
            string[] ids={"wide","efficient","quiet","disrupt","blueprint","signal","chain","selective","ghost","backdoor","blackout","decoy","passive","weakpoint","cascade","bridge","buffer"};
            int[] costs={45,45,45,45,90,60,85,85,70,100,85,95,120,130,140,140,135};
            for(int i=0;i<ids.Length;i++)Tech(ids[i]).cost=costs[i];
            Tech("efficient").modifiers[0].value=N(.8f);Tech("quiet").modifiers[0].value=N(.8f);
            var signalPreparation=Mod("baseTrace",Arithmetic.Multiply,N(.85f),All(Action(hack),State(NodeState.Scanned)));signalPreparation.id="signal-prepared-trace";
            Tech("signal").modifiers=Tech("signal").modifiers.Where(m=>m.id!="signal-prepared-trace").Concat(new[]{signalPreparation}).ToArray();
            Tech("selective").modifiers[0].value=N(.7f);Tech("weakpoint").modifiers[0].value=N(.75f);Tech("buffer").modifiers[0].value=N(.9f);
            Tech("bridge").parents=new[]{Tech("wide"),Tech("efficient")};
            Tech("signal").description="Scans take half the usual time and produce no base trace. Prepared hacks generate another {modifier:2:reduction}% less trace. Active sensors can still detect the scan.";
            Tech("bridge").description="Two charges per operation. Scan a reachable firewall, then cross it in 1.2 seconds with zero trace. Requires Wide scan and Efficient hack.";
            Tech("backdoor").description="Once per operation, reclaim a discovered workstation or relay within three mapped hops of controlled access. Takes 0.8 seconds and produces no trace.";
            Tech("disrupt").description="Suppress a reachable scanner for 45 seconds. Takes 1.2 seconds with 1 base trace. Reusable while equipped.";
            Tech("blackout").description="Remotely suppress a discovered hub within three mapped hops for 45 seconds. Its protected defenses immediately lose their bonus.";
            Tech("decoy").description="Once per operation, remove 12 trace and prevent trace growth for 10 seconds. Best used before committing to a noisy tool.";
            root.rewards.archiveXpCap=45;root.rewards.archiveCredits=100;root.rewards.quietCredits=100;root.rewards.quietXp=20;
            float[] quiet={60,60,65,60,65,70};int story=0;
            foreach(var job in root.contracts)
            {
                if(!job.recurring)job.quietLimit=quiet[story++];
                job.condition="Below "+job.quietLimit+"% trace: +100 cr / +20 XP. Each optional archive: +100 cr / +15 XP; all three pay XP.";
            }
            foreach(var hardware in root.hardware)
            {
                int[] prices=hardware.id=="memory"?new[]{450,1000}:new[]{350,800,1450};
                for(int i=0;i<hardware.tiers.Length;i++)hardware.tiers[i].price=prices[i];
                if(hardware.id=="memory")hardware.requirements=new[]{Req(Compare(I(NumberInput.UnlockedAbilities),Comparison.Greater,N(0)),"Research an ability before expanding its loadout.")};
            }
            root.career.allowTaskCancellation=true;
            GameDefinitionValidator.ThrowIfInvalid(root);
            foreach(var definition in GameDefinitionValidator.Definitions(root))EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssets();GameCatalog.InvalidateDefault();
            File.WriteAllText("Artifacts/balance-refinement.txt","PASS / Faster tools, scan preparation, affordable research, useful bounded abilities, full archive XP, and guarded hardware progression authored in gameplay assets.");
        }
    }
}
