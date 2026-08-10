# Actor 定义与移动配置实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 落地 `ActorMovementConfig` / `ActorDefinition` Resource、示例 `.tres`，并让 `MovementComponent` 从父 `Actor` 只读缓存移动配置。

**Architecture:** 移动手感独立为 `ActorMovementConfig`；`ActorDefinition`（含 `Id` + `Movement` 引用）作为 Actor 表行。`Actor` Export 挂 Definition；`MovementComponent._Ready` 向父节点拉取并校验。本轮不实现位移/跳跃物理，不做自动测试。

**Tech Stack:** Godot 4.6、C# / .NET、`[GlobalClass]` Resource、`.tres` 资产

**Spec:** `docs/superpowers/specs/2026-08-10-actor-definition-movement-config-design.md`

## Global Constraints

- 文档与计划使用中文；代码标识符保持英文
- 全局命名空间（与现有 `scripts/` 一致），不使用 `ProjectC.Data`
- 配置只读共享，不对 Definition / Movement `Duplicate()`
- 不接存档层；不写 `ActorCatalog`；不实现移动物理
- 不做自动化测试；以 `dotnet build ProjectC.csproj` 成功为完成标准
- 缺 Definition / Movement 时 `GD.PushError`，不静默回退默认数值

## 文件结构

| 文件 | 职责 |
|------|------|
| `scripts/data/ActorMovementConfig.cs` | 移动手感 Resource |
| `scripts/data/ActorDefinition.cs` | Actor 定义行 Resource |
| `data/actors/movement/*.tres` | 示例移动配置 |
| `data/actors/*.tres` | 示例 Actor 定义 |
| `scripts/Actor.cs` | Export `Definition` |
| `scripts/MovementComponent.cs` | 拉取并缓存 `MovementConfig` |
| `prefabs/Player.tscn` / `Enemy.tscn` | 绑定示例 Definition |

---

### Task 1: Resource 类型

**Files:**
- Create: `scripts/data/ActorMovementConfig.cs`
- Create: `scripts/data/ActorDefinition.cs`

**Interfaces:**
- Produces:
  - `ActorMovementConfig`：`BaseMoveSpeed` / `BaseJumpForce` / `BaseAerialMoveSpeedScale`（默认 `200` / `400` / `0.7`）
  - `ActorDefinition`：`Id`（`string`）、`Movement`（`ActorMovementConfig`）

- [ ] **Step 1: 创建 `ActorMovementConfig.cs`**

```csharp
using Godot;

[GlobalClass]
public partial class ActorMovementConfig : Resource
{
	[Export]
	public float BaseMoveSpeed { get; set; } = 200f;

	[Export]
	public float BaseJumpForce { get; set; } = 400f;

	[Export]
	public float BaseAerialMoveSpeedScale { get; set; } = 0.7f;
}
```

- [ ] **Step 2: 创建 `ActorDefinition.cs`**

```csharp
using Godot;

[GlobalClass]
public partial class ActorDefinition : Resource
{
	[Export]
	public string Id { get; set; } = "";

	[Export]
	public ActorMovementConfig Movement { get; set; }
}
```

- [ ] **Step 3: 编译确认**

Run: `dotnet build ProjectC.csproj`  
Expected: 成功（或仅有与本次无关的既有警告）

- [ ] **Step 4: Commit**

```bash
git add scripts/data/ActorMovementConfig.cs scripts/data/ActorDefinition.cs
git commit -m "添加 ActorMovementConfig 与 ActorDefinition Resource 类型。"
```

---

### Task 2: 示例 `.tres` 资产

**Files:**
- Create: `data/actors/movement/player_default_move.tres`
- Create: `data/actors/movement/enemy_default_move.tres`
- Create: `data/actors/player_default.tres`
- Create: `data/actors/enemy_default.tres`

**Interfaces:**
- Consumes: Task 1 的两个 Resource 脚本
- Produces: `Id` 分别为 `player_default` / `enemy_default` 的 Definition，及其独立 Movement `.tres`

- [ ] **Step 1: 创建目录与移动配置**

创建 `data/actors/movement/player_default_move.tres`：

```text
[gd_resource type="Resource" script_class="ActorMovementConfig" load_steps=2 format=3]

[ext_resource type="Script" path="res://scripts/data/ActorMovementConfig.cs" id="1_move"]

[resource]
script = ExtResource("1_move")
BaseMoveSpeed = 200.0
BaseJumpForce = 400.0
BaseAerialMoveSpeedScale = 0.7
```

创建 `data/actors/movement/enemy_default_move.tres`（可先用相同数值，便于日后区分）：

```text
[gd_resource type="Resource" script_class="ActorMovementConfig" load_steps=2 format=3]

[ext_resource type="Script" path="res://scripts/data/ActorMovementConfig.cs" id="1_move"]

[resource]
script = ExtResource("1_move")
BaseMoveSpeed = 160.0
BaseJumpForce = 360.0
BaseAerialMoveSpeedScale = 0.6
```

- [ ] **Step 2: 创建 ActorDefinition**

创建 `data/actors/player_default.tres`：

```text
[gd_resource type="Resource" script_class="ActorDefinition" load_steps=3 format=3]

[ext_resource type="Script" path="res://scripts/data/ActorDefinition.cs" id="1_def"]
[ext_resource type="Resource" path="res://data/actors/movement/player_default_move.tres" id="2_move"]

[resource]
script = ExtResource("1_def")
Id = "player_default"
Movement = ExtResource("2_move")
```

创建 `data/actors/enemy_default.tres`：

```text
[gd_resource type="Resource" script_class="ActorDefinition" load_steps=3 format=3]

[ext_resource type="Script" path="res://scripts/data/ActorDefinition.cs" id="1_def"]
[ext_resource type="Resource" path="res://data/actors/movement/enemy_default_move.tres" id="2_move"]

[resource]
script = ExtResource("1_def")
Id = "enemy_default"
Movement = ExtResource("2_move")
```

- [ ] **Step 3: Commit**

```bash
git add data/actors/
git commit -m "添加 Player/Enemy 默认 ActorDefinition 与移动配置资产。"
```

---

### Task 3: Actor Export 与 MovementComponent 接线

**Files:**
- Modify: `scripts/Actor.cs`
- Modify: `scripts/MovementComponent.cs`
- Modify: `prefabs/Player.tscn`
- Modify: `prefabs/Enemy.tscn`

**Interfaces:**
- Consumes: `ActorDefinition`、`ActorMovementConfig`
- Produces:
  - `Actor.Definition`（`[Export] ActorDefinition`）
  - `MovementComponent.MovementConfig`（只读，`ActorMovementConfig`，可能为 null）

- [ ] **Step 1: 修改 `Actor.cs`**

在现有字段旁增加 Export（保留原有 Transform/Movement 查找逻辑）：

```csharp
using Godot;

public partial class Actor : Node2D
{
	[Export]
	public ActorDefinition Definition { get; set; }

	private TransformComponent m_TransformComponent;
	private MovementComponent m_MovementComponent;

	public override void _Ready()
	{
		m_TransformComponent = GetNode<TransformComponent>("TransformComponent");
		m_MovementComponent = GetNode<MovementComponent>("MovementComponent");
	}

	public override void _PhysicsProcess(double delta)
	{
	}
}
```

- [ ] **Step 2: 修改 `MovementComponent.cs`**

```csharp
using Godot;

public partial class MovementComponent : Node
{
	private ActorMovementConfig m_MovementConfig;

	public ActorMovementConfig MovementConfig => m_MovementConfig;

	public override void _Ready()
	{
		var actor = GetParentOrNull<Actor>();
		if (actor == null)
		{
			GD.PushError($"{GetPath()}: parent is not Actor");
			return;
		}

		if (actor.Definition == null)
		{
			GD.PushError($"{GetPath()}: Actor.Definition is null");
			return;
		}

		if (actor.Definition.Movement == null)
		{
			GD.PushError($"{GetPath()}: Actor.Definition.Movement is null (Id={actor.Definition.Id})");
			return;
		}

		m_MovementConfig = actor.Definition.Movement;
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

- [ ] **Step 3: 绑定 prefab**

在 `prefabs/Player.tscn` 增加：

```text
[ext_resource type="Resource" path="res://data/actors/player_default.tres" id="8_actor_def"]
```

并将根节点改为（保留其余子节点不变）：

```text
[node name="Player" type="Node2D" unique_id=1297045618]
script = ExtResource("1_p3xyx")
Definition = ExtResource("8_actor_def")
```

在 `prefabs/Enemy.tscn` 增加：

```text
[ext_resource type="Resource" path="res://data/actors/enemy_default.tres" id="8_actor_def"]
```

并将根节点改为：

```text
[node name="Enemy" type="Node2D" unique_id=1297045618]
script = ExtResource("1_duu5m")
Definition = ExtResource("8_actor_def")
```

说明：`id` 字符串只需在该 `.tscn` 内唯一；若手改冲突，改用未占用的 id。Godot 打开场景后可能自动补 `uid=`，允许一并提交。

- [ ] **Step 4: 编译确认**

Run: `dotnet build ProjectC.csproj`  
Expected: 成功

- [ ] **Step 5: Commit**

```bash
git add scripts/Actor.cs scripts/MovementComponent.cs prefabs/Player.tscn prefabs/Enemy.tscn
git commit -m "Actor 绑定 Definition，MovementComponent 缓存移动配置。"
```

---

## Spec 覆盖自检

| Spec 要求 | 对应 Task |
|-----------|-----------|
| `ActorMovementConfig` 三字段与默认值 | Task 1 |
| `ActorDefinition`（Id + Movement） | Task 1 |
| 示例 `.tres` 与目录布局 | Task 2 |
| Actor Export Definition | Task 3 |
| MovementComponent 拉取/校验/缓存 | Task 3 |
| Player/Enemy prefab 绑定 | Task 3 |
| 不实现移动物理 / 不做自动测试 | 全计划；仅 `dotnet build` |
| 与存档层分离 | 未改 `Save*` |
