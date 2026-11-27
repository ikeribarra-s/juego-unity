# Networking (NGO) — design + status

Plan of record: milestones M0–M8 (see the approved plan; summary below). Decisions: **Netcode for GameObjects 2.14**, **host-client** (the host owns the world; each player owns their own movement, `NetworkTransform` AuthorityMode = Owner), **direct IP first**, Relay + Lobby later. Solo = a host with no clients (one code path). 1–8 players.

## Status
| Milestone | State |
|---|---|
| M0 Foundation | **done** (2026-11-27) |
| M1 Players see each other (animation, pitch, flags) | **done** (2026-11-27) |
| M2 Torches · M3 Interaction/evidence/grab · M4 Mission/generators/creature · M5 Death & bones · M6 Session flow · M7 Relay+Lobby · M8 Hardening | planned |

## M0 — what exists
`Assets/_Game/Network/`
- **`NetConfig`** (`Resources/NetConfig.asset`): player prefab, inert remote `InputReader`, scene names, max players (8), port (7777), tick rate (30).
- **`NetworkBootstrap`**: persistent singleton that builds the `NetworkManager` + `UnityTransport` at runtime (no scene asset), `StartHostSession(loadGameScene)`, `StartClientSession(ip)`, `StopSession()`, connection approval (player cap), spawns players on the server after the game scene loads (`OnLoadEventCompleted`, or `SynchronizeComplete` for late joiners), `Application.runInBackground = true`. Playing the game scene directly in the editor auto-starts a solo host. Command line for test builds: `-host`, `-solo`, `-client <ip>`, `-port <n>`.
- **`PlayerFactory`** (also the `INetworkPrefabInstanceHandler`): instantiates the player under an **inactive holder**, calls `CharacterBase.ConfigureAsRemote(...)` for other players' copies, then activates it — so `Awake`/`OnEnable` already see the remote state (nothing subscribes to input or locks the cursor on a remote copy).
- **`NetworkPlayer`** (NetworkBehaviour on the prefab root): registry (`All`, `Local`).
- **`Net`**: `IsOnline`, `IsServer` (true offline too), `LocalPlayer`. `CharacterBase.FindLocal()` now returns the owned player first.
- **`PlayerSpawnPoint`** (4 in SampleScene where the old scene character stood), **`LobbyMenu`** (IMGUI Solo / Host / Join IP placeholder), `Scenes/Lobby.unity`, `Player.prefab`.
- Editor: **Fluffterror > Network > Build Network Setup** (`Assets/Editor/NetworkSetupBuilder.cs`, re-runnable; builds the prefab only if missing).
- Remote copy (`ConfigureAsRemote`): inert input, `CharacterMovement` + `InteractionSystem` disabled, camera/listener/reverb/`CameraEffects`/`NeckFollowCamera` off, `FirstPersonBodyCull` full body, `TorchArmAnimator` plain arm, `CartoonAnimator` remote.
- TestScene is untouched (scene-placed character + TestDummy, no bootstrap = offline).

## Testing multi-peer (no second PC)
1. Editor: open SampleScene, Play (auto solo host on 7777).
2. Build a Windows **Mono development** player (scenes: Lobby, SampleScene; first build compiles all shaders, ~25 min, later ones are incremental).
3. Launch the client: `Fluffterror.exe -client 127.0.0.1 -netdrive -netlog -logFile client.log` (minimized so nothing clicks the lobby). `Testing/NetTestDriver` (`-netlog` / `-netdrive`) logs every player's position each second and walks the local player through a route (forward, strafe, jump, crouch, turn). Attach it in the editor (set `_log`) to log the host's view; compare the two logs.
- Verified 2026-11-27: connect, both players spawn on both peers, local/remote configuration correct (1 AudioListener, remote has no movement/camera), position + yaw replicate both ways (client view vs host view equal at rest, jump apex seen), no exceptions.

## M1 — remote players animate
- **`CharacterMovement`** can mirror another player's state: `SetRemoteState(planarVelocity, verticalVelocity, grounded, crouching, sprinting)` makes `IsGrounded / IsCrouching / IsSprinting / VerticalVelocity / PlanarVelocity` return those values, `RaiseJumped()` fires `Jumped`, `CameraPitch` exposes the look pitch. Every reader (`AnimationHandler`, `CharacterAudio`, `FootIK`, `CartoonAnimator`, `WaterSplashEmitter`) keeps reading the same properties — no network code in them. `CharacterBase.ConfigureAsRemote` switches it on before activation.
- **`NetworkMotion`** (NetworkBehaviour on the player prefab, order -50): the owner writes a `NetworkVariable<State>` (flags grounded/crouch/sprint, vertical velocity, pitch; owner-write) and calls `JumpedRpc` (`SendTo.NotOwner`, `InvokePermission.Owner`); remote copies derive **planar velocity from their own interpolated transform** (`_velocitySmoothTime` 0.08 s, teleport guard 25 m/s) so the feet always match the motion on screen, feed the rest to `SetRemoteState`, and apply the pitch to `CameraRoot` (the head-look IK target).
- Builder: `NetworkSetupBuilder.EnsurePlayerPrefabComponents()` adds `NetworkMotion` to the prefab (re-runnable, also called by Build Network Setup). **A client build made before a prefab change is rejected by `ForceSamePrefabs`: rebuild the player after touching `Player.prefab`.**
- Verified 2026-11-27 (editor host, build client running `-netdrive`): the host's copy of the client player animates — walk `Speed 3.04 MoveZ 3.04`, strafe `MoveX 2.96`, jump `Grounded False / FreeFall True`, crouch `Crouch True`, idle all 0; third-person shots (`NetTestDriver.ShotDir`) show the stride and crouch poses. Not verified yet: look pitch on the remote head, footstep sounds of remote players, sprint.

## Known gaps (next milestones)
- Remote players' crouch collider stays standing size (only the pose crouches).
- Everything else (torch, evidence, generators, creature, death/bones) is still local-only.
