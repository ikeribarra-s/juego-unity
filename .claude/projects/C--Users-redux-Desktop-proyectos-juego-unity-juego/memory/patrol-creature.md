---
name: patrol-creature
description: The patrolling enemy that hunts/kills the player — Phase 1.5 creature, NavMesh AI, where it lives
metadata:
  type: project
---

Phase 1.5 "Creature" (started 2026-06-21): a simple patrolling AI that spawns randomly and kills the player.

**Where it lives:** `Assets/_Game/Enemies/`
- `PatrolEnemy.cs` — `NavMeshAgent` FSM. **Patrol** (wander to random `NavMesh.SamplePosition` points) ↔ **Chase** (sees a living player within `_sightRange`, inside `_fieldOfView`, clear raycast past `_sightBlockers` = the `Walls` layer). Kills via `CharacterBase.Vitals.Kill()` within `_killRange`; keeps chasing `_loseSightTime` after losing LOS.
- `EnemySpawner.cs` — bakes a NavMesh **at runtime** over the procedural dungeon (`NavMeshSurface`, `PhysicsColliders`, auto-created), then spawns at random NavMesh points ≥ `_minDistanceFromPlayer` from the player. Builds a primitive capsule+glowing-eyes stand-in if no `_enemyPrefab`. Public `SpawnWave()` for runtime regen.

**Why NavMesh works here:** `com.unity.ai.navigation` 2.0.11 is installed and the dungeon pieces have mesh colliders, so a runtime `NavMeshSurface.BuildNavMesh()` carves walkable floors around the wall colliders. Corridors (CorridorWidth 3) are wide enough for the default agent.

**Death path (new, was none):** `Core/IKillable.cs` (interface, reusable for traps too) → `Characters/_Base/CharacterVitals.cs` (auto-added by `CharacterBase.Awake` like PhysicsGrabber; exposed as `CharacterBase.Vitals`). `Kill()` disables CharacterMovement + InteractionSystem (movement's OnDisable re-locks/unlocks cursor) and, single-player, calls `MissionManager.TriggerLoss()` (added). `Died` event is the hook for a future death screen / co-op downed state.

**Setup:** add `EnemySpawner` to a scene manager object. Optional: assign a creature prefab + tune sight/kill ranges. No InputReader needed (the creature isn't player-driven).

**Likely next:** creature SFX (growl/chase stinger via SoundManager), an Animator for walk/lunge, a death screen UI on `CharacterVitals.Died`, jump-scare hook into `HorrorPostFX.PulseGrain`.
