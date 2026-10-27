using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Unity.Scripting.LifecycleManagement;
using UnityEditor;
using UnityEngine;

namespace HackThePlanet.Editor
{
	/// <summary>
	///     Descriptions shared by forms and authoring coverage validation. Missing help is an error, never a silent
	///     generic fallback.
	/// </summary>
	[NoAutoStaticsCleanup]
	public static class ContentFieldHelp
	{
		// Parse the embedded help table into a lookup keyed by field name.
		private static readonly Dictionary<string, string> Help = @"
rig|Shared rig rules and starting layout. JSON is authoritative; generated assets are mirrors.
chassis|Registered chassis models, ordered by their unique progression tier.
modules|Registered purchasable module models. Each purchase creates an independent owned copy.
startingChassis|Chassis granted to a new career.
startingParts|Free installed starter copies and their row, column and rotation. Paid price is zero.
module|Registered module model reference.
rotated|Rotate this footprint 90 degrees; swaps width and height.
rotatable|Permit 90 degree rotation when global rotation is enabled.
allowRotation|Enable footprint rotation for models that permit it.
requireValidBuild|Block connections while the rack has placement, power or cooling failures, or no installed Uplink. Storage and processing gaps are warnings only.
saleFraction|Voluntary sale fraction of the actual price paid; credits round down.
researchSaleFraction|Fraction of actual paid price returned when a research refund locks the model.
minimumTraceFraction|Minimum fraction of ordinary trace retained after reductions. Explicitly trace-free tools remain exempt.
maximumCompute|Upper limit for the modified compute rate in GFLOP/s.
maximumBandwidth|Upper limit for the modified bandwidth rate in GB/s.
memoryReclaim|RAM in GB/s returned from the reclaiming bucket per simulation second; pauses with simulation time.
family|Module family used for filtering and adjacency matching, such as power, cooling, storage or reclaim.
tier|Model tier. Power and cooling tiers are separate purchasable models.
width|Unrotated module footprint width in rack cells.
height|Unrotated module footprint height in rack cells.
rows|Number of rack rows.
columns|Number of rack columns.
requiredJobs|Successful deliveries required to purchase this chassis, including freelance work.
slots|Maximum number of equipped abilities, independent of their RAM costs.
storageCapacity|Storage capacity in GB, held until disconnect.
memoryPool|Maximum ability RAM in GB. Active tools reserve it; one-shots occupy it until reclaimed.
powerCapacity|Power limit supplied by this chassis or installed module; stacks additively.
coolingCapacity|Cooling limit supplied by this chassis or installed module; stacks additively.
powerDemand|Power consumed while this module is installed, including cooling modules.
heatOutput|Cooling capacity required by this installed module, including power supplies.
flavour|Long-form flavour text shown in readable module tooltips.
synergy|Player-facing explanation of adjacency modifiers; mechanics live in Modifiers.
memoryMode|None, OneShot (reclaiming RAM), or Persistent (reserved until disabled).
memoryCost|RAM in GB needed to start the tool. Queued actions recheck when they start.
activeModifiers|Stat modifiers applied while this persistent tool has a RAM reservation.
instant|Allow a zero-duration targeted action alongside a running tool, such as archive discard.
releaseOnTargetControl|Release this persistent tool and its RAM when the target becomes permanently controlled.
payload|This objective downloads a file that occupies storage. Remote erase, alteration and control tasks leave it off.
payloadStorage|Deterministic GB formula for required payload files. All required payloads must fit the starting chassis.
archiveStorage|Deterministic GB formula for optional archives. Discarding an archive frees this space.
kind|Match a neighboring module model ID, family or branch. Empty matches all installed neighbors.
id|Stable identity used by references and saves. Use Rename ID for existing definitions; local network IDs must match links and objectives.
title|Player-facing title shown on cards, research nodes, or reports.
description|Player-facing explanatory copy. Technology copy can interpolate modifier values with {modifier:0:reduction}.
companies|Ordered companies registered in the catalog.
contracts|Ordered story and recurring contracts offered by the exchange.
technologies|Ordered research definitions registered in the catalog.
actions|Ordered tools registered in the catalog; grants and visibility determine availability.
nodeTypes|Node roles available to networks, with presentation and defense behavior.
networks|Network templates available for contracts.
hardware|Ordered hardware upgrade tracks shown in the rig interface.
statuses|Timed status definitions available to effects.
rewards|Shared settlement formulas and optional bonus values.
career|Starting career values, baseline stats, limits and outcome policies.
modifiers|Ordered stat adjustments. Lower priority applies first; equal priorities retain authored order.
triggers|Event handlers that apply effects when their conditions pass.
company|Company reference for this contract, or company attribution in a lore fragment.
contract|Player-facing contract attribution attached to this lore fragment.
network|Network template generated when connecting to this contract.
client|Player-facing client or contact identity.
tag|A semantic tag used by matching rules, or a short contract category caption.
objective|Player-facing mission summary; completion is defined separately by Objectives.
condition|Rule that must pass, or player-facing bonus-condition copy on a contract. Copy alone does not implement rewards.
aftermath|Narrative published after successfully delivering this contract.
displayKind|Presentation category for the contract. Actual behavior comes from objectives and rules.
recurring|Allows this contract to be offered repeatedly rather than completed once.
requiredWins|Minimum completed-contract count required to start this contract.
quietLimit|Trace percentage below which quiet reward rules can apply.
credits|Credits formula for a contract or reward rule; for Career this is the initial credit balance. Contract tier multipliers apply once.
xp|XP formula for a contract or reward rule; for Career this is the initial spendable XP.
difficulty|Difficulty formula: 1 or less Easy, 2 Medium, 3 or more Hard. Controls preview intel, generation tier and base payout multiplier.
seed|Deterministic network generation seed formula. The same content and seed produce the same topology.
objectives|All required goals. Every completion condition must pass before delivery.
actionRules|Extra execution requirements applied to a particular action throughout this contract.
defenses|Credential locks and periodic security sweeps captured when a new operation starts. Existing saved operations retain their snapshot.
paceMultiplier|Positive contract-specific multiplier for action duration. Use this to keep contracts with more objectives within their pacing target.
sourceNodeId|Network-local key server or security sweep controller. The node must exist in this contract's network.
targetNodeIds|Download targets protected by this credential lock. One extracted key opens all these targets; spoofing opens one target.
minimumDifficulty|Lowest evaluated difficulty that activates this layer. Freelance contracts can add locks at Medium and sweeps at Hard.
intervalSeconds|Active simulation seconds between sweep bursts. Pause, controller suppression and Loop Feed freeze the countdown.
traceBurst|Trace added by each security sweep before sweepTrace modifiers, independently from ordinary action trace.
lore|Lore fragments available to this contract; network archive indices select fragments.
nodeId|Exact local node ID used by this selector, condition or objective.
label|Player-facing node or objective label, or revision name in authoring tools.
actionLabel|Player-facing verb for the objective's assigned action.
action|Action definition being granted, matched, or required by this objective.
complete|Condition evaluated on the objective node to determine whether the goal is complete.
actionRequirements|Additional requirements for executing the assigned objective action.
entryNode|Local node ID that must start known and controlled; ingress for the operation.
preferredNode|Local node ID preferred for initial live-operation selection.
previewNode|Local node ID used for the pre-operation selection.
layout|Authored uses explicit nodes and links; ParallelRoutes generates seeded approaches from route settings.
nodes|Network-local nodes with stable IDs, types, positions and initial state.
links|Connections between local node IDs. Each link can be gated by a generation-time condition.
routes|ParallelRoutes generator configuration; unused by an Authored layout.
type|Node type reference determining glyph, tags, security and defense auras.
column|Legacy logical column and a numeric generation input; independent from 3D position.
row|Legacy logical row and a numeric generation input; independent from 3D position.
x|X coordinate. On NetworkNode this is legacy 2D metadata; use Position for the 3D graph.
y|Y coordinate. On NetworkNode this is legacy 2D metadata; use Position for the 3D graph.
z|Z coordinate in graph space.
position|Graph placement coordinates. Technology uses XY; network nodes use XYZ.
detail|Player-facing node detail text.
text|Full player-facing lore fragment prose recovered from an archive.
known|Whether the node begins discovered.
controlled|Whether the operator controls the node at operation start.
bonus|Marks optional archive data for bonus settlement.
loreIndex|Zero-based index into contract lore; -1 means no lore assigned.
securityModifiers|Adjustments to this node's initial security.
a|First endpoint's local node ID.
b|Second endpoint's local node ID.
when|Condition gating a link, modifier, reward, trigger or effect group.
nodeCount|Total systems created by this route tier.
lengths|Systems per approach lane. Lane order identifies the direct route and maintenance lanes.
sensorsPerLane|How many scanner nodes to attach to each eligible lane.
entrance|Local node ID at the first branch point after ingress.
destination|Local node ID at the shared route destination.
sensorAttachment|Local node ID used for the configured sensor attachment.
directRoute|Ordered local node IDs reserved for the short direct approach.
sensors|Local scanner identities used by the generator.
secondaryTargets|Local identities attached as secondary mission destinations.
archives|Local identities receiving optional archive and lore assignments.
reserved|Local identities excluded from the shuffled lane-node pool.
tiers|Difficulty tiers of route generation, or ordered purchasable hardware upgrades.
relay|Node type used for relay and filler systems.
workstation|Node type used when the workstation placement condition passes.
scanner|Node type used for scanner systems.
archive|Node type used for optional archive systems.
extraIds|Additional generated identities; must align with Extra Labels and Extra Types.
extraLabels|Display names corresponding by index to Extra IDs.
extraTypes|Node types corresponding by index to Extra IDs.
fillerPrefix|Identity prefix for filler systems added up to the tier node count.
fillerLabel|Display-name prefix for filler systems.
safeLaneLabel|Caption prefix for maintenance lanes.
transitLabel|Caption prefix for transit systems.
sensorLabel|Caption prefix for generated scanner systems.
fillerLabelOffset|Offset subtracted from generated indices when numbering filler captions.
entryPosition|3D position of the entry gateway.
entrancePosition|3D position of the first branch node.
destinationPosition|3D position of the shared destination.
radius|Radius in graph units of the route volume.
laneRadius|Radius in graph units used to separate route lanes.
jitter|Seeded random placement offset in graph units.
satelliteRadius|Base distance of attached targets from their parent system.
satelliteGrowth|Additional satellite separation per generated system.
minimumSpacing|Minimum preferred node spacing during placement, in graph units.
separationSpacing|Node spacing targeted by separation passes, in graph units.
separationForce|Strength of each post-placement separation adjustment.
placementAttempts|Maximum seeded placement attempts before settling on a position.
separationPasses|Number of post-placement passes to reduce node overlap.
crossoverFromLane|Zero-based source lane index for the crossover connection.
crossoverToLane|Zero-based destination lane index for the crossover connection.
crossoverStep|Step along the lanes where the crossover is placed.
directSecurity|Security expression for nodes on the direct approach.
routeSecurity|Security expression for generated route nodes; Step, Lane and SafeLane are available here.
workstationWhen|Generation-time condition selecting workstation instead of relay on a route.
glyph|Existing presentation glyph/model. Gameplay matching uses tags and states instead.
color|RGBA tint. HDR fields may contain components greater than 1.
tags|Semantic tags used by action and node matching conditions.
security|Initial node security formula; action costs can read effective security after modifiers.
auras|Defense modifiers emitted by this node type when activation conditions pass.
shortcut|Keyboard key that invokes this action when applicable.
shortcutLabel|Player-facing shortcut caption; keep consistent with Shortcut.
legacyGroup|Compatibility grouping for older tool-enum API adapters.
presentationPriority|Action ordering priority in the workstation; lower values are offered earlier within the same usability group.
operatorTarget|Runs on the operator rather than a selected node.
queueable|Allows this tool to be queued behind another action.
initiallyAvailable|Grants the action without purchasing research.
futureNeighborQueue|Allows authored future-neighbor queuing when the futureQueue stat permits it.
traceFree|Forces this action's final trace cost to zero.
visible|Condition deciding whether this action is offered on the selected target.
requirements|Execution requirements checked when an action starts or an upgrade is purchased.
queueRequirements|Requirements checked when enqueueing; execution requirements are checked again at action start.
duration|Time expression in simulation seconds. Node-targeted tools require positive duration.
baseTrace|Action trace before environmental pressure and final modifiers.
useLimit|Per-operation charge limit. Zero means unlimited uses.
effects|Ordered mutations applied when this action, trigger or effect group runs.
branch|Player-facing research branch caption.
cost|Research price in XP.
waypointTier|Planning waypoint capability granted by this technology, from 0 to 4.
special|Requires equipping this technology in a loadout slot before grants/modifiers apply.
parents|All prerequisite technologies that must be unlocked before purchase.
grants|Actions unlocked by this technology; special technologies must also be equipped.
category|Player-facing hardware category caption.
statLabel|Player-facing label for the hardware's displayed stat.
displayedStat|Baseline/modifiable stat shown in the hardware UI, such as compute or slots.
price|Credit cost to purchase this hardware tier.
disablesDefense|While active, this status suppresses the target's defense behavior.
preventsTrace|While active, this status prevents trace generation for its affected context.
archiveCredits|Credit bonus per recovered archive.
archiveXp|XP bonus per recovered archive before its cap.
archiveXpCap|Maximum archive XP banked per operation.
quietCredits|Credit bonus when the authored quiet conditions pass.
quietXp|XP bonus when the authored quiet conditions pass.
rules|Ordered settlement rules whose conditions determine credit and XP awards.
day|Starting career day, also available to difficulty and reward formulas.
reportLimit|Maximum operation reports retained in a career.
logLimit|Maximum action log entries retained.
researchDuringOperations|Allows research purchases during an active operation.
hardwareDuringOperations|Allows rig upgrades during an active operation.
equipDuringOperations|Allows special-ability loadout changes during an active operation.
allowTaskCancellation|Allows cancelling an active tool and its queue.
refundFraction|Fraction of invested research XP refunded, from 0 to 1.
traceLimit|Trace value at which the operation is caught.
traceWarning|Trace value that enters the warning band; below Trace Danger.
traceDanger|Trace value that enters the danger band; below Trace Limit.
maxTick|Maximum simulation time slice in seconds used when advancing an operation.
welcome|Narrative message shown at the start of a career.
outcomes|Exactly one policy per Delivered, Aborted and Caught outcome.
simulationSpeeds|Positive time-scale choices offered in the workstation.
initialTechnologies|Research granted to a fresh career at no cost.
stats|Named baseline numeric stats. Declare custom stats here before referring to them.
ranks|Career rank thresholds in completed jobs and their titles.
jobs|Completed-contract count required for this rank.
outcome|Operation result handled by this policy.
verdict|Player-facing report heading for this outcome.
narrative|Outcome report narrative; can interpolate supported context placeholders.
bankRewards|Banks settlement credits and XP for this outcome.
recordCompletion|Records the contract as completed for career progression.
bankLore|Adds recovered lore to the permanent career collection.
dayAdvance|Nonnegative number of career days advanced after this outcome.
stat|Stat identifier read or modified by this rule.
priority|Stat application order; ascending priority with stable authored order for ties.
operation|Arithmetic operation applied to the accumulated stat or numeric operands.
value|Constant numeric value or a formula used by this rule. SetState uses this boolean as its new state.
on|Simulation event that dispatches this trigger.
onceKey|Optional operation counter key ensuring this trigger fires only once; shared keys share the limit.
adjacentOnly|Limits aura application to neighboring systems.
stack|Allows multiple matching sources of this aura to stack.
source|Activation condition evaluated on the node emitting the aura.
modifier|Stat adjustment emitted by this aura.
scope|Target selection: selected target, all nodes, neighbors, entry, or a named node.
filter|Condition applied to candidate selected/countable nodes.
input|Runtime value to read. Counter, status and hardware inputs use Key.
key|Counter, status or hardware ID used by the chosen numeric input or counter effect.
left|Left numeric operand.
right|Right numeric operand. Divide and Modulo require a nonzero divisor.
yes|Numeric expression evaluated when the condition is true.
no|Numeric expression evaluated when the condition is false.
minimum|Inclusive random lower bound, or minimum security retained by a security effect.
maximum|Inclusive seeded random upper bound; random rules are only valid in network generation.
conditions|Child conditions combined by All or Any; order is preserved.
state|Node state to test or assign. Reachable and DefenseActive are computed and cannot be assigned.
technology|Research definition tested by this condition.
equipped|Tests whether the research is equipped instead of only unlocked.
comparison|Numeric relationship required between Left and Right.
targets|Node selector determining which systems receive this effect.
amount|Trace/security change or counter increment. Negative trace/security values reduce the current value.
status|Timed status definition applied by this effect.
operatorStatus|Applies the status to the operator rather than selected nodes.
message|Player-facing requirement explanation or action log message.
terms|Ordered text styling rules; longer matching phrases take precedence.
phrase|Plain phrase or regular expression to highlight in gameplay text.
regex|Treats Phrase as a regular expression rather than a literal phrase.
bold|Renders matching text in bold.
r|Red color channel, preserving HDR intensity.
g|Green color channel, preserving HDR intensity.
CelShading|Enables banded lighting on graph node surfaces.
LightBands|Number of cel-lighting brightness bands.
ShadowBrightness|Brightness multiplier for shaded node surfaces.
BandSoftness|Width of transitions between cel-lighting bands.
LightDirection|Direction of the simulated graph lighting.
SurfaceEmission|Self-lit contribution of node surfaces.
Outlines|Enables graph node outlines.
OutlineColor|RGBA tint used for node outlines.
OutlinePixels|Outline width in screen pixels.
OutlineOpacity|Opacity of node outlines, from 0 to 1.
CreaseAngle|Surface angle in degrees above which an edge is considered a crease.
DepthThreshold|Depth discontinuity threshold for detecting outline edges.
Bloom|Enables subtle glow around bright graph surfaces.
BloomIntensity|Strength of node bloom.
BloomRadius|Node bloom spread in screen pixels.
SelectionGlow|Enables glow on the selected node.
SelectionColor|HDR tint for selected-node glow.
SelectionGlowIntensity|Intensity of the selected node's glow.
SelectionRimIntensity|Intensity of the selected node's edge rim light.
SelectionGlowRadius|Selected glow spread in screen pixels.
PulseHz|Selection pulse cycles per real-time second.
PulseAmount|Selection pulse amplitude; reduced-motion settings suppress pulsing.
ShowSelectionRing|Shows a ring around the selected node.
ShowNodeProgress|Shows task progress and completion badges on graph nodes.
ProgressColor|Tint of the filled task-progress arc.
ProgressTrackColor|Tint of the unfilled task-progress track.
ProgressWidth|Width of the task-progress arc in graph units.
ProgressRadiusScale|Scale of the progress ring relative to the node.
CompletionBadgeSize|Size of the task completion badge in graph units.
MouseProximityReveal|Reveals package-managed labels near the pointer. The workstation's focused-card and hover rules still apply.
RevealInnerRadius|Pointer distance in screen pixels at which a package label is fully visible.
RevealOuterRadius|Pointer distance in screen pixels beyond which package labels are hidden.
RevealFadeSeconds|Real-time seconds to fade package labels; zero changes opacity immediately.
KeepSelectedLabelVisible|Keeps the selected package label visible outside proximity range.
TypeFontSize|Package label type-caption font size in pixels.
NameFontSize|Package label system-name font size in pixels.
MetaFontSize|Package label metadata font size in pixels.
BackgroundColor|RGBA backing tint of package label cards.
Padding|Package label inner padding in pixels.
CornerRadius|Package label corner rounding in pixels.
MinWidth|Minimum package label width in pixels.
MaxWidth|Maximum package label width in pixels.
AboveNodeGap|Gap between the node and its package label in pixels.
LabelSpacing|Minimum preferred spacing between package label cards in pixels.
StackRows|Maximum number of upward label-stacking rows.
".Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Split(new[] { '|' }, 2))
 .ToDictionary(p => p[0], p => p[1]);

		/// <summary>Resolves the authoring description for a serialized content field.</summary>
		/// <param name="field">Definition or rule field reflected from JSON types.</param>
		/// <returns>Tooltip text from attributes or the shared help table.</returns>
		/// <exception cref="InvalidOperationException">No description is registered for the field name.</exception>
		public static string Description(FieldInfo field)
		{
			if (field.DeclaringType == typeof(ContractDefenseDefinition) && field.Name == "kind")
				return "CredentialLock protects downloads; SecuritySweep periodically adds trace while its controller remains active.";
			if ((field.DeclaringType == typeof(DefenseCondition) || field.DeclaringType == typeof(DefenseEffect)) &&
				field.Name == "operation")
				return "KeyServer acquires a shared credential; LockedTarget spoofs one download gate; SweepController permanently disables a sweep.";
			// Starter rack rows use their own row-specific description.
			if (field.DeclaringType == typeof(RigStarterPart) && field.Name == "row")
				return "Zero-based starter rack row; the footprint must fit without overlap.";

			// Starter rack columns use their own column-specific description.
			if (field.DeclaringType == typeof(RigStarterPart) && field.Name == "column")
				return "Zero-based starter rack column; upgrades preserve this coordinate.";

			// Prefer a Tooltip attribute on the field when one is present.
			var tooltip = field.GetCustomAttribute<TooltipAttribute>();
			if (tooltip != null)
				return tooltip.tooltip;

			// Look up the shared help table and throw when no description is registered.
			return Help.TryGetValue(field.Name, out var text)
				? text
				: throw new InvalidOperationException("Missing property description: " + field.DeclaringType.Name +
													  "." + field.Name);
		}

		/// <summary>Friendly label for a property name. Keeps acronyms readable (Xp becomes XP, Id becomes ID).</summary>
		public static string DisplayName(string name)
		{
			// Nicify the property name, then restore acronym casing.
			return Regex.Replace(ObjectNames.NicifyVariableName(name), @"\b(Xp|Id|Ids)\b",
								 m => m.Value == "Xp" ? "XP" : m.Value == "Id" ? "ID" : "IDs");
		}

		/// <summary>Walks all definition and rule types and reports fields missing descriptions.</summary>
		/// <returns>Error messages for fields that would throw from <see cref="Description" />.</returns>
		public static IEnumerable<string> CheckCoverage()
		{
			// Track visited types and collect every missing-description error.
			var seen = new HashSet<Type>();
			var errors = new List<string>();

			// Visit every definition and rule type to reach all of their fields.
			foreach (var type in ContentJson.DefinitionTypes.Concat(ContentJson.RuleTypes))
				Visit(type);

			return errors;

			// Local helper that walks a type graph and checks each field description.
			void Visit(Type type)
			{
				// Recurse into the element type of arrays.
				if (type.IsArray)
				{
					Visit(type.GetElementType());
					return;
				}

				// Skip primitives, enums, strings, and types that were already visited.
				if (type.IsPrimitive || type.IsEnum || type == typeof(string) || !seen.Add(type))
					return;

				// Check every serialized field of this type.
				foreach (var field in ContentJson.Fields(type))
				{
					// Record the error message when a field has no description.
					try
					{
						Description(field);
					}
					catch (Exception ex)
					{
						errors.Add(ex.Message);
					}

					// Recurse into the field's own type.
					Visit(field.FieldType);
				}
			}
		}
	}
}
