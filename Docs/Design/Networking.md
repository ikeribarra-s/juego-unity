# Networking (NGO) — design + status

Plan of record: milestones M0–M8 (see the approved plan; summary below). Decisions: **Netcode for GameObjects 2.14**, **host-client** (the host owns the world; each player owns their own movement, `NetworkTransform` AuthorityMode = Owner), **direct IP first**, Relay + Lobby later. Solo = a host with no clients (one code path). 1–8 players.

## Status
| Milestone | State |
|---|---|
| M0 Foundation | **done** (2026-11-27) |
| M1 Players see each other (animation, pitch, flags) | next |
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

## Known gaps (next milestones)
- Remote players have no animation yet (idle pose): needs `IMotionSource` + replicated flags/pitch (M1).
- Everything else (torch, evidence, generators, creature, death/bones) is still local-only.
