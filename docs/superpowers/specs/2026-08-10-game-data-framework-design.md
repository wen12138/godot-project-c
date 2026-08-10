# Game Data Management Framework Design

Date: 2026-08-10  
Project: ProjectC (Godot 4.6 / C#)  
Status: Approved

## Goal

Scaffold a minimal game data framework that separates **persistent save data** from **injectable player runtime state**, with a mapper between them. No concrete gameplay fields yet—only structure for later development. Validation during play will be done manually by the developer; no framework self-test or automated test harness in this scope.

## Constraints

- Save archive access: Autoload global (`SaveService`)
- Player runtime data: injectable plain C# object (not Autoload)
- Deliver only `scripts/data/`; do not wire into `Actor` or components in this pass
- No UI, encryption, compression, cloud saves, or version migration implementation
- No smoke-on-ready / auto-test in `SaveService`

## Architecture

```text
scripts/data/
  SaveGameDto.cs          // serializable snapshot
  PlayerRuntimeState.cs   // session state, injectable
  PlayerSaveMapper.cs     // Runtime ↔ Dto
  SaveService.cs          // Autoload: file I/O
```

```text
New Game → PlayerRuntimeState
              ↕ PlayerSaveMapper
           SaveGameDto  ↔  SaveService  ↔  user:// JSON
```

| Type | Responsibility | Must not |
|------|----------------|----------|
| `SaveGameDto` | JSON-serializable snapshot; `Version` + placeholder field | Hold Nodes or game logic |
| `PlayerRuntimeState` | In-memory player session state | Read/write disk |
| `PlayerSaveMapper` | `ToDto` / `ApplyTo` | Touch files |
| `SaveService` | Slot save/load/exists/delete under `user://` | Own player runtime instances |

## Autoload

In `project.godot`:

```text
SaveService = *res://scripts/data/SaveService.cs
```

## API Surface

### `SaveGameDto`

- `int Version`
- `string SchemaProbe` — placeholder to prove round-trip later; real fields replace/extend this over time

### `PlayerRuntimeState`

- `string SchemaProbe`
- `void Reset()` — default values for a new game

### `PlayerSaveMapper`

- `SaveGameDto ToDto(PlayerRuntimeState runtime)`
- `void ApplyTo(SaveGameDto dto, PlayerRuntimeState runtime)`

### `SaveService` (Autoload)

- `Error Save(int slot, SaveGameDto dto)`
- `SaveGameDto Load(int slot)` — on failure: `null` + `GD.PushError`
- `bool Exists(int slot)`
- `Error Delete(int slot)`

## Conventions

- Path: `user://saves/slot_{n}.json`
- Current schema version: `1`
- Before write: ensure directory via `DirAccess.MakeDirRecursiveAbsolute`
- On load, if `Version != 1`: return `null` and log; leave an empty `Migrate` hook for later (not called in this pass)
- Serialize with System.Text.Json or Godot-friendly JSON into plain DTO shape (scalars/strings only for now)
- `PlayerRuntimeState` is created and owned by gameplay code later; this pass only provides the type

## Error Handling

| Case | Behavior |
|------|----------|
| Missing save directory | Create recursively on Save |
| Write / read failure | Return `Error`, `GD.PushError` |
| JSON parse failure | `Load` returns `null` |
| Unsupported version | `Load` returns `null` |

## Out of Scope

- Actor / component injection wiring
- Framework self-proof, smoke tests, automated tests
- Multi-slot UI
- Save encryption / compression
- Full version migration implementation
- Using `.tres` / `.res` for player save files

## Future Extensions

- Add real fields to DTO and Runtime together; keep Mapper as the only bridge
- Implement incremental `Migrate(dto)` when bumping `Version`
- Inject `PlayerRuntimeState` into `Actor` and components when gameplay needs it
- Optional Saveable registry if many world entities become persistent
