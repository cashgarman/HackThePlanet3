# HackThePlanet — Testable Units Inventory

Identification only (no tests implemented). Scope: **`Assets/HackThePlanet/Runtime`**.

**Excluded:** `Editor/` (UnityEditor menus and play-mode harnesses), MonoBehaviour UI (`WorkstationApp`, `GraphViewport`, `NetworkCanvas` rendering, `CircuitBackdrop`, `WaypointOverlay`, most of `WorkstationRig` / `WorkstationActionBar`), and ScriptableObject definition assets that only hold serialized fields (see [Appendix A](#appendix-a--definition-scriptableobjects-data-only)).

**Ratings:** **Easy** = deterministic, few dependencies; **Medium** = needs `GameCatalog`, fixture graph, or `RuleContext`; **Hard** = filesystem, `Resources`, `JsonUtility`, globals, or large integration surface.

---

## ContentJson.cs

### ContentEntry / ContentManifest

Data fields only — not logic units.

### ContentBundle

| Member | What it does | Rating |
|--------|----------------|--------|
| `Resolve(string key)` | Resolves a keyed asset from the loaded bundle | Medium |
| `Get<T>(category)` | Typed lookup for a content category | Medium |
| `Dispose()` | Clears cached scriptable objects | Easy |
| `Errors`, `Files`, `Objects`, `InvalidEntries`, `Manifest` | Observable load/validation state | Medium |

### ContentJson (static)

| Member | What it does | Rating |
|--------|----------------|--------|
| `DefinitionTypes`, `RuleTypes`, `Categories` | Registries of content types and JSON categories | Easy |
| `CategoryType(string)` | Maps category string to CLR type | Easy |
| `Fields(Type)` | Lists serializable fields for a content type | Easy |
| `IsRule(Type)` | Whether a type is a gameplay rule | Easy |
| `Encode(object)` | Serializes a content object to JSON | Medium |
| `Decode<T>(string)` | Deserializes JSON to a new instance | Medium |
| `Populate(string, object, ContentBundle)` | Hydrates an object from JSON with bundle refs | Medium |
| `SafePath(directory, relative)` | Blocks path traversal when resolving files | Easy |
| `Load(directory, overrides?)` | Loads full content tree from disk | Hard |
| `Validate(bundle)` | Returns cross-reference and schema error messages | Medium |

### GameData (static)

| Member | What it does | Rating |
|--------|----------------|--------|
| `Reset()` | Clears the cached content bundle | Easy |
| `Presentation(bool network)` | Builds graph presentation resource configuration | Hard |
| `ValidationLaunch` | True when launched with `--validate-content` | Easy |
| `Current`, `DirectoryPath` | Cached bundle and data folder (prefer `ContentJson.Load` in tests) | Hard |

---

## GameDefinitionValidator.cs

### GameDefinitionValidator (static)

| Member | What it does | Rating |
|--------|----------------|--------|
| `Definitions(GameDefinition)` | Flattens all gameplay definitions under the game root | Easy |
| `Validate(GameDefinition)` | Structural, reference, formula, and content checks | Medium |
| `ThrowIfInvalid(GameDefinition)` | Throws when validation returns any errors | Easy |

---

## GameCatalog.cs

### ActionInfo / HardwareInfo / NodeTypeInfo

| Member | What it does | Rating |
|--------|----------------|--------|
| Public id, title, description, and related properties | Read-only projection of definition assets | Easy |

### GameCatalog

| Member | What it does | Rating |
|--------|----------------|--------|
| Constructor `(GameDefinition)` | Builds lookup indexes from a game root | Medium |
| `Default` | Singleton catalog over loaded game data | Hard |
| `Actions`, `Hardware`, `NodeTypes`, `Companies`, `Technologies`, `StoryCount` | Catalog projections | Medium |
| `FindAction(id)` | Resolves action metadata by id | Easy |
| `Contracts(Career)` | Lists contracts available to a career with gating | Medium |
| `CreateCareer()` | Creates a starter career from catalog defaults | Medium |
| `Dispose()` | Disposes cloned definition objects | Easy |
| `Action(id)` (internal) | Resolves action definition | Easy |
| `Technology(id)` (internal) | Resolves technology definition | Easy |
| `NodeType(id)` (internal) | Resolves node type definition | Easy |
| `Network(id)` (internal) | Resolves network template | Easy |
| `Contract(id)` (internal) | Resolves contract definition | Easy |
| `BaseStat(id)` (internal) | Reads catalog base stat value | Easy |
| `CompanyFor(contractId)` (internal) | Maps contract to company label | Easy |
| `Context(Career, Contract?)` (internal) | Builds a rule-evaluation context | Easy |
| `DescribeContract(d, career)` (internal) | Builds runtime `Contract` with formulas and lore | Medium |
| `DescribeTechnology(d)` (internal) | Builds runtime `Technology` view | Medium |
| `Format(text, RuleContext)` (internal) | Substitutes tokens in UI strings | Medium |
| `InvalidateDefault()` (internal) | Clears cached default catalog | Easy |

---

## GameContent.cs

### Enums (NodeKind, JobKind, ToolKind, ContractIntel)

No methods — use as test inputs.

### NetNode, NetLink, LoreFragment, and related DTOs

Serializable state bags — construct as fixtures; logic lives in simulation and catalog.

### Contract, Technology

Runtime DTOs built by catalog — use as fixtures.

### GameContent (static)

| Member | What it does | Rating |
|--------|----------------|--------|
| `Catalog`, `Companies`, `Story`, `Tech` | Facade over `GameCatalog.Default` | Hard |
| `FindTech(id)` | Finds technology by id | Medium |
| `Contracts(Career)` | Lists contracts for a career | Medium |
| `Description(NodeKind)` | Default description for a node kind | Easy |

---

## GameSimulation.cs (+ partial NetworkWaypoints)

### Career

| Member | What it does | Rating |
|--------|----------------|--------|
| `Level(id)`, `SetLevel(id, level)` | Gets or sets installed hardware tier | Easy |
| `cpu`, `bandwidth`, `memory` | Legacy accessors for hardware tiers | Easy |
| Public fields and lists | Save schema state | Easy (fixtures) |

### QueuedTool

| Member | What it does | Rating |
|--------|----------------|--------|
| Constructors / `kind` | Maps action id and legacy `ToolKind` | Easy |

### ToolTask, Operation, OperationReport, ToolPreview

State DTOs for active operation — fixtures for sim tests.

### GameSimulation

| Member | What it does | Rating |
|--------|----------------|--------|
| `ValidateCareer(career, catalog)` (static) | Validates v3 save against catalog | Medium |
| `Node(id)` | Finds node in active operation | Easy |
| `Neighbors(node)` | Adjacent nodes via links | Easy |
| `Reachable(node)` | Whether node is targetable from control | Medium |
| `ControlledDistance(target)` | BFS hops from nearest controlled node | Medium |
| `Route(id, controlledOnly?)` | Shortest path via GraphExplorer | Medium |
| `ObjectiveDone(node)` | All objectives on node satisfied | Medium |
| `Has(id)`, `Equipped(id)` | Tech unlocked / equipped | Easy |
| `Start(job)` | Begins contract operation | Medium |
| `Disconnect(caught?)` | Ends operation and builds report | Medium |
| `SetPaused`, `SetManualPause` | Pause control | Easy |
| `Tick(delta)` | Advances simulation time and tasks | Medium |
| `Stat(stat)` | Evaluates stat with modifier stack | Medium |
| `EffectiveSecurity(node)` | Rounded security with floor | Medium |
| `Preview(action, target)`, `Preview(ToolKind, id)` | Pre-action validation snapshot | Medium |
| `Execute(action, target)`, `Execute(ToolKind, id)` | Runs or queues an action | Medium |
| `CanQueue`, `ClearQueue`, `StopTask`, `Decoy` | Queue and task control | Medium |
| `AvailableActions`, `UsesRemaining`, `ActionResearchHint`, `ActionTitle` | Action UI metadata | Medium |
| `Counter(key)`, `StatusRemaining(id, target?)` | Runtime counters and statuses | Easy |
| `BuyTech`, `ToggleEquip`, `RefundResearch`, `ResearchVisible`, `RevealResearch`, `EnsureResearchReveal` | Research meta | Medium |
| `UpgradePrice`, `PreviewUpgrade`, `Upgrade` | Hardware shop | Medium |
| `ObjectiveCount`, `ObjectiveComplete`, `Progress`, `Rank`, `StoryComplete`, trace/reward strings | Derived readouts | Medium |
| `RefreshGraph()` | Syncs known graph document | Medium |
| `Log(message)` | Timestamped operation log line | Easy |
| `DebugAddCredits`, `DebugAddXp`, `DebugUnlockAllTech` | Debug grants | Easy |
| `Context(...)` (internal) | Rule context for current sim state | Medium |
| `Stat(stat, RuleContext)` (internal) | Stat with explicit context | Medium |
| `ApplyEffects`, `AddCounter`, `ApplyStatus`, `Select`, `SetState`, `Reveal` (internal) | Effect and rule application | Medium |

### Waypoints (partial on GameSimulation)

| Member | What it does | Rating |
|--------|----------------|--------|
| `WaypointColors` (static) | Marker color palette | Easy |
| `WaypointTier` | Max waypoint features from research | Medium |
| `Waypoints(job)` | Lists markers for contract instance | Easy |
| `PlaceWaypoint`, `RemoveWaypoint`, `EditWaypoint` | CRUD for planning markers | Medium |
| `WaypointPath(nodes, links, entry, target)` (static) | Shortest path for route preview | Medium |

### CareerStorage (static)

| Member | What it does | Rating |
|--------|----------------|--------|
| `Load(path?, catalog?)` | Loads JSON save with validation and fallback | Hard |
| `Save(career, path?, catalog?)` | Validates and atomically writes save | Hard |
| `Reset(path?)` | Writes fresh default career | Hard |
| `SavePath`, `Warning` | Path and last I/O warning | Easy |

---

## RigSimulation.cs (partial on GameSimulation)

### RigPart, MemoryReservation, RigIssue, ResearchRefundPreview

DTOs and simple constructors — fixtures.

### Rig logic on GameSimulation

| Member | What it does | Rating |
|--------|----------------|--------|
| `PartSize(model, rotated)` (static) | Module footprint in grid cells | Easy |
| `Module(id)`, `Part(instanceId)`, `PartAt(row, column)` | Rig lookups | Easy |
| `CanPlace`, `TryPlaceModule`, `PlaceFirstFree`, `RemoveModule`, `RotateModule` | Grid placement rules | Medium |
| `BuyModule`, `SellModule`, `ModuleSalePrice` | Module shop and resale | Medium |
| `UpgradeChassis`, `PreviewChassis` | Chassis upgrade | Medium |
| `RigValid`, `RigIssues`, `RigProblems` | Build validation messages | Medium |
| `RigNeighborCount(instance, familyOrBranch)` | Adjacency for rig rules | Medium |
| `PreviewResearchRefund` | Refund breakdown | Medium |
| Power, cooling, storage, memory properties | Derived rig capacity and usage | Medium |
| `PreviewModule(modelId)` | Preview RAM/cost for module | Medium |
| `IsAbilityActive(action, target?)` | Persistent ability reservation check | Medium |
| `RigBaseStat(stat)` (internal) | Base stat before operation modifiers | Medium |

---

## GameplayRules.cs

### RuleContext

| Member | What it does | Rating |
|--------|----------------|--------|
| `Tagged(tag)` | Whether node type includes tag | Easy |
| `State(NodeState)` | Node flag or derived state | Easy |
| `ForRig(instance)`, `At(node)` (internal) | Clone context for rig or node | Easy |
| `Enter`, `Leave` (internal) | Recursion depth guard (max 64) | Easy |
| Public fields | Inputs for rule evaluation | Easy |

### Number (abstract) and subclasses

Each exposes `Evaluate(RuleContext)`:

| Type | What it does | Rating |
|------|----------------|--------|
| `ConstantNumber` | Literal value | Easy |
| `StatNumber` | Reads simulation stat | Medium |
| `InputNumber` | Formula input binding | Easy |
| `BinaryNumber` | Two-operand formula | Easy |
| `ConditionalNumber` | Branch on condition | Easy |
| `RandomNumber` | Seeded integer range | Easy |

### RuleMath (internal static)

| Member | What it does | Rating |
|--------|----------------|--------|
| `Apply(op, left, right)` | Pure arithmetic with divide/mod guards | Easy |

### Condition (abstract) and subclasses

Each exposes `Test(RuleContext)`:

| Type | What it does | Rating |
|------|----------------|--------|
| `AlwaysCondition` | Always true | Easy |
| `AllCondition`, `AnyCondition`, `NotCondition` | Boolean combinators | Easy |
| `NodeStateCondition` | Node state predicate | Easy |
| `NodeTagCondition` | Node type tag | Medium |
| `ActionCondition` | Action id match | Medium |
| `ResearchCondition` | Technology ownership | Medium |
| `RigNeighborCondition` | Rack adjacency | Medium |
| `NamedNodeCondition` | Specific node id | Easy |
| `CompareCondition` | Numeric comparison | Easy |

### Effect (abstract) and subclasses

Each exposes `Apply(RuleContext)`:

| Type | What it does | Rating |
|------|----------------|--------|
| `SetStateEffect` | Sets node state flags | Medium |
| `RevealEffect` | Fog-of-war reveal | Medium |
| `TraceEffect` | Changes operation trace | Medium |
| `ApplyStatusEffect` | Applies timed status | Medium |
| `SecurityEffect` | Changes node security | Medium |
| `CounterEffect` | Modifies named counter | Easy |
| `ConditionalEffect` | Runs nested effects when condition passes | Medium |
| `LogEffect` | Appends operation log | Easy |
| `RecoverLoreEffect` | Adds lore fragment | Medium |

---

## NetworkGenerator.cs

### GeneratedNetwork

DTO for nodes, links, and approach lanes — assert in tests.

### NetworkGenerator (static)

| Member | What it does | Rating |
|--------|----------------|--------|
| `Version` | Generation algorithm version constant | Easy |
| `Generate(job, career)` | Builds network using default catalog | Medium |
| `Generate(job, career, catalog)` | Seeded topology, security, objectives | Medium |

---

## GameplayRichText.cs

### GameplayRichText (static)

| Member | What it does | Rating |
|--------|----------------|--------|
| `Format(text)` | Applies palette highlight rules without overlap | Easy |
| `Plain(richText)` | Strips rich-text tags | Easy |
| `ReloadPalette()` | Clears cached palette and rules | Easy |
| `Palette` | Gets or sets palette used for rules | Easy |

---

## GameplayTextPalette.cs

### GameplayTextPalette

| Member | What it does | Rating |
|--------|----------------|--------|
| `DefaultTerms()` (static) | Built-in highlight term list | Easy |

---

## NetworkCanvas.cs — Palette (static only)

| Member | What it does | Rating |
|--------|----------------|--------|
| `Hex(string)` | Parses `#RRGGBB` to Color | Easy |
| `Kind(NodeKind)` | Color for node kind | Easy |
| `Branch(string)` | Color for research branch | Easy |

---

## WorkstationActionBar.cs — static only

| Member | What it does | Rating |
|--------|----------------|--------|
| `ActionGlyph.ColorFor(actionId)` | Tint color for action id | Easy |

---

## WorkstationRig.cs — static only

| Member | What it does | Rating |
|--------|----------------|--------|
| `RigGlyph.ForModule(model, tint, size)` | Builds module glyph (UI/visual) | Hard |

---

## GraphViewport.cs — static helpers only

| Member | What it does | Rating |
|--------|----------------|--------|
| `DescribeNode(...)` (internal) | Intel string for a node | Medium |
| `DescribeTechnology(...)` (internal) | Research card description | Medium |

---

## SignalVisuals.cs

| Member | What it does | Rating |
|--------|----------------|--------|
| `LabelFor(SignalEffects mode)` | Human label for effects mode | Easy |

---

## NetworkAppearance.cs

| Member | What it does | Rating |
|--------|----------------|--------|
| `Create(out settings)` | Loads node prefabs and clones visual settings | Hard |

---

## Appendix A — Definition ScriptableObjects (data-only)

Files include: `ActionDefinition`, `CareerDefinition`, `CompanyDefinition`, `ContractDefinition`, `GameDefinition`, `GameplayDefinition`, `HardwareDefinition`, `LoreContent`, `NetworkDefinition`, `NodeTypeDefinition`, `RewardDefinition`, `RigChassisDefinition`, `RigModuleDefinition`, `RigRulesDefinition`, `StatusDefinition`, `TechnologyDefinition`, and related nested rule types stored as fields.

Public fields and properties are authoring data, not logic. Exercise them through `GameDefinitionValidator.Validate` and simulation/catalog paths rather than per-field unit tests.

---

## Appendix B — Editor (not NUnit unit-test targets)

| Entry | Role |
|-------|------|
| `GameValidation.Run` | Full simulation validation suite |
| `NetworkValidation`, `RigValidation`, `DataDrivenValidation`, `BalanceValidation`, `ContentValidation` | Domain integration checks |
| `GameBuildTools` | `Temp/HackThePlanet.command` dispatch and smoke tests |

Run via **Hack The Planet** menus or command file with Unity Editor open.

---

## Appendix C — Related tests elsewhere in the repo

`Packages/com.cashg.graph-explorer/Tests/Editor/` — **Cashg.GraphExplorer.Tests.Editor** (~77 EditMode NUnit tests). Not part of `Assets/HackThePlanet`; HackThePlanet has no test asmdef yet.

---

## Suggested layering (for future test work)

1. **Easy:** `RuleMath`, leaf `Condition`/`Number` types, `ContentJson.SafePath`, `Palette.Hex`, `GameplayRichText`, `Career.Level`, `QueuedTool.kind`.
2. **Medium:** `NetworkGenerator.Generate` (golden seeds), `GameDefinitionValidator.Validate`, `GameSimulation` reachability and `Execute`/`Tick` with in-memory catalog.
3. **Hard / integration:** `ContentJson.Load`, `CareerStorage`, `NetworkAppearance.Create`, chains through `GameCatalog.Default`.
