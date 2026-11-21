---
name: generator-machinery
description: The repairable "generator" machines — mission objective, where the code lives, how hold-interaction works
metadata:
  type: project
---

Resuming feature (started 2026-06-19): repairable **generator machines** scattered in the dungeon.

**Design intent:** 3 generators spawn across the map, each in a different section/room. Each has states **Broken → Repaired → Running**. Hold E to fill a repair charge (charge drains if you stop); once repaired, press E to start it. The eventual goal is "all 3 repaired/running" as a mission objective (win-condition wiring is NOT built yet — `PowerGenerator.All` / `RepairedCount` / `AnyStateChanged` exist as the hook for it).

**Where it lives:**
- `Assets/_Game/Environment/PowerGenerator.cs` — the machine (class `PowerGenerator`, named that to avoid clashing with the dungeon "generator"). Self-contained: charge/drain, run timer, status-light visuals via instanced emissive material on a child Renderer named "Indicator".
- Spawning is in [[active-dungeon-generator]] (`MultiSectionDungeonGenerator.cs`): each generator gets its OWN dedicated small room. `CarveGeneratorRooms()` (runs after `ConnectSections()`, before `PruneFloorIslands()`) finds empty rectangles via a floor prefix-sum, carves a room + a connecting corridor to the nearest section (so it survives the island prune), and the room tiles stay `Transition` (clean walled+ceilinged chamber, no theme décor). `SpawnGenerators()` then drops one machine at each room centre. Inspector: `GeneratorCount`, `GeneratorPrefab`, `GeneratorClearance`, `GeneratorRoomSize`, `GeneratorRoomGap`, `GeneratorRoomSpacing`.

**Hold-interaction pattern (reusable):** added `Assets/_Game/Core/IHoldInteractable.cs` (extends `IInteractable`). `InteractionSystem` now subscribes to `InteractCanceledEvent` too; on press it asks `UseHold(interactor)` — true routes to `OnHoldStart/OnHoldTick/OnHoldStop` while the player keeps aiming + holding E, false falls back to the normal `Interact()` press. Use this for any future hold-to-charge interaction.

**Animation layer (added 2026-06-21):** the engine now has a code-generated `engineAnimatorController`. The user supplies the generator + handle models themselves; the code side: `PowerGenerator` drives an auto-found `Animator` via two bools in `EngineAnimParams` (bottom of `PowerGenerator.cs`) — `Repairing` (handle cranks while hold-charging) and `Running` (handle spins steadily). Build the controller from `Assets/Editor/EngineAnimatorBuilder.cs` (menu **Fluffterror > Animation > Build Engine Animator**): Idle→Crank→Run via AnyState transitions, spinning a child named `Handle` (must be an **identity-rotation pivot**) around `EngineAnimParams.SpinProperty` (default z). Also added a **look-at border** (`IHighlightable` + `InteractableOutline.cs/.shader`, toggled by `InteractionSystem`) and a **gothic on-screen prompt** (`UI/InteractionPromptUI.cs`, reads `InteractionSystem.CurrentPrompt`, needs a gothic `Font` assigned). Full details in [[active-dungeon-generator]] CONTEXTMAP §7.

**Skill-check minigame (added 2026-06-21, DBD-style):** repairing now pops Dead-by-Daylight skill checks. `Assets/_Game/UI/SkillCheckController.cs` (singleton `Instance`, IMGUI circular gauge: needle sweeps clockwise from top, random white success arc with a thin **red "great"** slice; also draws the repair progress bar). `PowerGenerator.DriveSkillChecks` (in `OnHoldTick`) rolls checks (`_skillCheckChancePerSecond`/`_skillCheckMinGap`) and `OnSkillCheckResult` applies Great=+bonus, Good=neutral, Miss=−penalty charge. Hit key is the **Jump/Space binding**; `CharacterMovement.OnJump` early-returns while `SkillCheckController.Active` so you don't jump. Release E → `Cancel()` (no penalty) + existing `_chargeDrain` regresses the saved progress. **Setup:** add `SkillCheckController` to a HUD object + assign the player's `InputReader`. Details in [[active-dungeon-generator]] CONTEXTMAP §7.

**Why:** This is the active thread the user wants to keep building (next likely: win condition, SFX/VFX on repair+run, finishing the handle/model art).
