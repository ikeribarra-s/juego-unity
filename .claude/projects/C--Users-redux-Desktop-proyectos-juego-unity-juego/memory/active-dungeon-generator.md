---
name: active-dungeon-generator
description: Which dungeon generator the scene actually uses (CLAUDE.md is stale on this)
metadata:
  type: project
---

The live procedural level generator is `Assets/_Game/Environment/MultiSectionDungeonGenerator.cs` — it is the component on the "DungeonGenerator" GameObject in `Assets/Scenes/SampleScene.unity` (m_EditorClassIdentifier `Assembly-CSharp::MultiSectionDungeonGenerator`).

`Assets/_Game/Environment/DungeonGenerator.cs` and `DressedDungeonGenerator.cs` are older/parallel generators NOT in the scene. Both the root `CLAUDE.md` and `Assets/_Game/Environment/CLAUDE.md` document `DungeonGenerator.cs` as if it were the generator — they are stale. When working on dungeon generation, edit MultiSectionDungeonGenerator.

**Why:** Editing the documented `DungeonGenerator.cs` changes nothing in-game.
**How to apply:** Confirm the component on the scene's dungeon GameObject before editing a generator.
