# Memory Index

- [Active dungeon generator](active-dungeon-generator.md) — scene uses MultiSectionDungeonGenerator, not the DungeonGenerator that CLAUDE.md documents
- [Generator machinery](generator-machinery.md) — repairable PowerGenerator machines (hold-E repair, press-E run), spawned 3 per map; hold-interaction via IHoldInteractable
- [Patrol creature](patrol-creature.md) — Phase 1.5 NavMesh enemy that spawns randomly and kills the player; death path via IKillable/CharacterVitals
