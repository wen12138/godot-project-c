# Actor 贴地四向移动 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 落地贴地四向（含斜向归一化）移动：`MovementComponent` 用 `BaseMoveSpeed` 积分逻辑坐标，`TransformComponent` 写回表现，`Actor`/`Player` 用 `PhysicsTick` 固定「输入 → 移动」顺序。

**Architecture:** `PlayerInputComponent.PhysicsTick` 只转发意图；`MovementComponent.PhysicsTick` 读配置与 `TransformComponent` 逻辑态并 `SetLogicX`/`SetLogicDepth`；表现投影仍在 `TransformComponent.UpdateVisualPosition`。`Player._PhysicsProcess` 先 Input 再 `base`（Movement），不依赖场景子节点顺序。

**Tech Stack:** Godot 4.6、C# / .NET、既有 `ActorDefinition` / `ActorMovementConfig`、`MapCoordinates` 投影

**Spec:** `docs/superpowers/specs/2026-08-11-actor-four-way-movement-design.md`

## Global Constraints

- 文档与计划使用中文；代码标识符保持英文
- 表现写回只由 `TransformComponent` 负责；组件不直接改 `Actor.Position` 做走位
- 本轮不做跳跃/重力/`VirtualZ` 变化；`Jump()` 保持空实现
- 瞬时速度 + 斜向归一化；不加加速度/摩擦力字段
- 不做可行走区域钳制、Enemy AI、自动化测试
- 完成标准：`dotnet build ProjectC.csproj` 成功 + 手动跑图验收
- 保持节点名拼写 `Privot`
- 缺依赖时 `GD.PushError` 并跳过，不抛未处理异常挡死整棵树

## 文件结构

| 文件 | 职责 |
|------|------|
| `scripts/TransformComponent.cs` | 新增 `GetLogicX()`；表现写回不变 |
| `scripts/MovementComponent.cs` | 缓存输入；`PhysicsTick` 积分逻辑 XY |
| `scripts/PlayerInputComponent.cs` | `_PhysicsProcess` → `PhysicsTick` |
| `scripts/Actor.cs` | 调度 `MovementComponent.PhysicsTick` |
| `scripts/Player.cs` | 先 Input `PhysicsTick`，再 `base` |
| `prefabs/Player.tscn` | 根脚本改为 `Player.cs` |

---

### Task 1: `TransformComponent.GetLogicX`

**Files:**
- Modify: `scripts/TransformComponent.cs`

**Interfaces:**
- Consumes: 既有 `m_LogicX`、`GetLogicDepth()`
- Produces: `public virtual float GetLogicX()`

- [ ] **Step 1: 在 `GetLogicDepth` 旁增加对称读取**

在 `scripts/TransformComponent.cs` 的 `GetVisualY` / `GetLogicDepth` 区域加入：

```csharp
public virtual float GetLogicX()
{
	return m_LogicX;
}
```

保持 `GetLogicDepth` 不变：

```csharp
public virtual float GetLogicDepth()
{
	return m_LogicDepth;
}
```

- [ ] **Step 2: 编译确认**

Run: `dotnet build ProjectC.csproj`  
Expected: 成功（或仅有与本次无关的既有警告）

- [ ] **Step 3: Commit**

```bash
git add scripts/TransformComponent.cs
git commit -m "TransformComponent：新增 GetLogicX 对称读取。"
```

---

### Task 2: `MovementComponent` 贴地积分

**Files:**
- Modify: `scripts/MovementComponent.cs`

**Interfaces:**
- Consumes:
  - `ActorMovementConfig.BaseMoveSpeed`
  - `TransformComponent.GetLogicX()` / `GetLogicDepth()` / `SetLogicX(float)` / `SetLogicDepth(float)`
- Produces:
  - `void SetMoveInput(Vector2 direction)` — 缓存；非零 `Normalized()`
  - `void PhysicsTick(double delta)` — 贴地积分
  - `void Jump()` — 仍为空

- [ ] **Step 1: 替换 `MovementComponent.cs` 为实现版**

```csharp
using Godot;

public partial class MovementComponent : Node
{
	private ActorMovementConfig m_MovementConfig;
	private TransformComponent m_Transform;
	private Vector2 m_MoveInput = Vector2.Zero;

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

		m_Transform = GetNodeOrNull<TransformComponent>("../TransformComponent");
		if (m_Transform == null)
		{
			GD.PushError($"{GetPath()}: missing sibling TransformComponent at ../TransformComponent");
		}
	}

	public void SetMoveInput(Vector2 direction)
	{
		m_MoveInput = direction == Vector2.Zero ? Vector2.Zero : direction.Normalized();
	}

	public void Jump()
	{
	}

	public void PhysicsTick(double delta)
	{
		if (m_MovementConfig == null || m_Transform == null)
		{
			return;
		}

		if (m_MoveInput == Vector2.Zero)
		{
			return;
		}

		var dt = (float)delta;
		var speed = m_MovementConfig.BaseMoveSpeed;
		var newX = m_Transform.GetLogicX() + m_MoveInput.X * speed * dt;
		var newDepth = m_Transform.GetLogicDepth() + m_MoveInput.Y * speed * dt;
		m_Transform.SetLogicX(newX);
		m_Transform.SetLogicDepth(newDepth);
	}
}
```

注意：删除空的 `_Process` / `_PhysicsProcess`，避免与 Actor 调度双跑。

- [ ] **Step 2: 编译确认**

Run: `dotnet build ProjectC.csproj`  
Expected: 成功

- [ ] **Step 3: Commit**

```bash
git add scripts/MovementComponent.cs
git commit -m "MovementComponent：实现贴地四向 PhysicsTick 积分。"
```

---

### Task 3: `PlayerInputComponent` 改为 `PhysicsTick`

**Files:**
- Modify: `scripts/PlayerInputComponent.cs`

**Interfaces:**
- Consumes: `MovementComponent.SetMoveInput` / `Jump`，`InputActions.GetMoveVector` / `IsJumpJustPressed`
- Produces: `public void PhysicsTick(double delta)`

- [ ] **Step 1: 将 `_PhysicsProcess` 迁为 `PhysicsTick`**

完整文件应为：

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

	public void PhysicsTick(double delta)
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
git commit -m "PlayerInputComponent：更新入口改为 PhysicsTick。"
```

---

### Task 4: `Actor` / `Player` 统一调度 + 场景绑定

**Files:**
- Modify: `scripts/Actor.cs`
- Modify: `scripts/Player.cs`（若尚未纳入版本库则一并添加 `scripts/Player.cs.uid`）
- Modify: `prefabs/Player.tscn`

**Interfaces:**
- Consumes: `MovementComponent.PhysicsTick`、`PlayerInputComponent.PhysicsTick`
- Produces:
  - `Actor._PhysicsProcess` → Movement tick
  - `Player._PhysicsProcess` → Input tick → `base`（Movement）

- [ ] **Step 1: 更新 `Actor.cs`**

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
		m_TransformComponent = GetNodeOrNull<TransformComponent>("TransformComponent");
		if (m_TransformComponent == null)
		{
			GD.PushError($"{GetPath()}: missing child TransformComponent");
		}

		m_MovementComponent = GetNodeOrNull<MovementComponent>("MovementComponent");
		if (m_MovementComponent == null)
		{
			GD.PushError($"{GetPath()}: missing child MovementComponent");
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		m_MovementComponent?.PhysicsTick(delta);
	}
}
```

- [ ] **Step 2: 更新 `Player.cs`**

```csharp
using Godot;

public partial class Player : Actor
{
	private PlayerInputComponent m_PlayerInputComponent;

	public override void _Ready()
	{
		base._Ready();
		m_PlayerInputComponent = GetNodeOrNull<PlayerInputComponent>("PlayerInputComponent");
		if (m_PlayerInputComponent == null)
		{
			GD.PushError($"{GetPath()}: missing child PlayerInputComponent");
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		m_PlayerInputComponent?.PhysicsTick(delta);
		base._PhysicsProcess(delta);
	}
}
```

- [ ] **Step 3: `Player.tscn` 根脚本改为 `Player.cs`**

将 `prefabs/Player.tscn` 顶部脚本资源从 `Actor.cs` 换成 `Player.cs`（保留既有 UID `uid://ddk7lans1eohn`）：

```text
[ext_resource type="Script" uid="uid://ddk7lans1eohn" path="res://scripts/Player.cs" id="1_p3xyx"]
```

根节点仍使用 `script = ExtResource("1_p3xyx")` 与 `Definition = ExtResource("8_actor_def")`，其余子节点不动。

若工作区已有未跟踪的 `scripts/Player.cs.uid`，一并纳入提交。

- [ ] **Step 4: 编译确认**

Run: `dotnet build ProjectC.csproj`  
Expected: 成功

- [ ] **Step 5: 手动跑图验收**

1. 用 Godot 打开主场景，确认存在 `MapOrigin` 且 Player 已实例化
2. 运行游戏，按上下左右：角色应沿逻辑 XY 移动；斜向不应明显快于单轴
3. 松手立刻停
4. 观察 `Shadow` 贴地、`Privot`/`Render` 不因走位产生额外高度偏移
5. 确认输出无「missing …Component」类错误（`MapContext` Origin 正常时也不应有投影错误）

- [ ] **Step 6: Commit**

```bash
git add scripts/Actor.cs scripts/Player.cs scripts/Player.cs.uid prefabs/Player.tscn
git commit -m "Actor/Player：统一 PhysicsTick 调度并绑定 Player 脚本。"
```

---

## Spec Coverage（自检）

| Spec 要求 | 任务 |
|-----------|------|
| `GetLogicX` | Task 1 |
| Movement 缓存输入 + 归一化 + `PhysicsTick` 积分 | Task 2 |
| `Jump` 空实现 | Task 2 |
| Input 改为 `PhysicsTick` | Task 3 |
| Actor 调度 Movement；Player 先 Input 再 base | Task 4 |
| `Player.tscn` 绑定 `Player.cs` | Task 4 |
| 表现仍由 Transform 写回 | 不改 `UpdateVisualPosition`（Task 1 仅只读） |
| 不做跳跃/钳制/AI/自动测试 | 全任务范围外 |

## Placeholder 扫描

无 TBD / TODO /「类似 Task N」占位；关键方法均给出完整代码。
