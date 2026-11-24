# Creature 01 — "Chucho" (the Bone Hound)

> Status: **design spec, not implemented.** Replaces the capsule stand-in (`PatrolEnemy` + primitive in
> `EnemySpawner`). Plan phase: **1.5 — Creature (Basic)**, with stretch items flagged for later phases.
> Owner of this doc: game-designer. Implementers per section are named in §13.

---

## 0. Concepts considered

| | Concept | Hook | Why not / why |
|---|---|---|---|
| A | **El Sombrerón** — a 3 m lanky man under an enormous floppy hat (plan §6.2 "Sombrero") | The hat brim hides his face; he ducks comically under every arch | Humanoid = more anims, hard to make funny *and* scary at 4k tris; the joke has no link to our skeleton player. Keep for Phase 6. |
| B | **La Sábana** — a ghost made of a moving pile of wet bedsheets | Silent drift, snags on furniture | No legs = cheap anim, but no sound tells and weak silhouette after pixelation; "invisible" is already Phase 6 *El Invisible*. |
| C | **Chucho** — a huge, mangy, starving stray hound that wants **your bones** | The player is a skeleton. A dog. *Obviously.* | **Picked.** |

**Why Chucho**
1. **Instant joke with the protagonist.** Skeleton + dog needs zero explanation; being "caught" = being slobbered on and having a bone (or your evidence) carried off. Horror (big, fast, starving) and comedy (it's a dog: it fetches, it points, it's scared of fire, it gets distracted) in one silhouette.
2. **The plan's catch rule fits naturally:** bite → you collapse into a heap of bones (stun) → it runs off with one item. Non-lethal, story-generating.
3. **Readable counterplay everyone already knows from real dogs:** throw something (fetch), wave fire, freeze (it sees movement), listen for the collar bell.
4. **Cheap to build:** quadruped, no fingers/face rig, ≤ 4k tris, ~12 clips, NavMesh-friendly.
5. **Feeds the flaws:** Mochín's spilled items (plan §3.5 already says "a creature picks one up and runs"), Lumi's freeze becomes an accidental defence, Trotín becomes the perfect (involuntary) bait.

---

## 1. Pitch

*"A starving giant stray that hunts by sound and movement, knocks you into a pile of bones and runs off to bury your evidence — the story afterwards is 'Trotín threw the cursed skull to distract it and Chucho buried it in the crypt'."*

## 2. Player experience

- **Calm:** far-off collar bell *tink… tink…* and a wet pant echoing down a corridor. You know roughly where it is; you don't know where it's going.
- **Build-up:** the bell stops. Sniffing. It heard your sprint. You crouch, stop moving and pray.
- **Spike:** it rounds a corner, sees you move, **freezes into a pointer pose** (0.8 s, growl) — your one-beat warning — then barks and gallops. The bell goes frantic.
- **Resolution:** you either break line of sight, wave your torch to hold it off for a few seconds, throw something to make it fetch… or you get pounced, your screen shakes, you collapse into a pile of bones and watch it trot away **with the evidence you were carrying**, tail wagging. Now someone has to go to its den to get it back.

---

## 3. Silhouette, size, readability

| Property | Value | Note |
|---|---|---|
| Body | Emaciated hound, too-long spine, huge paws, ribs, ragged fur tufts, long tongue, one ear up / one flopped | Reads as "dog" at 30 px tall after pixelation |
| Shoulder height | **1.5 m** (head ~2.0 m when alert) | Player model is 2.39 m: Chucho is lower but **3.0 m long** — big mass, not tall |
| Rear-up height (catch / bark) | ~3.0 m | Looms over the player |
| Collar | Comically tiny collar with a big brass **bell** and a tag that says **CHUCHO** | Bell = primary sound tell; tag catches torchlight |
| Eyes | Two **emissive sick-red** (`#B3202A`, ArtDirection danger) pinpoints | Visible at 20+ m in total darkness; **brighter in Chase** (×2 emission) |
| Mouth | Faint emissive drool (dim red-pink) | Only during Chase/Point |
| Real lights | **None by default.** Optional shadowless red point light (range 3, intensity 0.6) only in Chase, flag `_chaseGlowLight` | Forward+ budget; it must be excluded from `DynamicLightBudget` shadow picks (no shadows ever) |
| Material | `Fluffterror/ToonOutline` (same as the skeleton), one 512² texture, Point filter, no mips | Matches player |

**Sound tells (non-negotiable for fairness):** bell jingle tied to footfalls (rate ∝ speed), panting loop (idle/patrol), sniff bursts (investigate), growl (point), bark (chase start), yelp (fire). Silence of the bell = it stopped = danger.

---

## 4. Rules

### 4.1 State machine

```
            noise heard                     sees moving player
 PATROL ───────────────► INVESTIGATE ─────────────────────┐
   ▲  ▲                      │ nothing found (search end)  ▼
   │  └──────────────────────┘                         POINT (0.8 s telegraph)
   │                                                        │
   │  lost sight > _loseSightTime        SEARCH ◄──────── CHASE ──── within _catchRange ──► CATCH
   │◄────────────────────────────────── (sniff last pos)    │ ▲                               │
   │                                                        │ └── bravery expires ── HELD_AT_BAY (torch)
   │                                      thrown item lands │
   │                                      near / in view    ▼
   │◄── item dropped at den ─── CARRY ◄──────────── FETCH (run to item, pick up)
   │                              ▲                                            │
   │                              └──────────── stole an item on CATCH ◄───────┘
   │◄── flee timer ends ──── FLINCH/FLEE ◄── torch thrown / photo flash / hit by thrown object
```

| State | Behaviour | Exits |
|---|---|---|
| **Patrol** | Wander NavMesh points (`_patrolPointRadius`). Every `_huntBiasInterval` the next point is chosen near a random living player's position ± `_huntBiasJitter` (keeps pressure on a huge map without cheating LOS). Plays idle variety (sniff ground, scratch, sit) at points. | Noise ≥ hearing threshold → Investigate. Sees moving player → Point. Thrown item → Fetch. |
| **Investigate** | Trot to the noise position (loudest recent wins). Sniff loop for `_investigateSearchTime` at arrival, looking around. | Sees player → Point. Timeout → Patrol. New louder noise → re-target. |
| **Point** | Stops dead, pointer pose toward the player, growl. **Can't move or catch** for `_pointTime`. Raises `CreatureAggro` (Sabiucho hook). | → Chase (always). If the target is no longer visible at the end → Search. |
| **Chase** | Bark, gallop at `_chaseSpeed` toward the target. Within `_lungeRange` with clear LOS: **lunge** (`_lungeSpeed` for `_lungeDuration`, then `_lungeRecovery` slow). | Within `_catchRange` → Catch. LOS lost `_loseSightTime` → Search. Torch-holder faces it within `_torchKeepAwayRadius` → Held At Bay. Fetch trigger (chance) → Fetch. Flinch trigger → Flinch. |
| **Held At Bay** | Circles the torch holder at `_torchKeepAwayRadius`, barking/growling (loud → attracts nothing extra but freezes Lumi). Bravery timer `_torchBravery` counts down (only while it's being faced). | Bravery 0 → lunge (Chase). Holder turns away / drops torch → Chase. |
| **Catch** | Pounce + bite-shake animation (`_catchAnimTime`). Victim: screen shake + **stun** + drop held evidence & torch. Chucho grabs **one** item (priority: evidence the victim held > torch > nothing). Victim gets `_catchGraceTime` immunity. | Has item → Carry. Else → Patrol (`_postCatchCooldown` before it can re-aggro anyone). |
| **Fetch** | A thrown item (evidence, torch excluded — see Flinch) landing within `_fetchHearRange` or in view: run to it, pick it up. | Picked up → Carry. Item taken by a player first → Patrol/Chase. |
| **Carry** | **Not hostile** (mouth full). Trots (`_carrySpeed`) to its **Den** and drops the item there, tail wag. | Dropped → Patrol. Hit by a thrown object / scared → drops the item where it stands → Flinch. |
| **Search** | Go to last-seen position, sniff `_searchTime`. | Sees player → Point (shortened to `_pointTimeReacquire`). Timeout → Patrol ("lose interest" huff). |
| **Flinch/Flee** | Yelp, run away from the source for `_fleeTime`. Ignores players. | Timer → Patrol. |
| **Accept** | A player offered a bone (§15): skid, sit, take the limb. | → Chewing. |
| **Chewing** | Carries the limb to the den and chews `_chewDuration[n]` s; ignores everyone. | Timer → Patrol (offerer = friend 20 s). Hit by thrown item / limb taken → Chase. |

### 4.2 Senses

**Sight** — cone + raycast past `Walls` (current `PatrolEnemy.CanSee` logic, kept):
`effectiveRange = _sightRange × torchMul × stillMul × crouchMul`

| Modifier | Default | Meaning |
|---|---|---|
| `_sightRange` | 14 m | Base, target in the dark |
| `_torchLitMultiplier` | 1.6 | Target carries a lit torch → seen from 22 m (**torch = shield and beacon**) |
| `_stillMultiplier` | 0.4 | Target speed < `_stillSpeed` (0.4 m/s) → it barely sees you (**freeze!**) |
| `_crouchMultiplier` | 0.7 | Target crouching |
| `_fieldOfView` | 160° | |
| `_closeAwarenessRadius` | 2.5 m | 360° "smells you" radius, ignores still/FOV (no hiding at its feet) |

**Hearing** — listens to the noise bus (§6). A noise of loudness `L ∈ [0,1]` at distance `d` is heard if `d ≤ L × _hearingRange` (`_hearingRange` = 30 m) **and** the path isn't absurd (`NavMesh.CalculatePath` length ≤ `_hearingPathFactor` × `L × _hearingRange`, factor 1.5 — sounds don't go through 3 walls).

| Noise source | Loudness | Event |
|---|---|---|
| Walk footstep | 0.10 (3 m) | `CharacterAudio` step |
| Sprint footstep | 0.35 (10.5 m) | `CharacterAudio` step |
| Crouch footstep | 0 | — |
| Jump landing | 0.30 | `CharacterAudio` land |
| Thrown/dropped item impact | 0.15 + 0.08 × impact speed, max 0.7 | `EvidencePickup.OnCollisionEnter` |
| Evidence breaks | 0.9 | `EvidencePickup.Break` |
| Torch lands after throw | 0.4 | `Torch` collision |
| Generator repairing (pulse every 2 s) | 0.25 | `PowerGenerator` |
| Skill-check **miss** (machine bang) | 0.6 | `PowerGenerator.OnSkillCheckResult` |
| Generator starts | 0.8 | `PowerGenerator.StartRunning` |
| Extraction unlocks ("dinner bell") | 1.0, **map-wide** | `MissionManager.GeneratorsCompleted` → every Chucho Investigates the extraction zone |
| Chucho bark (other Chuchos) | 0.8 | Pack alert propagation |
| Mochín snore / Sabiucho cry / Coso break / Fotchi flash (later phases) | 0.7 / 0.5 / 1.0 / 0.9 | plan §3 events |

**Light reactions**
- **Held torch (faced):** keep-away + bravery timer (Held At Bay). Torch is not immunity, it buys `_torchBravery` seconds.
- **Thrown torch** landing within `_torchScareRadius` of Chucho, or hitting it: Flinch/Flee. A torch is the one thing it never fetches.
- **Wall torches:** no effect (otherwise the map is a safe zone). Phase 6 option: it avoids standing *under* lit wall torches when patrolling.

**Target choice** (when several players are visible): score = `1/distance × (1 + _movingBias × speedNormalized)` (`_movingBias` = 1.5) — **dogs chase whoever runs**. Re-evaluated only on entering Chase (no target flip-flop mid-chase).

### 4.3 Catch rule — recommendation

| Option | Pros | Cons |
|---|---|---|
| Current: instant kill | Simple, scary | In 1–8 co-op it removes players (spectating = no flaw chains); a single mistake ends a solo run; no story |
| Plan: screen shake + 5 s stun + drop one item | Story-generating, keeps everyone playing | No real stakes alone |

**Recommended: plan rule + theft + strike limit.**
- Catch = shake + **5 s stun** ("bone pile") + drops held evidence/torch; **Chucho steals one item** and buries it in its Den (recoverable).
- **`_lethalAfterCatches` = 3** per player per run (0 = never lethal). The 3rd catch calls the existing `IKillable.Kill` (→ `MissionManager.TriggerLoss` in single-player, "downed" in co-op Phase 4). HUD shows strikes as cracked bones (ui-designer, later).
- `_catchGraceTime` = 6 s immunity after a catch (no stun-lock).

### 4.4 Tunables (defaults) — all on `CreatureDefinition` SO

| Group | Field | Default |
|---|---|---|
| Speed | `_patrolSpeed` / `_investigateSpeed` / `_chaseSpeed` / `_carrySpeed` / `_fleeSpeed` | 2.2 / 3.8 / **7.5** / 4.0 / 8.0 m/s |
| Speed | `_angularSpeed` / `_acceleration` (agent) | 360 °/s / 14 |
| Lunge | `_lungeRange` / `_lungeSpeed` / `_lungeDuration` / `_lungeRecovery` / `_lungeCooldown` | 4.5 m / 13 m/s / 0.35 s / 0.8 s / 2.5 s |
| Patrol | `_patrolPointRadius` / `_idleAtPoint` / `_huntBiasInterval` / `_huntBiasJitter` | 18 m / 1.5–4 s / 40 s / 15 m |
| Senses | see §4.2 | |
| Timers | `_pointTime` / `_pointTimeReacquire` / `_loseSightTime` / `_searchTime` / `_investigateSearchTime` | **0.8** / 0.35 / 6 / 5 / 4 s |
| Catch | `_catchRange` / `_stunDuration` / `_catchGraceTime` / `_postCatchCooldown` / `_lethalAfterCatches` | 1.6 m / **5 s** / 6 s / 4 s / 3 |
| Shake | `_shakeAmplitude` / `_shakeDuration` | 0.12 m / 0.5 s |
| Torch | `_torchKeepAwayRadius` / `_torchBravery` / `_torchScareRadius` / `_fleeTime` | 3.5 m / 6 s / 4 m / 4 s |
| Fetch | `_fetchHearRange` / `_fetchChanceInPatrol` / `_fetchChanceInChase` | 12 m / 1.0 / **0.5** |
| Pack | `_barkAlertRadius` | 18 m |

Reference: player walk 5, sprint 9 m/s (`CharacterStats`). Chase 7.5 < sprint, so a sprinting player escapes in the open but **lunge + corners + noise** catch them; walkers are caught. Trotín's 15 m/s sprint outruns it — until he crashes.

---

## 5. Counterplay (what players learn)

1. **Listen for the bell.** Silence = it's sniffing near you.
2. **Freeze.** Standing still cuts its sight to 40 %. (Lumi does this involuntarily.)
3. **Crouch-walk.** Silent steps.
4. **Fetch.** Throw something — but it's your evidence that ends up in the Den (or breaks).
5. **Fire.** Face it with a torch for 6 s of safety, or throw the torch at it to make it flee — then you're in the dark.
6. **Break LOS** around corners; it searches the *last seen* spot.
7. **Rob the Den.** Stolen items are recoverable; Chucho returns to patrol after dropping them.

---

## 6. Flaw interactions

| Character | Interaction | Story it creates |
|---|---|---|
| **Trotín** (can't stop) | Highest speed → **preferred chase target** (moving bias). Outruns it, but his Flying crash is a 0.7 noise and he can't stop to hide. Perfect involuntary bait. | "Trotín dragged it across the whole map, then slid into the generator room where we were repairing." |
| **Lumi** (frozen by loud sounds) | Chucho's bark (0.8) freezes Lumi 3 s… and a frozen Lumi is *still* → 0.4 sight. Her flaw saves her — unless she's within the 2.5 m smell radius. Night vision sees its eyes/den across dark rooms. | "It barked, Lumi froze, it walked right past her." |
| **Mochín** (sleeps, spills items) | Snore = 0.7 noise every 3 s → Chucho comes, **fetches the spilled items one by one** to the Den (plan §3.5 rule made concrete). It never attacks a sleeping player (`IsAsleep` → skip as target); it sniffs and steals. | "We came back and Mochín was asleep with Chucho stealing his stuff." |
| **Coso** (breaks things) | Break = 1.0 noise; 30 % clumsy breaks while sneaking are a dinner bell. Stretch: Coso can break the Den's "bone pile" open (bonus recovered item). | |
| **Fotchi** (auto-photo on drama) | Point/Chase is a `CreatureSpotted` drama event → auto-flash: the flash (0.9 noise, light burst) makes Chucho **Flinch** 2 s if within 6 m — accidental save — and a photo of a pouncing Chucho is ×2 evidence. | |
| **Sabiucho** (cries if ignored) | Point state raises `CreatureAggro` 0.8 s before Chase — the natural pre-warning. Ignored → crying = 0.5 noise loop → Chucho returns. | |
| **Bobi** (smell) | Smell ping reveals Chucho and the Den. Comedy: if Bobi is caught, 50 % chance Chucho sniffs him and loses interest (`_dogFriendChance`) — "professional courtesy". | |
| **Pompón** (hears through walls) | Bell, pant and growl are `SoundSource`s — Pompón tracks it through walls and false-alarms on drips. | |

---

## 7. Multiplayer notes (Phase 4)

- **Server-authoritative AI.** `CreatureBase` becomes a `NetworkBehaviour`; only the server runs senses/state machine/NavMeshAgent. Clients get `NetworkTransform` (interpolated) + `NetworkVariable<CreatureState>` + `NetworkVariable<ulong> TargetId` and drive Animator/audio from state.
- Noise events from clients (footsteps, throws) are sent to the server as `ServerRpc(position, loudness, type)` rate-limited (footsteps: max 4/s per player).
- **Catch** resolved on the server → `ClientRpc` to the victim's owner: shake + stun + drop (owner releases grabber/torch so physics ownership is clean); stolen item ownership → server (it's carried by the creature).
- Bell/bark/growl one-shots are triggered from state changes on clients (no audio RPCs), except bark which also feeds the server noise bus.
- Den location is generated with the map seed → identical on all peers.

---

## 8. Implementation plan

Rule: fit the plan's systems map (`CreatureBase / CreatureAI / CreatureSenses`), data in SOs, `protected virtual` per-state ticks so Phase 6 creatures (Dormilón, Invisible, Sombrerón) subclass instead of copying.

**New (folder `Assets/_Game/Enemies/`, later `Creatures/` per plan §3):**

| Class / asset | Role |
|---|---|
| `CreatureDefinition` (SO) | All tunables in §4.4 + `SoundDefinition` refs + prefab. `Chucho_Definition.asset`. |
| `CreatureBase` | Owns `NavMeshAgent`, `CreatureDefinition`, exposes `State`, `Target`, events (`StateChanged`, `Caught`, `Barked`). Future `NetworkBehaviour`. |
| `CreatureSenses` | Sight (ported from `PatrolEnemy.CanSee` + modifiers) and hearing (subscribes to the noise event). Non-alloc, sight checks at 5 Hz, not per frame. |
| `CreatureAI` | State machine above; `protected virtual void Tick<State>()` + `Enter/Exit`. `ChuchoAI : CreatureAI` only adds Fetch/Carry/Den/fire reactions. |
| `CreatureMouth` | Holds one `EvidencePickup` or `Torch` at a jaw socket (kinematic while held), drops it. Needs `EvidencePickup` to accept a non-player carrier (small change: carrier as `Component`/`object`, or a new `OnTakenByCreature()` state path). |
| `CreatureDen` | Marker component; generator places one per map (level-designer). Dropped items get `AtDen` flag only for HUD/score. |
| `CreatureAnimator` | Maps state + agent speed → Animator params; uses `RetroAnimationStepper` (creature at **12 fps** — creepier, cheaper). |
| `CreatureAudio` | Like `CharacterAudio`: dedicated source for bell/footsteps on animation contacts, pant loop, one-shots via `SoundManager`. |
| **Noise event** | Plan §2.2 `GameEventBus` with **only** `NoiseMadeEvent { position, loudness, type, source }` pulled forward (see Open Questions). Emitters: `CharacterAudio` (steps/land), `EvidencePickup` (impact/break), `Torch` (land), `PowerGenerator` (repair pulse/miss/start), `MissionManager.GeneratorsCompleted`. |

**Changes to existing:**
- `CharacterVitals`: add `Stun(float seconds, GameObject source)` + `IsStunned` + `Stunned/Recovered` events + `CatchCount`; stun disables Movement/Interaction like `Kill` but restores them. Keep `IKillable` for the 3rd strike.
- `CameraEffects`: add `public void Shake(float amplitude, float duration)` (trauma-based, on the Camera child so it doesn't fight CameraRoot).
- `PhysicsGrabber.Release()` / `TorchCarrier` drop are called on catch (public already / add `ForceDrop()`).
- `EnemySpawner`: spawn `CreatureDefinition.Prefab`; keep the primitive capsule as fallback; count from player-count curve (§10).
- `PatrolEnemy`: kept as legacy fallback until Chucho passes acceptance, then deleted.
- `LightingMoodController`: Chase state → `SetMood(Chase)`, back to Normal/Tense on Search end (first real use of moods — see LivingMap.md Director).
- `DebugHUD`: creature state, target, last noise heard.

---

## 9. Assets needed

### 9.1 Model brief — **3d-artist**
- **Chucho**, giant mangy stray hound. 3.0 m nose-to-tail, 1.5 m shoulder, head 2.0 m alert. **≤ 4,000 tris (hard cap 5k)**, one 512² texture (Point, no mips), `Fluffterror/ToonOutline` material, flat-shaded low-poly faceting like the dungeon kits.
- Silhouette priorities: huge paws, long bony spine, ribs, uneven ears (one up, one flopped), long tongue, scraggly tail, **tiny collar + oversized brass bell + tag "CHUCHO"**. Fur = 3–6 big jagged tufts, not detail. Palette: dirty grey-brown fur `#4A4340`/`#6B5F55`, pale bone-coloured snout, red-pink gums; eyes and drool use a separate small emissive area (red `#B3202A`).
- Comedy rule (ArtDirection §7): scary-big, not grotesque — no gore, no open wounds; mange and ribs are fine.
- **Rig:** generic (non-Humanoid) quadruped, ~35 bones: root, hips, spine×3, chest, neck×2, head, **jaw**, tongue×2, ears 2×2, tail×4, each leg 4 (upper/lower/foot/toe), collar + **bell** (bone, so the bell swings). Sockets: `MouthSocket` (child of jaw, carried item), `EyeL/EyeR` (for optional light). Forward = +Z, Y-up, metres, pivot at ground between the feet. Deliver FBX to `Assets/Art/Creatures/Chucho/`, source in `ArtSource/Creatures/`.
- Prefab: root with `NavMeshAgent` (radius 0.6, height 1.8), `CapsuleCollider` (horizontal-ish, trigger off), model child with Animator.
- **Agreed concept reference** (`ArtSource/Creatures/Chucho/concepts/`, approved mix):
  - `chucho_01.png` → the **faceted low-poly body** (big flat planes, hard facet lines) and the **clean side silhouette**: long straight back, deep chest, thin legs, low-slung head with an open jaw and tongue.
  - `chucho_02.png` → the **darker palette and menace**: near-black legs/underside, dark olive-brown body, hunched shoulders, head lowered, pinpoint **red** eyes (not 04's yellow), pointed ears.
  - `chucho_04.png` → the **scrawny stray look and the collar**: ragged pale fur fringe along the neck/back, skinny tucked belly, one ear flopped, thin leather collar.
  - **Added on top:** **visible ribs** (3–4 faceted ridges on the flank, readable in silhouette when lit from the side) and an **oversized brass bell** on the collar (fist-sized relative to the head, the brightest non-emissive spot on the model so it catches torchlight; tag "CHUCHO").
  - Final palette: body `#3A3631`/`#4A4340`, legs and belly `#1E1C1B`, fur fringe `#8C8070`, snout `#2A2A2A`, gums/tongue `#A0404A`, bell `#B08A3C`, eyes emissive `#B3202A`.
- The 3d-artist works from these four PNGs; no new concept round is needed.

### 9.2 Animation brief — **animator**
All loops in place (no root motion; NavMeshAgent moves), 30 fps source, stepped to 12 fps in game.

| Clip | Type | Length | Notes |
|---|---|---|---|
| `Idle_Pant` | loop | 2 s | chest heaving, tongue out |
| `Idle_Sniff` / `Idle_Scratch` / `Idle_Sit` | one-shot | 2–3 s | patrol variety (comedy) |
| `Walk` | loop | ~1.2 s | patrol 2.2 m/s |
| `Trot` | loop | ~0.8 s | investigate / carry (carry = head up additive) |
| `Gallop` | loop | ~0.5 s | chase / flee 7.5–8 m/s |
| `Sniff_Walk` | loop | | investigate/search, nose to floor |
| `Point` | one-shot → hold | 0.8 s | freeze, one paw raised, growl jaw |
| `Bark` | one-shot | 0.6 s | can layer on gallop start |
| `Circle_Growl` | loop | | sidestep strafe, head low (held at bay) |
| `Lunge` | one-shot | 0.35 s + land | |
| `Catch_BiteShake` | one-shot | 1.2 s | rears up, bite, head shake |
| `Drop_Item` + `TailWag` | one-shot | 1 s | at den |
| `Yelp_Flinch` | one-shot | 0.5 s | fire / hit |
| `Howl` | one-shot | 2 s | pack alert / dinner bell (Phase 2) |
| Player: `BonePile_Collapse` + `Reassemble` | one-shot | 0.6 s + 1 s | on the skeleton for the 5 s stun (animator; procedural fallback = drop the camera to 0.5 m + tilt) |

Controller: base locomotion blend tree on `Speed`; triggers `Point`, `Bark`, `Catch`, `Flinch`, `Drop`; bool `Carrying`, `HeldAtBay`. Bell bone driven by a cheap spring script or baked secondary motion. Foot-contact phases measured with the existing `FootContactAnalyzer` approach for bell/paw sync.

### 9.3 Sound brief — **sound-designer**
Follow ArtDirection §9 **format spec** (real CC0 recordings + `relaxed`, A/B round first); stings in `dark8bit`. Folder `Assets/_Game/Audio/SFX/Creatures/`, mono, 3D (spatialBlend 1), `Actions.mixer` except sting.

| SD asset | Files | Role / loudness | Note |
|---|---|---|---|
| `SD_Chucho_Bell` | ×6 | action −22 | brass bell single *tink*, per footfall; the key tell. Must cut through ambience. |
| `SD_Chucho_Paw` | ×8 | footstep −26 | heavy pad + **nail clicks on stone** |
| `SD_Chucho_Pant` | loop ×1 | ambient −30 | wet slow panting, 3–4 s seamless |
| `SD_Chucho_Sniff` | ×4 | action −22 | |
| `SD_Chucho_Growl` | ×3 | impact −20 | point telegraph, 0.8 s |
| `SD_Chucho_Bark` | ×4 | impact −20 | big, hoarse; doubles as noise event |
| `SD_Chucho_BiteShake` | ×3 | impact −20 | snap + bone rattle (our bone tick) |
| `SD_Chucho_Yelp` | ×3 | action −22 | comedy beat, not cartoonish |
| `SD_Chucho_Huff` | ×2 | idle −30 | lose interest |
| `SD_Chucho_Howl` | ×2 | impact −20 | distant, pack / dinner bell |
| `SD_Chucho_DropItem` | ×2 | action −22 | slobbery drop |
| `SD_Sting_Chase` | ×2 | 2D, `SFX.mixer`, dark8bit | plays on Chase enter, cooldown 20 s |
| `SD_Player_BonePile` | ×3 | impact −20 | skeleton collapse clatter (+ reassemble ×2) |

---

## 10. Scaling 1–8 players

- **Creature count** = `MissionConfig.CreatureCountByPlayers` (AnimationCurve or int[8]): **1,1,1,2,2,2,3,3**. Extra Chuchos spawn ≥ 40 m apart and from players.
- Per-creature stats don't scale (fairness is readable). More players already = more noise.
- **Pack:** a barking Chucho alerts others within `_barkAlertRadius` (Investigate its position) — never more than **2 chasing the same player** (`_maxChasersPerTarget`).
- Hunt bias picks the player **with the fewest recent creature visits**, so 8 players get spread pressure.
- Single player: `_lethalAfterCatches` 3 and hunt bias interval ×1.5 (60 s) — solo is gentler.

## 11. Performance

- Sight at 5 Hz, hearing event-driven, `FindObjectsByType` removed from per-frame use (player registry list on `CharacterBase` enable/disable).
- No real lights by default; no shadows from creature lights ever. Skinned mesh ≤ 4k tris, 1 material → 1 draw (+ outline pass).
- Animator culling: `CullUpdateTransforms` when off-screen; audio still runs from state.

## 12. Acceptance tests (TestScene + generated map)

1. Standing still in the open 8 m in front of a patrolling Chucho: not detected for 10 s. Walking: detected.
2. Sprinting 9 m from it behind a wall (path ≤ 15 m): it Investigates the spot; crouch-walking the same: ignored.
3. Detection always shows the **Point** pose + growl ≥ 0.8 s before any movement toward the player (frame-measured).
4. Catch: shake, 5 s no movement/interaction, held evidence released, Chucho carries it to the Den and drops it there; the item is collectable and extraction still counts it.
5. Third catch of the same player triggers `IKillable.Kill` (single-player → loss screen); caught twice → still alive.
6. Facing it with a torch at 3.5 m holds it 6 s ± 0.2; throwing the torch within 4 m makes it flee ≥ 4 s.
7. Throwing evidence past it during a chase: it fetches ≈ 50 % of the time over 20 trials.
8. Bell is audible and directional at 20 m in the TestScene long hall; silent when it stops.
9. `GeneratorsCompleted` sends every Chucho toward the extraction zone.
10. 3 Chuchos + 8 dummy players on the generated map: creature CPU < 0.5 ms/frame total, no GC alloc per frame.
11. It never gets stuck: 10-minute soak on 5 seeds, no agent stuck > 5 s (log).

## 13. Who does what

| Part | Agent |
|---|---|
| This spec, tuning passes, playtest notes | **game-designer** |
| Concept sheet, model, rig, prefab, materials | **3d-artist** |
| Clips, Animator Controller, bell secondary motion, player bone-pile collapse | **animator** |
| All SFX in §9.3 (A/B round first) | **sound-designer** |
| Den placement + spawn rules in `MultiSectionDungeonGenerator`, NavMesh validity | **level-designer** |
| Gameplay code §8 (CreatureBase/AI/Senses, noise bus, Vitals.Stun, CameraEffects.Shake) | **main session** (no dedicated coder agent) |
| Profiling per §11 | **optimizer** |
| Strike HUD, stun overlay | **ui-designer** (Phase 1.6) |

## 14. Decisions (resolved 2026-11-23)

| # | Question | Decision |
|---|---|---|
| 1 | Concept | **Chucho**, the bone hound |
| 2 | Catch rule | **Stun 5 s + drop + steal one item; 3rd catch = death** |
| 3 | Noise bus | **Minimal `GameEventBus` with `NoiseMadeEvent` pulled into 1.5** (PLANIFICACION updated) |
| 4 | Den | **Dedicated generated room** ("Chucho's Den"; level-designer, LivingMap S2) |
| 5 | Torch | **6 s shield + torch-carrier seen from farther** |
| 6 | Art direction | ArtDirection §1/§4 **updated for the skeleton protagonist** |
| 7 | Offer input | **Hold F / DPad Up** (dedicated `OfferBone` action) |
| 8 | Which limb | **Fixed order: right arm → left leg** |
| 9 | Jaw offer (Phase 5) | **Yes** — optional 3rd offer, mutes the player's proximity voice |
| 10 | Missing limbs at extraction | **−15 % score per missing limb, healed next run** |

---

## 15. Bone offering — "Here, boy… take my arm"

> User idea: *"Chucho, if you offer your own bone, could calm down, but you lose a limb."*

### 15.1 Pitch & experience
Chucho is charging. You panic-hold **F**: your skeleton yanks off its own arm and holds it out. Chucho skids to a stop, tail wagging, snatches the arm and trots happily back to its den to chew it. The hunt is over — for a while — and you're now a one-armed skeleton who can't hold the torch and the evidence at the same time. Later, a teammate runs into the den, grabs your arm and throws it to you across the room.

### 15.2 Rules

**When you can offer** (the prompt "Hold F — offer a bone" appears only then):
- A Chucho is within `_offerRange` (6 m) of you, in line of sight, in **Point, Chase, Held At Bay, Search or Investigate**, and not already Chewing/Carrying; **and**
- You have a limb left to offer (`_maxOffersPerPlayer`), you're not stunned/dead/frozen (Lumi) /asleep (Mochín).
- Chucho does **not** need to be targeting *you*: any player can offer to stop a Chucho hunting a teammate within 6 m of the offerer ("**heroic donation**").

**Input:** new `OfferBone` Button action (keyboard **F**, gamepad DPad Up) → `InputReader.OfferBoneEvent(bool)` (hold, like `CrouchEvent`). Hold `_offerHoldTime` (0.5 s): the skeleton grabs the limb; releasing early cancels with no cost. While holding you can't move faster than walk; you can still turn.

**Resolution:**
1. Hold completes → the limb detaches (pop + bone clatter), you hold it out (`Offer` pose, 0.4 s). Lose the limb **now** (the cost is paid even if things go wrong).
2. Chucho (if still within `_offerAcceptRange` 8 m, not yet in its Catch animation) enters **Accept**: stops, sits, takes the limb into `CreatureMouth`. If its Catch already started, the bite still lands (the limb is wasted, it's dropped on the floor — bad timing is a story too).
3. Chucho enters **Chewing**: carries the limb to its den (non-hostile, like Carry) and chews for `_chewDuration[n]` where n = how many bones *this* Chucho has accepted this run: **45 / 30 / 20 s**, then refuses (`_maxAcceptsPerCreature` = 3 → after that it sniffs the offered bone, ignores it and keeps hunting; the limb is lost on the floor where you can pick it back up).
4. While chewing it ignores **everyone** (it's the only creature calmed; other Chuchos keep hunting). It still hears noise but only reacts with an ear flick. A thrown item hitting it, or a player taking the limb out of the den **while it's chewing**, ends Chewing → Chase on that player (+ "my bone!" growl).
5. After chewing it leaves the limb in the den's bone pile and returns to Patrol. The **offerer** is a "friend" for `_friendTime` (20 s): this Chucho won't start a Point/Chase on them.
6. Offering does **not** count as a catch (no strike).

### 15.3 Limbs and their cost
Default order `_limbOrder` = **Right arm → Left leg** (`_maxOffersPerPlayer` = 2). Left arm is kept so the torch can always be held (`TorchArmAnimator` drives the **left** arm). Phase 5 adds **Jaw** as an optional third.

| Limb lost | Gameplay cost | Comedy |
|---|---|---|
| **Right arm** | **One-handed:** torch *or* grab beam, never both — starting a grab with the torch held drops the torch lit at your feet (G still throws whichever you hold). Grab is weaker: `_maxLiftMass` ×`_oneArmLiftFactor` (0.6), throw speed ×0.7. Can't repair generators faster than ×`_oneArmRepairFactor` (0.75). | Torch-or-evidence juggling; dropping your torch every time you grab |
| **Left leg** | **Hop:** move speed ×`_oneLegSpeedFactor` (0.65), **no sprint**, jump force ×0.5, crouch allowed. Every hop is a footstep with loudness 0.2 (louder than a walk — hopping thuds). Chucho catches you easily. | Hop locomotion, camera bob ×2 |
| **Jaw** (Phase 5, optional) | Can't talk in proximity voice (Vivox muted; text pings only). Chattering-teeth emote unavailable. | Muted player miming to the team |
| **Both arm+leg** | Both effects stack | The run's legend |

The **skull is never offered** (it's the camera). Death from the 3rd catch keeps its rule; missing limbs make catches likelier (leg) but don't change the strike count.

### 15.4 Recovering limbs
- Limbs are **`LimbPickup`** world items: a physics prop carried with the grab beam like evidence (`PhysicsGrabber`), mass 1.5 kg, not breakable, named "Pepe's right arm". Anyone can carry or throw them.
- Found in Chucho's den (bone pile) after chewing, or wherever a refused/wasted offer dropped it.
- **Reattach:** the *owner* aims at their own limb (in the world or held by a teammate within 2 m) and holds E for `_reattachTime` (2 s, bone-click sound) → limb restored. A teammate can't attach it for you (Coso can: 30 % he attaches it **backwards** → controls for that limb reversed: arm = grab beam pushes instead of pulls / leg = hop goes slightly sideways, until reattached again).
- Chucho **fetches** limbs like evidence if it sees one thrown or being carried (fetch chance rules, §4.2): the arm can be stolen back to the den.
- At **extraction** all limbs are restored for the next run; each limb still missing at extraction = `_missingLimbScorePenalty` (−15 % of the player's score share, "medical bill" line on the score screen). (Decided: #10.)
- **Jaw** (decided #9): added to `_limbOrder` as the optional 3rd offer once Phase 5 voice chat exists; losing it mutes proximity voice until reattached.

### 15.5 Tunables (on `CreatureDefinition` for creature-side, new `SkeletonLimbConfig` SO for player-side)

| Field | Default |
|---|---|
| `_offerRange` / `_offerAcceptRange` | 6 m / 8 m |
| `_offerHoldTime` | 0.5 s |
| `_chewDuration` (per accepted bone, this creature) | 45 / 30 / 20 s |
| `_maxAcceptsPerCreature` | 3 per run |
| `_friendTime` | 20 s |
| `_maxOffersPerPlayer` / `_limbOrder` | 2 / [RightArm, LeftLeg] (+ Jaw in Phase 5) |
| `_oneArmLiftFactor` / throw factor / `_oneArmRepairFactor` | 0.6 / 0.7 / 0.75 |
| `_oneLegSpeedFactor` / jump factor / hop noise | 0.65 / 0.5 / 0.2 |
| `_reattachTime` | 2 s |
| `_cosoBackwardsChance` | 0.3 |
| `_missingLimbScorePenalty` | 0.15 per limb |

**Balance check:** a full team of 8 can buy at most 3 × 1 Chucho = 95 s of peace per Chucho per run (out of a 300 s mission), every bone costs a real handicap until someone makes the den trip, and the den is where Chucho comes back to. Solo: 2 offers max → the offer is a panic button, not a strategy.

### 15.6 Flaw interactions

| Character | Interaction |
|---|---|
| **Trotín** (can't stop) | Can't hold still to offer: his offer is a **drive-by** — the limb is thrown behind him as he runs and Chucho *fetches* it (Accept happens at the limb, not at Trotín). Losing a leg mid-sprint → he keeps going, hopping at 3× speed, in a straight line. |
| **Lumi** (frozen by loud sounds) | Can't offer while frozen — Chucho's bark freezes her right when she needs the prompt. A teammate must donate for her. |
| **Mochín** (sleeps) | Asleep = can't offer. Chucho finds sleeping Mochín, sniffs… and **takes a limb by itself** (not a catch, no stun): Mochín wakes up with one arm fewer. |
| **Coso** (breaks things) | 30 % his offered bone **snaps in two**: Chucho chews ×1.5 longer, but Coso has to recover two pieces (both needed to reattach). And the backwards-reattach rule above. |
| **Bobi** (smell, distracted by shiny things) | A dropped limb is a distraction for Bobi too (`ShinyObjectDistraction`): Bobi and Chucho race for the same arm. Bobi's smell ping shows lost limbs on the map. |
| **Fotchi** (auto-photo) | An offering is a dramatic event → auto-flash: the flash startles the accepting Chucho (Flinch, the limb is dropped and the hunt restarts) **25 %** of the time. Photo of "the sacrifice" is evidence ×1.5. |
| **Sabiucho** (cries if ignored) | Warning banner "¡No le des tu brazo!" when someone starts the hold; if they finish, it counts as an ignored warning → cry (a 0.5 noise loop — which a *chewing* Chucho ignores, but other Chuchos don't). |
| **Pompón** (hears through walls) | Hears the chewing through walls → always knows where the den is and when the chewing stops ("it stopped chewing…"). |

### 15.7 Multiplayer notes (Phase 4)
- Server validates the offer (range, creature state, limbs left) via `ServerRpc(creatureId)`; server-side `NetworkVariable<byte> LimbMask` on each `CharacterVitals` (bit per limb) → every client applies the visual (mask/collapse) and the gameplay modifiers read it locally on the owner.
- `LimbPickup` is a pooled `NetworkObject` owned by the server; carried-by-creature uses the same path as stolen evidence.
- Reattach: `ServerRpc` from the owner, server checks distance + ownership, clears the bit, despawns the pickup.
- Chewing is a creature state → already synced by `NetworkVariable<CreatureState>`.

### 15.8 Implementation plan (main session; no code in this doc)
- **Input:** add `OfferBone` to `InputSystem_Actions.inputactions` (F / DPad Up) and `OfferBoneEvent(bool)` to `InputReader` (same null-guard pattern as the other events).
- **`SkeletonLimbs`** component (auto-added by `CharacterBase.Awake`, like `TorchCarrier`): `LimbMask`, `CanOffer`, `TryOffer`, `Reattach(LimbId)`, events `LimbLost` / `LimbRestored`; exposes the modifier factors that `CharacterMovement` (speed, sprint block, jump), `PhysicsGrabber` (lift/throw), `TorchCarrier` (one-hand exclusivity) and `PowerGenerator` (repair rate) read through `protected virtual` getters — no parallel stat system, just multipliers on existing tunables.
- **`BoneOffering`** component on the player: listens to the hold, finds the eligible Chucho via the creature registry, shows the prompt through `InteractionSystem.CurrentPrompt` priority (or `InteractionPromptUI` override), spawns the `LimbPickup` and hands it to `CreatureAI.TryAcceptOffer(limb, offerer)`.
- **`ChuchoAI`:** new states **Accept** and **Chewing** (`protected virtual` like the others), per-creature accept counter, friend list with expiry.
- **Visual removal on the single skinned mesh** — reuse `FirstPersonBodyCull`'s approach:
  - **Primary:** at Awake build per-vertex limb IDs from bone weights (vertices whose dominant weight is under `RightUpperArm`/`LeftUpperLeg` subtree), stored in a spare UV channel (UV3.y — UV3.x is already the head mask). `Fluffterror/ToonOutline` gets a `_HiddenLimbs` bitmask float; discard matching vertices in **all** passes, **including ShadowCaster** (a lost limb has no shadow, unlike the hidden head). Needs Read/Write on `Skeleton.fbx` (already required).
  - **Fallback** (mesh not readable / other shaders): collapse the limb root bone scale to 0.001 like the old neck collapse.
  - Remote players use the same mask (it's not a first-person-only effect).
  - **Separate limb props** (from the 3d-artist): `Limb_RightArm`, `Limb_LeftArm`, `Limb_LeftLeg`, `Limb_RightLeg` low-poly meshes cut from the skeleton mesh with the same material; jaw reuses `Skeleton_Jaw.fbx` (just disable the `JawPivot` renderer on the player).
- **Animation hookup:** Animator layer `Hop` (Override, lower body) on when the leg bit is set; right-arm layer masked/zeroed when the arm bit is set (the remaining stump holds still). `TorchArmAnimator` unaffected (left arm).
- **HUD:** limb silhouette icon with missing pieces next to the catch-strike bones (ui-designer).

### 15.9 Assets
| Agent | Needs |
|---|---|
| **3d-artist** | 4 limb props cut from `Skeleton.fbx` (same UVs/material, pivot at the joint, colliders = capsule), jaw reuses existing; den "bone pile" dressing with room for limb props |
| **animator** | Player: `Offer_Rip` (grab + yank own arm, 0.5 s, matches the hold), `Offer_Hold` (arm held out, 0.4 s), `Hop` locomotion (idle/hop-walk/hop-turn, lower-body mask), `Reattach` (2 s, click it back), stump pose for the lost right arm. Chucho: `Accept_Sit` (skid + sit + take, 0.8 s), `Chew` loop (lying down, limb between paws, 3 s loop), `Refuse_Sniff` (sniff + turn away, 1 s) |
| **sound-designer** | `SD_Player_LimbPop` ×3 (bone pop + bony clatter), `SD_Player_Reattach` ×2 (click), `SD_Player_Hop` ×6 (heavier single-foot thud, footstep role), `SD_Chucho_Chew` loop (gnawing, 3D, audible to ~12 m — the "it's still busy" tell), `SD_Chucho_HappyWhine` ×2 (accept), `SD_Chucho_MyBone` growl ×2 |
| **ui-designer** | Offer prompt (hold ring), limb silhouette icon, score-screen "medical bill" / "Heroic donation" titles |

### 15.10 Acceptance tests
1. Prompt appears only within 6 m of a hunting/alerted Chucho with limbs left; releasing before 0.5 s costs nothing.
2. Completing the hold during Chase: right arm vanishes from body **and shadow**, an arm prop is created, Chucho sits, takes it, walks to the den, chews 45 s; nobody is attacked by it meanwhile; the offerer is ignored for 20 s after.
3. A 4th offer to the same Chucho is refused; the limb is dropped and recoverable.
4. One arm: grabbing evidence while holding the torch drops the torch lit; lift of an 8 kg item visibly sags more than with two arms.
5. One leg: speed 3.25 m/s, Shift does nothing, hop steps are heard by Chucho at 6 m.
6. Owner reattaches a limb from the den in 2 s; all modifiers revert; mask cleared.
7. Teammate offer: player B offers while Chucho chases player A 5 m away → Chucho stops chasing A.
8. Offering never increments the catch-strike counter; the 3rd catch still kills a two-limbed-short player.

