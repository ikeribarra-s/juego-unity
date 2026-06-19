 1. Chunked, combined dungeon meshes (DungeonGenerator.cs)
  Right now every floor run and wall run is its own GameObject with a MeshFilter + MeshRenderer + MeshCollider — an 80×80 map already produces hundreds of objects, and Phase 6 maps will be bigger. The scalable
  shape is: divide the grid into chunks (e.g. 16×16 tiles), build one combined mesh per chunk per material, one MeshCollider per chunk. That cuts draw-call overhead, transform overhead, and physics broadphase
  entries by ~50×, and chunks give you free distance-based activation later. Two things to fix while you're in there:

  - Generated walls aren't on the Walls layer, so EnvironmentReverb's raycasts ignore the entire dungeon. Set go.layer during generation.
  - BuildQuadMesh/BuildBoxMesh meshes are never destroyed on Clear() — in the editor those leak until domain reload. Track and destroy them.

  2. A player registry instead of scene searches
  EvidenceGlow does FindAnyObjectByType<CharacterBase>() and assumes one player; MissionManager uses a serialized _playerCount. Both break at 8 players and both will be rewritten in Phase 4 anyway. Create a
  tiny static PlayerRegistry (players add/remove themselves in OnEnable/OnDisable) now — EvidenceGlow, the Phase 1.5 creature AI, Bobi/Pompón detection abilities, and ExtractionZone all need "iterate players /
  find nearest player," and retrofitting it later touches everything at once.

  - Generated walls aren't on the Walls layer, so EnvironmentReverb's raycasts ignore the entire dungeon. Set go.layer during generation.
  - BuildQuadMesh/BuildBoxMesh meshes are never destroyed on Clear() — in the editor those leak until domain reload. Track and destroy them.

  2. A player registry instead of scene searches
  EvidenceGlow does FindAnyObjectByType<CharacterBase>() and assumes one player; MissionManager uses a serialized _playerCount. Both break at 8 players and both will be rewritten in Phase 4 anyway. Create a
  tiny static PlayerRegistry (players add/remove themselves in OnEnable/OnDisable) now — EvidenceGlow, the Phase 1.5 creature AI, Bobi/Pompón detection abilities, and ExtractionZone all need "iterate players /
  find nearest player," and retrofitting it later touches everything at once.

  3. Layers + collision matrix + tightened masks
  You have one layer (Walls). Define Players, Evidence, Creature now and prune the physics collision matrix. This is the cheapest physics optimization that exists, and it also fixes correctness:
  InteractionSystem._interactLayer = ~0 and PhysicsGrabber's hold-point raycast (~0) currently hit everything, including triggers and other players. Masked queries get cheaper and more correct as scenes grow.

  4. Local-vs-replicated component split (pre-Netcode)
  In multiplayer, only the local player should run InteractionSystem's per-frame raycast, CameraEffects, DebugHUD, the camera, and the AudioListener. Decide now which components are "local player only" and
  document it in CLAUDE.md — Phase 4 becomes "disable this list on remote players" instead of surgery. Related: your seed-based DungeonGenerator is already the right multiplayer pattern — sync the seed,
  generate locally on each client. Keep generation strictly deterministic from _rng (never use UnityEngine.Random or order-dependent iteration in it).

  Medium leverage: adopt as content grows

  5. Centralized ticking for ambient effects
  EvidenceGlow and LightFlicker each run their own Update() with per-frame noise/distance math. At dozens of instances that's fine; at hundreds (Phase 6 maps full of flickering lights) the per-MonoBehaviour
  Update overhead dominates. The scalable pattern is a single manager that iterates a list — or better, Unity's CullingGroup API, which gives you distance-band callbacks ("player got within 5 m of this
  evidence") with no per-object Update at all. Glow and flicker can also tick at 10–15 Hz and lerp; nobody can tell.

  6. Realtime light budget
  Procedural dungeons can't use baked lightmaps or baked occlusion culling, so your scaling lever is light discipline: Forward+ (you have it) handles many lights, but shadow-casting point lights are the real
  cost. Establish a rule now — e.g. shadows only on the player's flashlight and 1–2 key lights, everything else shadowless with tight ranges — and have LightFlicker disable lights beyond ~25 m from any player
  (it already owns the light list).

  7. Non-allocating physics queries for abilities
  Bobi's smell, Pompón's wall-hearing, and the creature's senses will all be OverlapSphere-shaped. Standardize on Physics.OverlapSphereNonAlloc / RaycastNonAlloc with cached buffers from day one so detection
  abilities don't generate GC garbage every tick — GC spikes are the classic cause of horror-game hitches.

  8. Replace IMGUI HUDs before they're load-bearing
  OnGUI (DebugHUD, InventoryHUD) allocates every frame and runs multiple times per frame. Fine as debug tools — just don't build the Phase 1.6 timer/score UI on it. Use UI Toolkit or uGUI with change-driven
  updates (only touch text when a value changes).

  Lower priority, worth knowing

  - Addressables for Phase 6 maps/characters/audio so content ships and loads incrementally instead of bloating one build.
  - LODs + GPU instancing on the furniture-pack props once rooms get decorated.
  - NavMesh: for Phase 1.5, bake at runtime with NavMeshSurface after generation, per-chunk if maps get huge.
  - Audio: footstep/effect culling by distance for remote players, and AudioMixer voice limits, once 8 players × footsteps × creature sounds stack up.