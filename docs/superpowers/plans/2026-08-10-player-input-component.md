# 玩家输入组件实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 落地 `InputActions` 常量类、`PlayerInputComponent` 输入转发，以及 `MovementComponent` 移动/跳跃桩 API，并挂到 `Player.tscn`。

**Architecture:** 输入层只读 Input Map 并转发意图；移动层本轮只提供空实现入口。兄弟节点直连：`PlayerInputComponent` → `MovementComponent`。Action 名统一经 `InputActions`，禁止魔法字符串。

**Tech Stack:** Godot 4.6、C# / Godot.NET.Sdk、现有 `project.godot` Input Map

## Global Constraints

- 文档与计划使用中文；代码标识符保持英文
- 不做真实移动/跳跃物理、手柄轴、输入缓冲、重绑定 UI
- 不改动 `Actor` 职责（输入不经 Actor 转发）
- 不做自动化测试；用手编/运行确认节点绑定与桩调用即可
- Input Action 字符串只允许出现在 `InputActions` 中
- 类名文件以 `PlayerInputComponent` 为准（非 Compoent）

## 文件结构

| 文件 | 职责 |
|------|------|
| `scripts/InputActions.cs` | Action 名常量 + `GetMoveVector` / `IsJumpJustPressed` |
| `scripts/PlayerInputComponent.cs` | `_PhysicsProcess` 读输入并调用 Movement |
| `scripts/MovementComponent.cs` | 新增 `SetMoveInput` / `Jump` 空实现 |
| `prefabs/Player.tscn` | 增加 `PlayerInputComponent` 节点并绑定脚本 |

---

### Task 1: InputActions 常量与辅助方法

**Files:**
- Create: `scripts/InputActions.cs`

**Interfaces:**
- Produces:
  - `const string MoveUp = "move_up"`
  - `const string MoveDown = "move_down"`
  - `const string MoveLeft = "move_left"`
  - `const string MoveRight = "move_right"`
  - `const string Jump = "jump"`
  - `static Vector2 GetMoveVector()`
  - `static bool IsJumpJustPressed()`

- [ ] **Step 1: 创建 `scripts/InputActions.cs`**

```csharp
using Godot;

public static class InputActions
{
	public const string MoveUp = "move_up";
	public const string MoveDown = "move_down";
	public const string MoveLeft = "move_left";
	public const string MoveRight = "move_right";
	public const string Jump = "jump";

	public static Vector2 GetMoveVector()
	{
		return Input.GetVector(MoveLeft, MoveRight, MoveUp, MoveDown);
	}

	public static bool IsJumpJustPressed()
	{
		return Input.IsActionJustPressed(Jump);
	}
}
```

- [ ] **Step 2: 编译确认**

Run: `dotnet build ProjectC.csproj`  
Expected: 成功（或仅有与本次无关的既有警告）

- [ ] **Step 3: Commit**

```bash
git add scripts/InputActions.cs
git commit -m "添加 InputActions 常量与输入辅助方法。"
```

---

### Task 2: MovementComponent 桩 API

**Files:**
- Modify: `scripts/MovementComponent.cs`

**Interfaces:**
- Consumes: 无（本任务不依赖 Task 1）
- Produces:
  - `void SetMoveInput(Vector2 direction)` — 空实现
  - `void Jump()` — 空实现

- [ ] **Step 1: 在 `MovementComponent` 中追加桩方法**

在现有类中增加（保留既有字段与 `_Ready` / `_Process` 不变）：

```csharp
public void SetMoveInput(Vector2 direction)
{
}

public void Jump()
{
}
```

完整目标文件示意（在既有成员之后追加方法即可）：

```csharp
using Godot;
using System;

public partial class MovementComponent : Node
{
	private TransformComponent m_TransformComponent;

	public override void _Ready()
	{
		m_TransformComponent = GetNode<TransformComponent>("TransformComponent");
	}

	public override void _Process(double delta)
	{
	}

	public void SetMoveInput(Vector2 direction)
	{
	}

	public void Jump()
	{
	}
}
```

说明：本任务不修正既有 `GetNode("TransformComponent")` 路径问题（范围外）。

- [ ] **Step 2: 编译确认**

Run: `dotnet build ProjectC.csproj`  
Expected: 成功

- [ ] **Step 3: Commit**

```bash
git add scripts/MovementComponent.cs
git commit -m "为 MovementComponent 添加移动与跳跃桩方法。"
```

---

### Task 3: PlayerInputComponent

**Files:**
- Create: `scripts/PlayerInputComponent.cs`

**Interfaces:**
- Consumes:
  - `InputActions.GetMoveVector()` / `InputActions.IsJumpJustPressed()`
  - `MovementComponent.SetMoveInput(Vector2)` / `MovementComponent.Jump()`
- Produces: `PlayerInputComponent` 节点脚本，兄弟路径 `../MovementComponent`

- [ ] **Step 1: 创建 `scripts/PlayerInputComponent.cs`**

```csharp
using Godot;

public partial class PlayerInputComponent : Node
{
	private MovementComponent m_Movement;

	public override void _Ready()
	{
		m_Movement = GetNodeOrNull<MovementComponent>("../MovementComponent");
		if (m_Movement == null)
		{
			GD.PushError("PlayerInputComponent: missing sibling MovementComponent at ../MovementComponent");
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		if (m_Movement == null)
		{
			return;
		}

		m_Movement.SetMoveInput(InputActions.GetMoveVector());

		if (InputActions.IsJumpJustPressed())
		{
			m_Movement.Jump();
		}
	}
}
```

- [ ] **Step 2: 编译确认**

Run: `dotnet build ProjectC.csproj`  
Expected: 成功

- [ ] **Step 3: Commit**

```bash
git add scripts/PlayerInputComponent.cs
git commit -m "添加 PlayerInputComponent 以转发玩家输入。"
```

---

### Task 4: 挂到 Player.tscn 并验收

**Files:**
- Modify: `prefabs/Player.tscn`

**Interfaces:**
- Consumes: `res://scripts/PlayerInputComponent.cs`
- Produces: 根节点下兄弟节点 `PlayerInputComponent`（`type="Node"`）

- [ ] **Step 1: 在 `Player.tscn` 注册脚本并添加节点**

在 `[ext_resource]` 区增加（`id` 若冲突则换未占用 id，例如 `id="7_input"`）：

```text
[ext_resource type="Script" path="res://scripts/PlayerInputComponent.cs" id="7_input"]
```

在 `MovementComponent` 节点之后、`RenderPrivot` 之前插入：

```text
[node name="PlayerInputComponent" type="Node" parent="."]
script = ExtResource("7_input")
```

也可用 Godot MCP / 编辑器：选中 `Player` 根 → 添加子节点 `Node`，命名 `PlayerInputComponent`，附加脚本 `res://scripts/PlayerInputComponent.cs`。若编辑器自动写入 `uid://...`，保留即可。

目标树：

```text
Player (Actor)
├── TransformComponent
├── MovementComponent
├── PlayerInputComponent
└── RenderPrivot
    └── …
```

- [ ] **Step 2: 编译 + 场景可读性检查**

Run: `dotnet build ProjectC.csproj`  
Expected: 成功

可选：用 MCP `read_scene` 打开 `prefabs/Player.tscn`，确认存在名为 `PlayerInputComponent` 的节点且脚本路径正确。

- [ ] **Step 3: 手动冒烟（可选，合入前可不留日志）**

若需确认桩被调用：临时在 `MovementComponent.SetMoveInput` / `Jump` 内加 `GD.Print`，运行主场景后按方向键与 `C`，应看到打印；验证后删除打印再提交。

- [ ] **Step 4: Commit**

```bash
git add prefabs/Player.tscn scripts/PlayerInputComponent.cs.uid
git commit -m "在 Player 场景挂载 PlayerInputComponent。"
```

说明：若 Godot 生成了 `PlayerInputComponent.cs.uid`，一并纳入本次提交。

---

## 验收对照

| Spec 验收项 | 对应任务 |
|-------------|----------|
| `InputActions` 与 Input Map 一一对应 | Task 1 |
| `Player.tscn` 绑定 `PlayerInputComponent` | Task 4 |
| 运行时可解析 Movement 并调用桩方法 | Task 2–4 |
| 新增代码无 Input Action 魔法字符串 | Task 1、3（字面量仅在 `InputActions`） |

## Spec 覆盖自检

- 兄弟直连 `../MovementComponent` → Task 3
- `_PhysicsProcess` + 零向量停步 → Task 3
- `SetMoveInput` / `Jump` 空实现 → Task 2
- 不做真实物理 / Actor 转发 / 自动化测试 → 各任务均未引入
