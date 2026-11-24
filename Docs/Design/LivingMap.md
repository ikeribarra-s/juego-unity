# Living Map — a dungeon that feels alive and never plays the same twice

> Status: **Milestone 1 implemented (2026-11-23)** — see §11 "Milestone 1 — status". Milestones 2–4 are
> still design spec. Owner: game-designer. Implementers per item in each table (the "Agent" column) and in §8.
> Decisions taken by the user (§12): kit dressing yes; map stays 4×4 sections for now; one modifier per run;
> the director MAY fake creature cues (with a hook for real creatures); "3 generators" stays the objective.
>
> User request (verbatim): *"I want to work a bit on the creation of the map; I want a map that feels
> alive and a game that doesn't feel that repetitive."*

---

## 1. Pitch

*"Every run the dungeon is a different place with its own weather, its own landmarks and its own bad
luck — and it reacts to you: doors slam when Coso breaks a vase, the lights die just as the creature
starts hunting, and you tell the story of 'the flooded crypt run'."*

## 2. Player experience

- **Arrival:** a run card names the place and its mutator ("*The Drowned Crypt — Blackout*"). You can
  orient from the first minute: "the chapel with the bell", "the collapsed bridge", "the big tree".
- **Exploring:** quiet stretches with dripping water and far-off creaks; rats scatter from your torch;
  a draught makes your flame lean toward an unexplored passage.
- **Tension builds** on a rhythm you can feel but not predict: lights stutter, a door creaks shut
  behind you, the creature's bell gets closer.
- **Spike → relief:** a slam, a scream, a chase; then the director backs off and lets the team regroup
  and laugh.
- **Next run:** different layout skeleton, different set pieces, different objective twist.

---

## 3. Audit — why the current map feels static and repetitive

Live generator = `MultiSectionDungeonGenerator` (SampleScene: 4×4 sections of 50×46 cells ≈ 200×184 m).

| # | Problem | Concrete cause in code/data |
|---|---|---|
| A1 | **Flat world** | Every floor is built at y = 0 (`BuildHorizontalMesh`); no stairs, pits, balconies, ramps. The kits' `Stairs_Modular`, `Stairs`, `BridgeSection`, `Floor_Hole_*` are unused. |
| A2 | **Same room shapes** | Gothic/Catacombs = BSP rectangles; Graveyard/Hell/Cavern = one CA blob per section. Corridors are always `CorridorWidth` A* paths. No L/T/circular rooms, no alcoves, no pillared halls. |
| A3 | **Predictable layout** | Sections sit on a fixed grid and `ConnectSections` links **every** right/down neighbour → a uniform lattice of loops: no hubs, no dead ends, no "far side" of the map, no chokepoints. |
| A4 | **Primitive, repeated props** | Live dressing is cubes/cylinders (`BuildGravestone`, `BuildPillar`, `BuildRock`…) — the **~160 kit models** (Ruins 92, Dungeon 48, Furniture 20) are only used by the inactive `DungeonGenerator` and the unplaced `ChunkBuilder` chunks. Each theme repeats 3–4 builders at a fixed density. |
| A5 | **Lighting sameness** | One torch prefab, one colour, uniform `TorchDensity` 0.1 everywhere. `LightingMoodController` has Normal/Tense/Blackout/Chase/Extraction profiles but **nothing calls `SetMood`**. Lights never die, never change. |
| A6 | **No landmarks** | Nothing is unique; within a section every room looks alike → you navigate by luck. |
| A7 | **Objectives identical every run** | Always 3 generators in identical undecorated `Transition` boxes; evidence = the same hand-placed scene objects moved around (one per section type); extraction prefers Graveyard; player start always 8–18 tiles from extraction. `MissionConfig` never changes. |
| A8 | **Nothing moves** | No doors in the live generator, no dynamic events, no creatures besides the capsule, no physics props in rooms. |
| A9 | **Thin soundscape** | One `AmbientAudio` loop; **no `SFX/Environment` folder exists**. `EnvironmentReverb` is the only spatial variety. |
| A10 | **No run identity** | No mutators, no secrets, no rare events, no seed-shareable "named" runs. |

---

## 4. Layer (a) — Structural variety

| ID | Item | Player value | Implementation sketch | Cost | Perf | Assets / agent |
|---|---|---|---|---|---|---|
| **S1** | **Room archetypes** inside sections: Hall (pillared), Cross, L, Circular (rasterised), Alcove room, Cell block, Ossuary (narrow niches) | Rooms read differently; hiding spots | `RoomArchetype` SO (footprint stamp as `bool[,]`/rules + dressing profile). `CarveGothic` picks an archetype per BSP leaf instead of a plain rect. Stamps only write `_floor` before prune. | M | none (geometry) | level-designer |
| **S2** | **Set-piece rooms** (hand-authored prefabs) stamped into the procedural layout: Chapel with bell, Collapsed bridge over a pit, Flooded cistern, Library, Torture hall, Kitchen, Big dead tree courtyard, Chucho's Den | Memorable moments + landmarks | `SetPieceDefinition` SO: prefab, footprint (tiles), door sockets (side + offset), allowed sections, max per run, weight. New pipeline step after `LayoutSections`: reserve a rect inside a section, carve its footprint + sockets, instantiate prefab after geometry. Prefabs built from the kits (reuse `ChunkBuilder` approach). | M (system) + S per room | each set piece ≤ 4 lights, ≤ 1 shadowed | level-designer (system + rooms), 3d-artist (missing pieces) |
| **S3** | **Use the kits for dressing** (replace primitives) | Instantly richer look, less repetition | Per-theme `PropPalette` SO (weighted `PropEntry` list — same struct as `DungeonGenerator.PropGroup`) assigned to `MultiSectionDungeonGenerator`; builders become "pick from palette", primitives stay the fallback. GPU instancing on kit materials. | M | +draw calls: use static batching (already `MarkStatic`) and instancing | level-designer; optimizer check |
| **S4** | **Verticality**: sunken rooms (−1.5 m with stairs), mezzanine balconies in Halls, pit + bridge set piece, raised altar | Chases with height, views over a room, Trotín flying off ledges | Phase 1: per-room floor offset (`_floorHeight[,]` float grid) with ramp/stair tiles at doorways; walls extend down. Bridges/balconies only as set pieces (S2) to keep the grid 2D. NavMesh rebakes fine. | L | none | level-designer; 3d-artist (stairs fitting 1-tile grid) |
| **S5** | **Loops vs dead ends** | Real routes, chokepoints, "the far wing" | Replace "connect all neighbours" by: spanning tree of sections + `_extraLoopChance` (0.35) extra edges; 1–3 dead-end side rooms (rewarded with evidence/secret). Tunables on generator. | S | none | level-designer |
| **S6** | **Landmarks** — 1 per section, visible from afar | Navigation, callouts on voice chat ("meet at the bell") | Each section gets one "hero" prop/set piece: bell tower, giant statue (`Statue_Horse/Fox/Stag`), dead tree (`DeadTree_*`), lava crack glow, crystal. Unique colour light. Placed at section centre, kept clear. | S | 1 light each (unshadowed) | level-designer; 3d-artist (statue variants) |
| **S7** | **Biomes with distinct palettes** | Each area *feels* different | `BiomeProfile` SO per `SectionType`: wall/floor materials, fog tint, torch colour (amber / sick green / blue candle / red ember), ambient bed, reverb preset, prop palette. A `BiomeZone` trigger per section blends `LightingMoodController` fog/ambient + audio bed on entry. | M | fog/ambient are global: blend, don't stack | level-designer, sound-designer, 3d-artist (2–3 material sets) |
| **S8** | **Doors** (wood, iron gate, covered gothic arch) at some doorways | Hiding, noise, slams, Bobi gets stuck (plan §2.4) | `Door` : `IInteractable` hinge (kinematic, swing on E, physics-pushable when open), `NarrowPassage` hook later. Generator places at `_doorChance` (0.3) of room entrances using `Doors_GothicArch`/`Doors_RoundArch`/`Arch_Door` kit pieces. NavMesh: `NavMeshObstacle` carve when closed; creature opens doors (noise 0.4). | M | negligible | level-designer, 3d-artist (pivot fix), animator (none: procedural swing) |

> **S2 status (2026-11-24):** superseded by the **chunk grid** — the whole map is now stamped from the 42 authored chunk prefabs (corridors, rooms and set pieces), see `Assets/_Game/Environment/CLAUDE.md` §13. Set pieces = SP_* chunks (Bell Tower, Pit Bridge, Sewer, Well Room, Ossuary, Collapsed/Trap corridor, Chucho's Den), selected by door mask/theme/rarity. Remaining: themed corridor sets for Graveyard/Cavern/Hell, more Cross/T variants.

## 5. Layer (b) — "Alive" ambience

### 5.1 Ambient Director (the heart)

`AmbientDirector` (MonoBehaviour on the manager object, data in `DirectorProfile` SO) keeps a
**tension value T ∈ [0,1]** and a phase:

```
CALM ──(T ≥ 0.35)──► BUILD-UP ──(T ≥ 0.8 or creature Chase)──► SPIKE ──(after spike, 20–40 s)──► RELIEF ──► CALM
```

| Input | Effect on T |
|---|---|
| Time in Calm | +`_calmRise` 0.01/s |
| Creature Investigate / Point / Chase | +0.15 / +0.3 / set 1 |
| Loud player noise (≥ 0.6) | +0.1 |
| Generator running / extraction open | permanent floor +0.1 / +0.3 |
| Players grouped (all within 8 m) | −0.02/s (comfort) |
| Relief phase | forced decay to 0.1 |

**Outputs per phase** — the director spends a **budget of events** (`_eventsPerMinute` per phase: Calm 2, Build-up 5, Spike 0, Relief 1) choosing from `AmbientEventDefinition` SOs whose tags match the phase, filtered by distance to players (10–35 m: "heard, not seen"; some "in view" events only in Build-up). It also drives `LightingMoodController.SetMood` (Calm=Normal, Build-up=Tense, Spike=Chase) and the music/sting layer. Never fires two events within `_minEventGap` (6 s). It **nudges, never kills**: no director event can damage a player.

Cost **M**, perf: one tick at 2 Hz, events pooled. Agent: main session (code), game-designer (tuning).

### 5.2 Dynamic events (each one = an `AmbientEventDefinition` SO + a small `IAmbientEvent` component on a world object)

| ID | Event | Player value | Implementation | Cost | Perf | Assets / agent |
|---|---|---|---|---|---|---|
| E1 | **Light flickers & dies** (torch gutters out for 20–60 s, then relights or stays dead) | Dark patches move, tension | `TorchStateController` on Torch prefab: Lit / Guttering / Out; uses `LightFlicker` intensity scale + flame flipbook fade; `DynamicLightBudget` already ignores disabled lights. Re-light = interact with a carried torch. | S | *reduces* light count | sound-designer (gutter hiss, poof), main session |
| E2 | **Wind gust** blows along a corridor: torches lean & dim, a door creaks, embers stream | Telegraphs routes (draughts point to unexplored areas) | `WindGust` event: picks a corridor run, sweeps a trigger capsule; affected `Torch` children lean (flame quad rotation) and dim 50 % for 1.5 s; held torch too (8 % chance to go out). Ember `ParticleSystem` external force. | S | none | sound-designer (wind whoosh low), main session |
| E3 | **Doors creak / slam** (needs S8) | Classic jump scare + noise | Door gets `Creak(angle)` and `Slam()`; slam is noise 0.6 (creature hears it). | S | none | sound-designer (creak ×4, slam ×3) |
| E4 | **Distant sounds** (chains, a crying child-toy, footsteps that aren't yours, a bell) | Paranoia, cheap | Spawn a pooled 3D `AudioSource` at a NavMesh point 15–35 m away, behind a wall. One-shots from `SD_Amb_*`. Pompón false-alarm source. | S | audio voices ≤ 6 | sound-designer |
| E5 | **Dripping water / puddles** | Constant life in Cavern/Catacombs | Static `AmbientEmitter` points placed by the generator (ceiling drip + ripple decal flipbook). Random interval drips. | S | audio only; no lights | sound-designer (drips ×6), 3d-artist (ripple flipbook) |
| E6 | **Rats** scatter from light and noise | "Alive" in one glance; comedy when they run over the skeleton | `RatSwarm`: 3–6 low-poly rats (≤ 150 tris, vertex-animated or 2-frame flipbook card) wandering on a NavMesh area; flee from torches within 4 m / noise; squeak = noise 0.1 (can trigger Lumi/Pompón). Max 3 swarms active near players. | M | ≤ 18 skinned/instanced rats, no lights | 3d-artist (rat), animator (2-clip run), sound-designer (squeaks) |
| E7 | **Moving shadows** (a shape passing in front of a far torch) | Pure dread at zero gameplay cost | A dark billboard card slides across a light's cone, or a fake `Shadow` projector blob on a wall; only during Build-up, only out of reach. | S | 1 transparent quad | 3d-artist/flipbook |
| E8 | **Objects fall** (book off a shelf, pot breaks, chandelier drop as a set-piece) | Physical surprise; noise for the creature | `FallingProp` component: kinematic prop with a `Fall()` that enables physics + small impulse; pots swap to `Pot*_Broken` kit meshes. Noise per impact. | S | physics wakes briefly | level-designer (placement), sound-designer (book thud, pot smash) |
| E9 | **Ambient audio zones** | Each biome sounds different | `AmbientAudio` per biome on `BiomeZone` (S7) with crossfade; reverb presets per biome feed `EnvironmentReverb` as bias. | S | ≤ 2 looping beds at once | sound-designer (5 beds, −30 LUFS) |
| E10 | **Environmental storytelling clutter** (previous expedition: a dead skeleton explorer with a torch, scratched tally marks, a dog bowl with "CHUCHO", a half-eaten bone pile) | Lore + comedy + hints (they mark evidence or danger) | `StoryVignette` SO = small prefab cluster + placement rule (dead-end, near den, near generator). 1–2 per section. | S each | static | level-designer, 3d-artist (vignette props) |

## 6. Layer (c) — Run-to-run variety

| ID | Item | Player value | Implementation | Cost | Perf | Agent |
|---|---|---|---|---|---|---|
| R1 | **Run modifiers (mutators)** — 0–1 per run (2 on "cursed" rolls): **Blackout** (50 % of torches start Out, Lumi shines), **Fog** (fog ×2.5, sight ranges ×0.7), **Flooded section** (one section has 0.4 m water: walk speed ×0.7, splash steps noise ×2, items float), **Extra creature** (+1 Chucho), **Cursed evidence** (one item whispers — noise 0.2 pulse every 5 s — and is worth ×2), **Draughty** (wind gusts ×3), **Rat plague** | Biggest "this run is different" lever | `RunModifier` SO with `Apply(RunContext)` / `Remove`: each is a small class touching existing tunables (lighting profile, `EnemySpawner` count, `EvidenceItem` flag, director weights). `RunSetup` picks from the pool by seed and shows it on the run card (`MissionMessageUI.Show`). | S per modifier (system M) | Fog **lowers** cost; water plane = 1 transparent mesh | game-designer (pool), main session, level-designer (flood) |
| R2 | **Randomised objectives** | Not always "3 generators" | `MissionConfig` variants: generator count 2–4; objective pool: *Generators* (current), *Ritual* (place 3 candles on an altar — reuses `PhysicsGrabber`), *Find the key* (key evidence opens the extraction gate), plan §6.4 types later. Extraction chosen among 3 candidate zones, not always Graveyard. | M | none | game-designer, level-designer, main session |
| R3 | **Randomised evidence** | Different loot, different risk | Evidence spawned from an `EvidenceTable` SO (weighted `EvidenceItem` + prefab + biome affinity + fragile chance) instead of moving hand-placed scene objects; count from the existing `MissionConfig` formula. Kit props as bodies (Skull, Vase, Chest, Bag_Coins, Candles…). | M | none | level-designer, 3d-artist (evidence variants), ui-designer (icons via `gen.py icon`) |
| R4 | **Seeded but varied placement rules** | Fair but unpredictable | Placement constraints per run from a `PlacementProfile` SO (start ↔ extraction distance band now 20–60 % of map diameter, generators in different sections, evidence weighted to dead ends/set pieces). Show the seed on the run card so good runs can be shared. | S | none | level-designer |
| R5 | **Rare events** (1 in ~5 runs): the bell tower rings by itself (all creatures investigate), a "lucky" golden skull, a wandering ghost skeleton that copies the last player, the floor of a room collapses into a secret cellar | Stories, screenshots | `RareEventDefinition` SO + director hook with low weight and per-run cap 1. | M each | | game-designer, level-designer |
| R6 | **Secrets** | Reward exploration | Breakable walls (`Wall_Broken` / `Wall_Hole` kit) hiding a small room with bonus evidence; opened by thrown items at high speed or Coso. 0–2 per run. | M | none | level-designer, 3d-artist |

## 7. Layer (d) — Comedic interactions with the flaws

| Flaw | Map reaction | Notes |
|---|---|---|
| **Coso** breaks things | Every `ObjectBroken` adds +0.15 tension; breaking a `FallingProp`-holding shelf drops everything on it; 10 % chance a break "wakes the building" — a nearby door slams (E3) and torches in the room gutter (E1). | Plan §3.4 events |
| **Lumi** frozen by loud sounds | Director prefers loud events (slam, falling pot, bell) **near Lumi** at 2× weight in Build-up. In **Blackout** runs the map is her stage. | Plan §2.5 |
| **Trotín** can't stop | Flying into a `FallingProp`/door knocks it over (noise); flooded sections make him skid further (drag ×0.5). | `ISlideTarget` hook from plan §1.4 |
| **Mochín** sleeps | Rats (E6) gather around a sleeping Mochín and nibble → wake chance 30 %/10 s; snores feed the director as noise. | |
| **Bobi** stuck in doors | Doors (S8) narrower variants (`Doors_*_Covered`) are `NarrowPassage`; wind gust can push a door onto a stuck Bobi (slam). | |
| **Pompón** false alarms | Distant sound events (E4) are routed through his wall-listen as "threat-like" 30 % of the time. | |
| **Fotchi** auto-photo | Rare events and set-piece triggers raise `CreatureSpotted`-style drama events → auto-flash; flash relights `Out` torches in 5 m for 3 s (light burst). | |
| **Sabiucho** cries | Set pieces with traps (collapsing floor, chandelier) carry a `PreWarning` zone; ignoring it → crying → +tension. | |

## 8. Performance rules (Forward+)

- **Lights:** keep `DynamicLightBudget` as the single authority (nearest 4 shadowed, culled past 45 m). Every new light (landmarks, set pieces) is **unshadowed by default**, range ≤ 10. Blackout/dying torches lower the count. Budget target: ≤ 24 visible additional lights per view.
- **Audio:** ≤ 6 director one-shots + 2 beds + creatures; all pooled via `SoundManager`.
- **Props:** kit meshes static-batched (generated content is already `MarkStatic`); dynamic props (`FallingProp`, doors) not static; rats instanced.
- **Director/events:** 2 Hz tick, no `Find*` per frame (registries on enable), no per-frame GC.
- **Fog** is the cheapest atmosphere tool — use it per biome instead of more lights.
- optimizer agent profiles after each milestone (target 60 fps on the generated 4×4 map).

## 9. Data assets (new ScriptableObjects)

`RoomArchetype`, `SetPieceDefinition`, `PropPalette`, `BiomeProfile`, `DirectorProfile`,
`AmbientEventDefinition`, `RunModifier`, `EvidenceTable`, `PlacementProfile`, `RareEventDefinition`,
`StoryVignette`. All live in `Assets/_Game/Environment/Data/` (or `Mission/Data/` for run-level ones).
No `Resources.Load`, no hardcoded paths — assigned in the Inspector like `DungeonGenerator.PropGroup`.
Generator invariants stay: `_floor` sacred, carve-before-prune, all layout randomness through `_rng`
(director/events use gameplay `Random`, never the seeded RNG).

## 10. Asset needs by agent

| Agent | Needs |
|---|---|
| **3d-artist** | Rat (≤150 tris) ; vignette props (dead explorer skeleton pose, dog bowl, tally-mark wall decal, bone pile) ; door prefabs with hinge pivots from kit FBX ; stairs/ramp fitting the 1-tile grid & 3 m walls ; 2–3 biome material sets ; landmark statue variants ; water plane material (pixel-friendly) ; ripple + moving-shadow flipbooks |
| **sound-designer** (`SFX/Environment/`) | Torch gutter/poof/relight, wind gust ×3, door creak ×4 / slam ×3, distant chains/bell/footsteps/toy ×8, drips ×6, rat squeaks ×4 + scurry loop, pot smash ×3, book thud ×3, wall crumble ×2, 5 biome beds (−30 LUFS, Streaming/Vorbis), tension sting + relief sting (dark8bit). A/B round first. |
| **level-designer** | Archetypes, set-piece system + 6 rooms, kit palettes, loops/dead ends, landmarks, biome zones, doors placement, evidence table placement, flood section |
| **animator** | Rat run/idle; door swing is procedural (none) |
| **ui-designer** | Run card (place name + mutator + seed), objective variants on HUD |
| **main session** | Director, events framework, modifiers, `Door`, `TorchStateController`, `FallingProp`, `RunSetup` |

## 11. Recommended order

### Milestone 1 — "Vertical slice: it breathes" (highest impact / lowest cost)
1. **E1 Torches gutter & die + director-driven moods** (`AmbientDirector` minimal: tension + Calm/Build-up/Spike → `LightingMoodController.SetMood`, which nobody calls today). — S/M
2. **S3 Kit props replace primitives** (prop palettes per theme). — M, biggest visual win
3. **S5 Spanning tree + loops + dead ends** (kill the lattice). — S
4. **S6 One landmark per section** (statues, dead tree, bell). — S
5. **E4 + E5 + E9 Soundscape**: distant sounds, drips, one bed per biome. — S (mostly audio work)
6. **R1 Run modifiers ×3**: Blackout, Fog, Extra creature + run card. — S each
7. **R4 Placement variety** (start/extraction distance band, 3 extraction candidates). — S

Exit test: two consecutive seeds are told apart by a new player within 30 s; a 10-min session has ≥ 3 noticeable director moments; frame time unchanged ±1 ms.

#### Milestone 1 — status (implemented 2026-11-23, level-designer)

| # | Item | Done | Where |
|---|---|---|---|
| 1 | E1 torches gutter / die / relight + director | `TorchStateController` on the Torch prefab (Lit/Guttering/Out; Out deactivates flame+light so `DynamicLightBudget` drops it; E with a lit carried torch relights). `TensionDirector` (Calm→Build-up→Spike→Relief, tension T, per-phase event budget, moods via `LightingMoodController.SetMood`), data in `DirectorProfile` + `AmbientEventDefinition` SOs. Creature hook `ICreatureTensionSource` (PatrolEnemy already reports Chase → Spike; the director asks a registered creature for its own bell first, otherwise fakes it). | `Environment/LivingMap/`, `Environment/Data/Director/` |
| 2 | S3 kit dressing | Per-theme `PropPalette` SOs (Floor / AgainstWall / Wall / Ceiling mounts) + kit swaps for pillars, dead trees, windows, cell bars, wall panels, bone piles, candles. One BoxCollider per solid prop, wall props shadowless, everything static-batched. Gravestones/crosses/tombs/rocks/stalagmites/crystals stay primitive (no kit equivalent). | `MultiSectionDungeonGenerator.LivingMap.cs`, `Environment/Data/Palettes/` |
| 3 | S5 routes + loops + dead ends | Kruskal spanning tree of sections + `ExtraLoopChance` 0.3 (min 2, max 4 loops) + 2–3 single-entrance dead-end rooms; connectivity validated after the prune (repair + log). Navigation check: 2×2-tile flood fill from the start removes props that cut off floor/objectives. | same |
| 4 | S6 landmarks | 11 kit-built landmark prefabs (Bell, Fox/Stag/Horse statues, Hanging Tree, Bonfire, Shrine, Spike Pit, Wagon, Well, Broken Arch), one per section, least-used first, clearest spot near the centre, own `ShadowlessLight` in the theme colour. Names go to `DungeonLayout`. | `Environment/Landmarks/` |
| 5 | E4/E5/E9 soundscape | 5 area beds (one per theme, crossfaded, ≤ 2 sources), drips from generator-placed points, distant sounds + fake bell as director events, torch gutter/out/relight, spike sting. Stable Audio → `relaxed` + role loudness. | `SoundscapeController`, `Audio/SFX/Ambient/` |
| 6 | R1 modifiers ×3 + run card | `RunModifier` SOs: Blackout (≈50 % torches out, ambient ×0.45, tension floor), Fog (fog ×2.5 + mist tint, creature sight ×0.7), Extra creature (`EnemySpawner.EnemyCount` +1). `RunSetup` picks one from the hashed seed and shows "The <Adjective> <Place> — modifier — seed" via `MissionMessageUI.Show`. | `Mission/RunModifiers/`, `Mission/Data/RunModifiers/` |
| 7 | R4 placement | Extraction among 3 candidate sections (Graveyard weighted ×2, any theme possible); start in a themed area at 35–60 % of the farthest walking distance from the extraction; 1 evidence prefers a dead-end room. | generator |

Verified in Play mode on seeds **30 (Extra creature), 7 (Fog), 12 (Blackout)**: all 16 sections connected,
generators / evidence / extraction / 16 landmarks reachable on the baked NavMesh, ≈ 99 % of floor-level
NavMesh reachable from the start (the rest are pockets inside prop clusters), director cycles
Calm 36 s → Build-up 44 s → Spike 13 s → Relief 34 s (≈ 2 min cycle), PatrolEnemy chase forces Spike.
Rebuild everything with **Fluffterror › Living Map › Build Assets + Wire Scene**, then Generate.

### Milestone 2 — "It reacts"
S8 Doors + E3 creak/slam · E2 wind gusts · E8 falling props · E6 rats · S7 biome profiles · R3 evidence table · flaw hooks for Coso/Lumi/Trotín (§7, once Phase 2 event bus exists).

### Milestone 3 — "It surprises"
S1 room archetypes · S2 set-piece system + 6 rooms (incl. Chucho's Den) · E10 story vignettes · R2 objective variants · R6 secrets · more modifiers (Flooded, Cursed evidence, Rat plague).

### Milestone 4 — "It's deep"
S4 verticality · R5 rare events · E7 moving shadows · remaining flaw interactions (Phase 3 characters).

## 12. Decisions (resolved 2026-11-23)

| # | Question | Decision |
|---|---|---|
| 1 | Kit vs. primitives | **Kits** (primitives stay as fallback) |
| 2 | Map size | **Keep 4×4** for now; revisit after playtests |
| 3 | Mutators | **Always one per run** |
| 4 | Director fakes creature cues | **Yes** — false bell jingles / distant pants in Build-up, max 1 per 90 s, never within 10 m of a real Chucho |
| 5 | Objectives | **Keep 3 generators**; Ritual / Key objectives in a later milestone (M3) |
| 6 | Phase scope | **Milestone 1 moved up** to run alongside Phase 1.5/1.6 (`PLANIFICACION.md` §1.7) |

Related: Chucho's Den (Creature_01.md) is the first set piece (S2) and now also stores offered limbs (Creature_01.md §15) — its bone pile must leave floor space for limb props to be found.
