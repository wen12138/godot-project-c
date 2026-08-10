# 游戏数据管理框架实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 落地薄 DTO + Mapper + SaveService Autoload，仅搭建 `scripts/data/` 数据框架。

**Architecture:** 持久化用 `SaveGameDto` + `SaveService`（Autoload，JSON 读写）；玩家会话用可注入的 `PlayerRuntimeState`；二者只通过 `PlayerSaveMapper` 转换。本轮不接 Actor、不做自动化测试。

**Tech Stack:** Godot 4.6、C# / .NET 8、System.Text.Json、`user://` FileAccess

## Global Constraints

- 文档与计划使用中文；代码标识符保持英文
- 只创建/修改 `scripts/data/*` 与 `project.godot` Autoload
- 不做 smoke、自证、自动化测试
- 存档路径：`user://saves/slot_{n}.json`；当前 Version = `1`
- 不使用 `.tres` / `.res` 作为玩家存档

## 文件结构

| 文件 | 职责 |
|------|------|
| `scripts/data/SaveGameDto.cs` | 可序列化快照 |
| `scripts/data/PlayerRuntimeState.cs` | 可注入运行时状态 |
| `scripts/data/PlayerSaveMapper.cs` | Runtime ↔ Dto |
| `scripts/data/SaveService.cs` | Autoload 文件 I/O |
| `project.godot` | 注册 `SaveService` Autoload |

---

### Task 1: DTO 与运行时状态

**Files:**
- Create: `scripts/data/SaveGameDto.cs`
- Create: `scripts/data/PlayerRuntimeState.cs`

**Interfaces:**
- Produces: `SaveGameDto { Version, SchemaProbe }`；`PlayerRuntimeState { SchemaProbe, Reset() }`

- [x] **Step 1: 创建 `SaveGameDto.cs`**

```csharp
namespace ProjectC.Data;

public sealed class SaveGameDto
{
	public int Version { get; set; } = 1;
	public string SchemaProbe { get; set; } = "";
}
```

- [x] **Step 2: 创建 `PlayerRuntimeState.cs`**

```csharp
namespace ProjectC.Data;

public sealed class PlayerRuntimeState
{
	public string SchemaProbe { get; set; } = "";

	public void Reset()
	{
		SchemaProbe = "";
	}
}
```

- [x] **Step 3: 编译确认类型可用**

Run: `dotnet build ProjectC.csproj`  
Expected: 成功（或仅有与本次无关的既有警告）

---

### Task 2: Mapper

**Files:**
- Create: `scripts/data/PlayerSaveMapper.cs`

**Interfaces:**
- Consumes: `SaveGameDto`, `PlayerRuntimeState`
- Produces: `ToDto(...)`, `ApplyTo(...)`

- [x] **Step 1: 创建 `PlayerSaveMapper.cs`**

```csharp
namespace ProjectC.Data;

public static class PlayerSaveMapper
{
	public const int CurrentVersion = 1;

	public static SaveGameDto ToDto(PlayerRuntimeState runtime)
	{
		return new SaveGameDto
		{
			Version = CurrentVersion,
			SchemaProbe = runtime.SchemaProbe,
		};
	}

	public static void ApplyTo(SaveGameDto dto, PlayerRuntimeState runtime)
	{
		runtime.SchemaProbe = dto.SchemaProbe ?? "";
	}
}
```

- [x] **Step 2: 编译确认**

Run: `dotnet build ProjectC.csproj`  
Expected: 成功

---

### Task 3: SaveService Autoload

**Files:**
- Create: `scripts/data/SaveService.cs`
- Modify: `project.godot`（`[autoload]` 节）

**Interfaces:**
- Consumes: `SaveGameDto`, System.Text.Json, FileAccess
- Produces: `Save`, `Load`, `Exists`, `Delete`；空 `Migrate` 钩子（本轮不调用）

- [x] **Step 1: 创建 `SaveService.cs`**

实现要点：
- `partial class SaveService : Node`
- 路径 `user://saves/slot_{slot}.json`
- Save 前 `DirAccess.MakeDirRecursiveAbsolute("user://saves")`
- `JsonSerializer.Serialize` / `Deserialize<SaveGameDto>`
- `Version != CurrentVersion`（与 Mapper 同为 1）时 `Load` 返回 null 并 `GD.PushError`
- 预留 `private SaveGameDto Migrate(SaveGameDto dto) => dto;` 本轮不调用
- API 签名与 spec 一致：`Error Save`、`SaveGameDto Load`、`bool Exists`、`Error Delete`

- [x] **Step 2: 注册 Autoload**

在 `project.godot` 的 `[autoload]` 增加：

```ini
SaveService="*res://scripts/data/SaveService.cs"
```

保留已有 `McpInteractionServer` 条目。

- [x] **Step 3: 编译确认**

Run: `dotnet build ProjectC.csproj`  
Expected: 成功

---

### Task 4: 收尾核对

- [x] **Step 1: 对照 spec 检查清单**
  - [x] 四类文件均在 `scripts/data/`
  - [x] Autoload 已注册
  - [x] 无 Actor 接线、无 smoke/自测代码
  - [x] 占位字段 `SchemaProbe` + `Version` 存在

- [x] **Step 2: 向用户汇报完成与手动检验建议**（由用户自行在后续开发中验证）
