# 逻辑 Z 轴跳跃 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 落地逻辑 Z 轴跳跃：固定起跳冲量 + 重力落地 + 空中移速缩放；`MovementComponent` 写 `VirtualZ` / 逻辑 XY，`TransformComponent` 投影到 `Privot`。

**Architecture:** 竖直速度态放在 `MovementComponent`（`m_VerticalVelocity`）。`Jump()` 仅贴地写入 `BaseJumpForce`；`PhysicsTick` 每帧做重力积分与落地钳制，再按贴地/空中选水平速度。表现写回仍走既有 `SetLogic*` / `SetVirtualZ`。

**Tech Stack:** Godot 4.6、C# / .NET、既有 `ActorMovementConfig`、`TransformComponent`、`MapCoordinates`

**Spec:** `docs/superpowers/specs/2026-08-19-logic-z-jump-design.md`

## Global Constraints

- 文档与计划使用中文；代码标识符保持英文
- 表现写回只由 `TransformComponent` 负责；不直接改 `Actor.Position` 做跳跃/走位
- 固定冲量；无土狼、缓冲、可变跳高、二段跳
- 贴地判定：`VirtualZ <= 0` 且 `m_VerticalVelocity <= 0`
- `PhysicsTick` 输入为零也要跑竖直积分
- 不做可行走区钳制、动画/影子缩放、Enemy AI 跳跃、自动化测试
- 完成标准：`dotnet build ProjectC.csproj` 成功 + 手动跑图验收
- 保持节点名拼写 `Privot`
- 缺依赖时 `GD.PushError` 并跳过，不抛未处理异常

## 文件结构

| 文件 | 职责 |
|------|------|
| `scripts/data/ActorMovementConfig.cs` | 新增 `BaseGravity` |
| `data/actors/movement/player_default_move.tres` | 写入 `BaseGravity = 980` |
| `data/actors/movement/enemy_default_move.tres` | 写入 `BaseGravity = 980` |
| `scripts/TransformComponent.cs` | 新增 `GetVirtualZ()` |
| `scripts/MovementComponent.cs` | 竖直速度、`Jump()`、重力/落地、空中移速 |

---

### Task 1: `ActorMovementConfig.BaseGravity` 与资源

**Files:**
- Modify: `scripts/data/ActorMovementConfig.cs`
- Modify: `data/actors/movement/player_default_move.tres`
- Modify: `data/actors/movement/enemy_default_move.tres`

**Interfaces:**
- Consumes: 无
- Produces: `public float BaseGravity { get; set; }`（`[Export]`，默认 `980f`）

- [x] **Step 1: 在 `ActorMovementConfig` 增加 `BaseGravity`**

将 `scripts/data/ActorMovementConfig.cs` 改为：

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

	[Export]
	public float BaseGravity { get; set; } = 980f;
}
```

- [x] **Step 2: 更新两个 `.tres`**

`data/actors/movement/player_default_move.tres`：

```text
[gd_resource type="Resource" script_class="ActorMovementConfig" load_steps=2 format=3]

[ext_resource type="Script" path="res://scripts/data/ActorMovementConfig.cs" id="1_move"]

[resource]
script = ExtResource("1_move")
BaseMoveSpeed = 200.0
BaseJumpForce = 400.0
BaseAerialMoveSpeedScale = 0.7
BaseGravity = 980.0
```

`data/actors/movement/enemy_default_move.tres`：

```text
[gd_resource type="Resource" script_class="ActorMovementConfig" load_steps=2 format=3]

[ext_resource type="Script" path="res://scripts/data/ActorMovementConfig.cs" id="1_move"]

[resource]
script = ExtResource("1_move")
BaseMoveSpeed = 160.0
BaseJumpForce = 360.0
BaseAerialMoveSpeedScale = 0.6
BaseGravity = 980.0
```

- [x] **Step 3: 编译确认**

Run: `dotnet build ProjectC.csproj`  
Expected: 成功（或仅有与本次无关的既有警告）

- [x] **Step 4: Commit**

```bash
git add scripts/data/ActorMovementConfig.cs data/actors/movement/player_default_move.tres data/actors/movement/enemy_default_move.tres
git commit -m "ActorMovementConfig：新增 BaseGravity 并写入默认移动资源。"
```

---

### Task 2: `TransformComponent.GetVirtualZ`

**Files:**
- Modify: `scripts/TransformComponent.cs`

**Interfaces:**
- Consumes: 既有 `m_VirtualZ`、`GetLogicDepth()`
- Produces: `public virtual float GetVirtualZ()`

- [x] **Step 1: 在 `GetLogicDepth` 旁增加对称读取**

在 `scripts/TransformComponent.cs` 的 `GetLogicDepth` 之后加入：

```csharp
public virtual float GetVirtualZ()
{
	return m_VirtualZ;
}
```

完整尾部应类似：

```csharp
public virtual float GetLogicX()
{
	return m_LogicX;
}

public virtual float GetLogicDepth()
{
	return m_LogicDepth;
}

public virtual float GetVirtualZ()
{
	return m_VirtualZ;
}
```

- [x] **Step 2: 编译确认**

Run: `dotnet build ProjectC.csproj`  
Expected: 成功

- [x] **Step 3: Commit**

```bash
git add scripts/TransformComponent.cs
git commit -m "TransformComponent：新增 GetVirtualZ 对称读取。"
```

---

### Task 3: `MovementComponent` 跳跃与重力

**Files:**
- Modify: `scripts/MovementComponent.cs`

**Interfaces:**
- Consumes:
  - `ActorMovementConfig.BaseMoveSpeed` / `BaseJumpForce` / `BaseAerialMoveSpeedScale` / `BaseGravity`
  - `TransformComponent.GetLogicX()` / `GetLogicDepth()` / `GetVirtualZ()`
  - `TransformComponent.SetLogicX(float)` / `SetLogicDepth(float)` / `SetVirtualZ(float)`
- Produces:
  - `void Jump()` — 贴地时设竖直速度为 `BaseJumpForce`
  - `void PhysicsTick(double delta)` — 重力、落地、空中/贴地水平积分
  - 运行时字段 `m_VerticalVelocity`

- [x] **Step 1: 替换 `MovementComponent.cs` 为完整实现**

```csharp
using Godot;

public partial class MovementComponent : Node
{
	private ActorMovementConfig m_MovementConfig;
	private TransformComponent m_Transform;
	private Vector2 m_MoveInput = Vector2.Zero;
	private float m_VerticalVelocity;

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
		if (m_MovementConfig == null || m_Transform == null)
		{
			return;
		}

		if (!IsGrounded(m_Transform.GetVirtualZ()))
		{
			return;
		}

		m_VerticalVelocity = m_MovementConfig.BaseJumpForce;
	}

	public void PhysicsTick(double delta)
	{
		if (m_MovementConfig == null || m_Transform == null)
		{
			return;
		}

		var dt = (float)delta;
		var virtualZ = m_Transform.GetVirtualZ();

		if (!IsGrounded(virtualZ))
		{
			m_VerticalVelocity -= m_MovementConfig.BaseGravity * dt;
			virtualZ += m_VerticalVelocity * dt;
		}

		if (virtualZ <= 0f)
		{
			virtualZ = 0f;
			m_VerticalVelocity = 0f;
		}

		if (!Mathf.IsEqualApprox(virtualZ, m_Transform.GetVirtualZ()))
		{
			m_Transform.SetVirtualZ(virtualZ);
		}

		var grounded = IsGrounded(virtualZ);
		if (m_MoveInput == Vector2.Zero)
		{
			return;
		}

		var speed = m_MovementConfig.BaseMoveSpeed;
		if (!grounded)
		{
			speed *= m_MovementConfig.BaseAerialMoveSpeedScale;
		}

		var newX = m_Transform.GetLogicX() + m_MoveInput.X * speed * dt;
		var newDepth = m_Transform.GetLogicDepth() + m_MoveInput.Y * speed * dt;
		m_Transform.SetLogicX(newX);
		m_Transform.SetLogicDepth(newDepth);
	}

	private bool IsGrounded(float virtualZ)
	{
		return virtualZ <= 0f && m_VerticalVelocity <= 0f;
	}
}
```

要点（实现时对照）：

- `Jump()` 在 Input 的 `PhysicsTick` 中先于 Movement 的 `PhysicsTick` 调用，因此同帧可起跳
- 刚起跳时 `VirtualZ == 0` 但 `m_VerticalVelocity > 0` → `IsGrounded` 为 false → 本帧即积分上升
- 仅当 `VirtualZ` 相对 Transform 当前值有变化时才 `SetVirtualZ`，避免贴地静止帧无意义刷新
- 水平仍松手立刻停；空中用 `BaseAerialMoveSpeedScale`

- [x] **Step 2: 编译确认**

Run: `dotnet build ProjectC.csproj`  
Expected: 成功

- [ ] **Step 3: 手动跑图验收（对照 Spec 完成标准）**

在编辑器运行主场景 / 含 Player 的地图：

1. 贴地按 `jump`：角色精灵（`Privot`）上抛再落回；Shadow 与 Actor 根不离地
2. 滞空中再按 `jump`：不应再次上抛；落地后可再跳
3. 空中按方向键：移速明显慢于地面（约 ×0.7）；松手立刻停
4. 仅走位不跳：行为与改前贴地四向一致

- [x] **Step 4: Commit**

```bash
git add scripts/MovementComponent.cs
git commit -m "MovementComponent：实现逻辑 Z 跳跃、重力落地与空中移速。"
```

---

## Spec 覆盖对照

| Spec 要求 | 任务 |
|-----------|------|
| `BaseGravity` 默认 980 + `.tres` | Task 1 |
| `GetVirtualZ()` | Task 2 |
| `Jump()` 贴地冲量 / 空中忽略 | Task 3 |
| 重力积分 + 落地钳制 `VirtualZ=0` | Task 3 |
| 空中 `BaseAerialMoveSpeedScale` | Task 3 |
| 输入为零仍落地 | Task 3（竖直在水平 return 之前） |
| 不改 Input/Actor/Player 调度 | 无任务（已接好） |
| 无土狼/缓冲/可变高/二段跳 | 范围外，未实现 |

## 范围外（勿在本计划实现）

| 项 | 说明 |
|----|------|
| 土狼 / 缓冲 / 可变跳高 / 二段跳 | Spec 明确排除 |
| 加减速、空中惯性 | 保持瞬时速度 |
| 可行走区钳制、非零落地高度 | 始终落回 `VirtualZ = 0` |
| 跳跃动画、影子缩放 | 后续 |
| Enemy AI 主动 `Jump()` | 路径已通，本轮不接 AI |
| 自动化测试 | 以编译 + 手动跑图为准 |
