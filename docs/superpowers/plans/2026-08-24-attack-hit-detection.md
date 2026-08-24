# 近战攻击命中判定 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 落地逻辑空间三轴 AABB 近战判定：开/关攻击盒、命中信号、同一挥击对同一目标只算一次、调试盒可见。

**Architecture:** `HurtboxComponent` 进树注册到静态 `HurtboxRegistry`；`CombatComponent.TryStartAttack` 分配 `AttackId` 并 `Hitbox.Activate`；`Actor` 在 Movement 之后先跑 Hitbox 查询、再跑 Combat 扣时。命中用 `LogicAabb` 中心+半伸展三轴闭区间重叠，不用 `Area2D`。

**Tech Stack:** Godot 4.6、C# / Godot.NET.Sdk 4.6.2、既有 `TransformComponent` / `MapCoordinates` / `InputActions`

**Spec:** `docs/superpowers/specs/2026-08-24-attack-hit-detection-design.md`

## Global Constraints

- 文档与计划使用中文；代码标识符保持英文
- 判定真相是 `LogicX` / `LogicDepth` / `VirtualZ` 的 AABB；`Area2D` 不是命中依据
- 组件不自挂 `_PhysicsProcess` 做玩法；由 `Actor` / `Player` 编排 `PhysicsTick`
- 调度顺序：Input → Movement → Hitbox 查询 → Combat 扣时 → Hurtbox 调试重绘
- 朝向用 `TransformComponent.GetFacing()`，不用 `Privot.Scale`
- 阵营用 `CombatTeam`，不用物理层
- 不做 Health / 击退 / 硬直 / 连段 / 投射物 / 动画命中帧 / Enemy AI 出招 / 自动化测试
- 完成标准：`dotnet build ProjectC.csproj` 成功 + 手动跑图
- 保持节点名拼写 `Privot`
- 缺依赖时 `GD.PushError` 并跳过，不抛未处理异常
- 实现时读 `godot-prompter:component-system` 只取其组合与信号，**不要**把 skill 里的 `Area2D` Hitbox 示例当真相

## 文件结构

| 文件 | 职责 |
|------|------|
| `scripts/LogicAabb.cs` | 逻辑 AABB：中心+半伸展、重叠、朝向偏移、本地矩形 |
| `scripts/CombatTeam.cs` | `Player` / `Enemy` |
| `scripts/HurtboxRegistry.cs` | 静态注册表 |
| `scripts/HurtboxComponent.cs` | 常开受击盒、注册、调试绘制 |
| `scripts/HitboxComponent.cs` | 开盒查询、去重、`Hit` 信号、调试绘制 |
| `scripts/CombatComponent.cs` | `AttackData`、时长、开/关盒、命中日志 |
| `scripts/data/AttackData.cs` | 近战配置 Resource |
| `data/actors/attacks/player_melee_default.tres` | Player 默认近战 |
| `scripts/InputActions.cs` | 增加 `attack` |
| `scripts/PlayerInputComponent.cs` | 按攻击调用 `TryStartAttack` |
| `scripts/Actor.cs` | 缓存组件并按规格顺序 tick |
| `project.godot` | Input Map `attack` = 物理键 J |
| `prefabs/Player.tscn` | Hurtbox + Hitbox + Combat |
| `prefabs/Enemy.tscn` | Hurtbox |

---

### Task 1: 逻辑 AABB 数学

**Skills:** `godot-prompter:math-essentials`

**Files:**
- Create: `scripts/LogicAabb.cs`
- Create: `scripts/CombatTeam.cs`

**Interfaces:**
- Consumes: `Godot.Vector3`、`Godot.Mathf`、既有 `ActorFacing`
- Produces:
  - `readonly struct LogicAabb`：`Center`、`HalfExtents`、`FromCenterSize`、`Overlaps`、`HasVolume`、`ApplyFacingOffset`、`ToActorLocalRect`
  - `enum CombatTeam { Player, Enemy }`

- [ ] **Step 1: 创建 `scripts/CombatTeam.cs`**

```csharp
public enum CombatTeam
{
	Player,
	Enemy
}
```

- [ ] **Step 2: 创建 `scripts/LogicAabb.cs`**

AABB 约定与规格一致：中心 + 半伸展；边贴边（`<=`）算命中；`Size` 任一轴 `<= 0` 则无体积。文件内注释的三组数字必须与规格「三组对照数字」一致，实现后按注释心算核对，不要另开测试工程。

```csharp
using Godot;

/// <summary>
/// 逻辑空间轴对齐盒。Vector3 含义：X=LogicX，Y=LogicDepth，Z=VirtualZ。
/// 重叠核对（半伸展已写出）：
/// 1. 攻击 (48,0,36)±(36,14,36) vs 受击 (80,0,36)±(18,12,36) → 命中
/// 2. 受击改为 (80,40,36)±(18,12,36) → 深度失败
/// 3. 受击改为 (80,0,117)±(18,12,36) → 高度失败
/// </summary>
public readonly struct LogicAabb
{
	public readonly Vector3 Center;
	public readonly Vector3 HalfExtents;

	public LogicAabb(Vector3 center, Vector3 halfExtents)
	{
		Center = center;
		HalfExtents = halfExtents;
	}

	public static LogicAabb FromCenterSize(Vector3 center, Vector3 size)
	{
		if (size.X <= 0f || size.Y <= 0f || size.Z <= 0f)
		{
			return new LogicAabb(center, Vector3.Zero);
		}

		return new LogicAabb(center, size * 0.5f);
	}

	public bool HasVolume => HalfExtents.X > 0f && HalfExtents.Y > 0f && HalfExtents.Z > 0f;

	public bool Overlaps(in LogicAabb other)
	{
		return Mathf.Abs(Center.X - other.Center.X) <= HalfExtents.X + other.HalfExtents.X
			&& Mathf.Abs(Center.Y - other.Center.Y) <= HalfExtents.Y + other.HalfExtents.Y
			&& Mathf.Abs(Center.Z - other.Center.Z) <= HalfExtents.Z + other.HalfExtents.Z;
	}

	public static Vector3 ApplyFacingOffset(Vector3 offset, ActorFacing facing)
	{
		return facing == ActorFacing.Left
			? new Vector3(-offset.X, offset.Y, offset.Z)
			: offset;
	}

	public Rect2 ToActorLocalRect(float actorLogicX, float actorLogicDepth)
	{
		var minX = Center.X - HalfExtents.X - actorLogicX;
		var maxX = Center.X + HalfExtents.X - actorLogicX;
		var minY = (Center.Y - HalfExtents.Y - actorLogicDepth) * MapCoordinates.DepthToScreenY
			- (Center.Z + HalfExtents.Z) * MapCoordinates.HeightToScreenY;
		var maxY = (Center.Y + HalfExtents.Y - actorLogicDepth) * MapCoordinates.DepthToScreenY
			- (Center.Z - HalfExtents.Z) * MapCoordinates.HeightToScreenY;
		return new Rect2(minX, minY, maxX - minX, maxY - minY);
	}
}
```

- [ ] **Step 3: 心算核对三组数字**

用 `FromCenterSize` 的半伸展（Size 的一半）对照规格：

1. `|48-80|=32 <= 36+18` 且深度/高度为 0 → 应重叠  
2. `|0-40|=40 <= 14+12=26` 为假 → 不应重叠  
3. `|36-117|=81 <= 36+36=72` 为假 → 不应重叠  

- [ ] **Step 4: 编译**

Run: `dotnet build ProjectC.csproj`  
Expected: 成功（或仅有与本次无关的既有警告）

- [ ] **Step 5: Commit（若用户要求再提交）**

```bash
git add scripts/LogicAabb.cs scripts/CombatTeam.cs
git commit -m "新增逻辑 AABB 与 CombatTeam，作为近战判定数学基础。"
```

---

### Task 2: Hurtbox 注册表

**Skills:** `godot-prompter:component-system`、`godot-prompter:scene-organization`

**Files:**
- Create: `scripts/HurtboxRegistry.cs`

**Interfaces:**
- Consumes: `HurtboxComponent` 类型名（下一任务创建脚本；本任务只引用类型，编译在 Task 3 之后才会同时通过。若希望本任务单独编译，先建一个空的 `HurtboxComponent : Node2D` 再在 Task 3 填满——按下面 Step 1 做空壳。）
- Produces:
  - `static void Register(HurtboxComponent hurtbox)`
  - `static void Unregister(HurtboxComponent hurtbox)`
  - `static List<HurtboxComponent> Snapshot()`

- [ ] **Step 1: 创建空壳 `scripts/HurtboxComponent.cs`（供注册表编译）**

```csharp
using Godot;

public partial class HurtboxComponent : Node2D
{
}
```

- [ ] **Step 2: 创建 `scripts/HurtboxRegistry.cs`**

```csharp
using System.Collections.Generic;
using Godot;

public static class HurtboxRegistry
{
	private static readonly HashSet<HurtboxComponent> Boxes = new();

	public static void Register(HurtboxComponent hurtbox)
	{
		if (hurtbox == null)
		{
			GD.PushError("HurtboxRegistry.Register: hurtbox 为 null");
			return;
		}

		Boxes.Add(hurtbox);
	}

	public static void Unregister(HurtboxComponent hurtbox)
	{
		if (hurtbox == null)
		{
			return;
		}

		Boxes.Remove(hurtbox);
	}

	public static List<HurtboxComponent> Snapshot()
	{
		return new List<HurtboxComponent>(Boxes);
	}
}
```

- [ ] **Step 3: 编译**

Run: `dotnet build ProjectC.csproj`  
Expected: 成功

- [ ] **Step 4: Commit（若用户要求再提交）**

```bash
git add scripts/HurtboxRegistry.cs scripts/HurtboxComponent.cs
git commit -m "新增 Hurtbox 静态注册表（进树注册、离树注销）。"
```

---

### Task 3: HurtboxComponent

**Skills:** `godot-prompter:component-system`、`godot-prompter:scene-organization`

**Files:**
- Modify: `scripts/HurtboxComponent.cs`（替换 Task 2 空壳）

**Interfaces:**
- Consumes:
  - `HurtboxRegistry.Register` / `Unregister`
  - `LogicAabb.FromCenterSize` / `ApplyFacingOffset` / `HasVolume`
  - `TransformComponent.GetLogicX` / `GetLogicDepth` / `GetVirtualZ` / `GetFacing`
- Produces:
  - `[Export] CombatTeam Team`
  - `[Export] Vector3 Offset` 默认 `(0, 0, 36)`
  - `[Export] Vector3 Size` 默认 `(36, 24, 72)`
  - `[Export] bool DebugDrawEnabled` 默认 `true`
  - `bool TryGetWorldAabb(out LogicAabb aabb)`
  - `Actor GetOwnerActor()`
  - `void RedrawDebug()`
  - `_EnterTree` 注册、`_ExitTree` 注销

本任务先不实现 `_Draw`（Task 7）。`RedrawDebug` 先只 `QueueRedraw`，无 Origin 时画不出来也无害。

- [ ] **Step 1: 将 `scripts/HurtboxComponent.cs` 替换为**

```csharp
using Godot;

public partial class HurtboxComponent : Node2D
{
	[Export]
	public CombatTeam Team { get; set; } = CombatTeam.Player;

	[Export]
	public Vector3 Offset { get; set; } = new(0f, 0f, 36f);

	[Export]
	public Vector3 Size { get; set; } = new(36f, 24f, 72f);

	[Export]
	public bool DebugDrawEnabled { get; set; } = true;

	private TransformComponent m_Transform;
	private Actor m_OwnerActor;

	public override void _EnterTree()
	{
		HurtboxRegistry.Register(this);
	}

	public override void _ExitTree()
	{
		HurtboxRegistry.Unregister(this);
	}

	public override void _Ready()
	{
		ZIndex = 100;
		m_OwnerActor = GetParentOrNull<Actor>();
		if (m_OwnerActor == null)
		{
			GD.PushError($"{GetPath()}: parent is not Actor");
		}

		m_Transform = GetNodeOrNull<TransformComponent>("../TransformComponent");
		if (m_Transform == null)
		{
			GD.PushError($"{GetPath()}: missing sibling TransformComponent at ../TransformComponent");
		}
	}

	public Actor GetOwnerActor()
	{
		return m_OwnerActor;
	}

	public bool TryGetWorldAabb(out LogicAabb aabb)
	{
		aabb = default;
		if (m_Transform == null)
		{
			return false;
		}

		var signed = LogicAabb.ApplyFacingOffset(Offset, m_Transform.GetFacing());
		var center = new Vector3(
			m_Transform.GetLogicX() + signed.X,
			m_Transform.GetLogicDepth() + signed.Y,
			m_Transform.GetVirtualZ() + signed.Z);
		aabb = LogicAabb.FromCenterSize(center, Size);
		return aabb.HasVolume;
	}

	public void RedrawDebug()
	{
		if (DebugDrawEnabled)
		{
			QueueRedraw();
		}
	}
}
```

- [ ] **Step 2: 编译**

Run: `dotnet build ProjectC.csproj`  
Expected: 成功

- [ ] **Step 3: Commit（若用户要求再提交）**

```bash
git add scripts/HurtboxComponent.cs
git commit -m "实现 HurtboxComponent：逻辑盒跟随 Transform，进树注册。"
```

---

### Task 4: HitboxComponent

**Skills:** `godot-prompter:component-system`、`godot-prompter:physics-system`、`godot-prompter:csharp-signals`

**Files:**
- Create: `scripts/HitboxComponent.cs`

**Interfaces:**
- Consumes:
  - `HurtboxRegistry.Snapshot()`
  - `HurtboxComponent.TryGetWorldAabb` / `GetOwnerActor` / `Team`
  - `LogicAabb.Overlaps` / `ApplyFacingOffset` / `FromCenterSize`
- Produces:
  - `[Export] CombatTeam Team`
  - `[Export] bool DebugDrawEnabled`
  - `[Signal] Hit(HurtboxComponent hurtbox)`
  - `void Activate(int attackId, Vector3 offset, Vector3 size)`
  - `void Deactivate()`
  - `void PhysicsTick(double delta)`
  - `bool IsActive` / `int CurrentAttackId`
  - `bool TryGetWorldAabb(out LogicAabb aabb)`（仅激活且有体积时成功）

实现时不要复制 skill 里的 `AreaEntered` / `Area2D` 流程。没有 `area_entered`、没有 cooldown Timer、没有 `ReceiveHit` 扣血。

- [ ] **Step 1: 创建 `scripts/HitboxComponent.cs`**

```csharp
using System.Collections.Generic;
using Godot;

public partial class HitboxComponent : Node2D
{
	[Export]
	public CombatTeam Team { get; set; } = CombatTeam.Player;

	[Export]
	public bool DebugDrawEnabled { get; set; } = true;

	[Signal]
	public delegate void HitEventHandler(HurtboxComponent hurtbox);

	private TransformComponent m_Transform;
	private Actor m_OwnerActor;
	private readonly HashSet<HurtboxComponent> m_HitThisAttack = new();
	private bool m_Active;
	private int m_AttackId;
	private Vector3 m_Offset;
	private Vector3 m_Size;

	public bool IsActive => m_Active;

	public int CurrentAttackId => m_AttackId;

	public override void _Ready()
	{
		ZIndex = 100;
		m_OwnerActor = GetParentOrNull<Actor>();
		if (m_OwnerActor == null)
		{
			GD.PushError($"{GetPath()}: parent is not Actor");
		}

		m_Transform = GetNodeOrNull<TransformComponent>("../TransformComponent");
		if (m_Transform == null)
		{
			GD.PushError($"{GetPath()}: missing sibling TransformComponent at ../TransformComponent");
		}
	}

	public void Activate(int attackId, Vector3 offset, Vector3 size)
	{
		m_AttackId = attackId;
		m_Offset = offset;
		m_Size = size;
		m_Active = true;
		m_HitThisAttack.Clear();
		QueueRedraw();
	}

	public void Deactivate()
	{
		m_Active = false;
		QueueRedraw();
	}

	public void PhysicsTick(double delta)
	{
		if (!m_Active || m_Transform == null)
		{
			return;
		}

		if (!TryGetWorldAabb(out var myAabb))
		{
			return;
		}

		foreach (var hurtbox in HurtboxRegistry.Snapshot())
		{
			if (hurtbox == null || !GodotObject.IsInstanceValid(hurtbox))
			{
				continue;
			}

			var targetActor = hurtbox.GetOwnerActor();
			if (targetActor == null || targetActor == m_OwnerActor)
			{
				continue;
			}

			if (hurtbox.Team == Team)
			{
				continue;
			}

			if (!hurtbox.TryGetWorldAabb(out var theirAabb))
			{
				continue;
			}

			if (!myAabb.Overlaps(theirAabb))
			{
				continue;
			}

			if (!m_HitThisAttack.Add(hurtbox))
			{
				continue;
			}

			EmitSignal(SignalName.Hit, hurtbox);
		}

		if (DebugDrawEnabled)
		{
			QueueRedraw();
		}
	}

	public bool TryGetWorldAabb(out LogicAabb aabb)
	{
		aabb = default;
		if (!m_Active || m_Transform == null)
		{
			return false;
		}

		var signed = LogicAabb.ApplyFacingOffset(m_Offset, m_Transform.GetFacing());
		var center = new Vector3(
			m_Transform.GetLogicX() + signed.X,
			m_Transform.GetLogicDepth() + signed.Y,
			m_Transform.GetVirtualZ() + signed.Z);
		aabb = LogicAabb.FromCenterSize(center, m_Size);
		return aabb.HasVolume;
	}
}
```

- [ ] **Step 2: 编译**

Run: `dotnet build ProjectC.csproj`  
Expected: 成功。`PhysicsTick` 的 `delta` 本轮可以不用；若编译器警告 unused，保留参数以与其它 `PhysicsTick(double delta)` 签名一致，不要删掉。

- [ ] **Step 3: Commit（若用户要求再提交）**

```bash
git add scripts/HitboxComponent.cs
git commit -m "实现 HitboxComponent：逻辑 AABB 查询、挥击去重与 Hit 信号。"
```

---

### Task 5: 攻击输入与一次近战开盒

**Skills:** `godot-prompter:input-handling`、`godot-prompter:resource-pattern`、`godot-prompter:component-system`、`godot-prompter:csharp-signals`

**Files:**
- Create: `scripts/data/AttackData.cs`
- Create: `data/actors/attacks/player_melee_default.tres`
- Create: `scripts/CombatComponent.cs`
- Modify: `scripts/InputActions.cs`
- Modify: `scripts/PlayerInputComponent.cs`
- Modify: `project.godot`（`[input]` 段）

**Interfaces:**
- Consumes:
  - `HitboxComponent.Activate(int, Vector3, Vector3)` / `Deactivate()` / `Hit` / `CurrentAttackId`
  - `Input.IsActionJustPressed`
- Produces:
  - `AttackData.ActiveDuration` / `HitboxOffset` / `HitboxSize`
  - `CombatComponent.TryStartAttack()` / `PhysicsTick(double delta)` / `IsAttacking`
  - `InputActions.Attack = "attack"`
  - `InputActions.IsAttackJustPressed()`
  - Input Map 动作 `attack`，默认物理键 J（`physical_keycode=74`）

本任务结束后 `TryStartAttack` 已能 `Activate`，但时长扣减要等 Task 6 由 `Actor` 调用 `Combat.PhysicsTick`。Input 仍走既有 `PhysicsTick` 轮询（与跳跃相同），不要改成 `_UnhandledInput`。

- [ ] **Step 1: 创建 `scripts/data/AttackData.cs`**

```csharp
using Godot;

[GlobalClass]
public partial class AttackData : Resource
{
	[Export]
	public float ActiveDuration { get; set; } = 0.2f;

	[Export]
	public Vector3 HitboxOffset { get; set; } = new(48f, 0f, 36f);

	[Export]
	public Vector3 HitboxSize { get; set; } = new(72f, 28f, 72f);
}
```

- [ ] **Step 2: 创建 `data/actors/attacks/player_melee_default.tres`**

```text
[gd_resource type="Resource" script_class="AttackData" load_steps=2 format=3]

[ext_resource type="Script" path="res://scripts/data/AttackData.cs" id="1_atk"]

[resource]
script = ExtResource("1_atk")
ActiveDuration = 0.2
HitboxOffset = Vector3(48, 0, 36)
HitboxSize = Vector3(72, 28, 72)
```

- [ ] **Step 3: 创建 `scripts/CombatComponent.cs`**

```csharp
using Godot;

public partial class CombatComponent : Node
{
	[Export]
	public AttackData Attack { get; set; }

	private HitboxComponent m_Hitbox;
	private float m_Remaining;
	private int m_NextAttackId = 1;

	public bool IsAttacking => m_Remaining > 0f;

	public override void _Ready()
	{
		m_Hitbox = GetNodeOrNull<HitboxComponent>("../HitboxComponent");
		if (m_Hitbox == null)
		{
			GD.PushError($"{GetPath()}: missing sibling HitboxComponent at ../HitboxComponent");
			return;
		}

		if (Attack == null)
		{
			GD.PushError($"{GetPath()}: Attack is null");
			return;
		}

		m_Hitbox.Hit += OnHit;
	}

	public override void _ExitTree()
	{
		if (m_Hitbox != null)
		{
			m_Hitbox.Hit -= OnHit;
		}
	}

	public void TryStartAttack()
	{
		if (m_Hitbox == null || Attack == null)
		{
			return;
		}

		if (m_Remaining > 0f)
		{
			return;
		}

		var attackId = m_NextAttackId;
		m_NextAttackId += 1;
		m_Hitbox.Activate(attackId, Attack.HitboxOffset, Attack.HitboxSize);
		m_Remaining = Attack.ActiveDuration;
	}

	public void PhysicsTick(double delta)
	{
		if (m_Hitbox == null || m_Remaining <= 0f)
		{
			return;
		}

		m_Remaining -= (float)delta;
		if (m_Remaining <= 0f)
		{
			m_Remaining = 0f;
			m_Hitbox.Deactivate();
		}
	}

	private void OnHit(HurtboxComponent hurtbox)
	{
		var target = hurtbox.GetOwnerActor();
		var name = target != null ? target.Name.ToString() : hurtbox.Name.ToString();
		GD.Print($"CombatComponent: hit {name} attackId={m_Hitbox.CurrentAttackId}");
	}
}
```

- [ ] **Step 4: 更新 `scripts/InputActions.cs` 为**

```csharp
using Godot;

public static class InputActions
{
	public const string MoveUp = "move_up";
	public const string MoveDown = "move_down";
	public const string MoveLeft = "move_left";
	public const string MoveRight = "move_right";
	public const string Jump = "jump";
	public const string Attack = "attack";

	public static Vector2 GetMoveVector()
	{
		return Input.GetVector(MoveLeft, MoveRight, MoveUp, MoveDown);
	}

	public static bool IsJumpJustPressed()
	{
		return Input.IsActionJustPressed(Jump);
	}

	public static bool IsAttackJustPressed()
	{
		return Input.IsActionJustPressed(Attack);
	}
}
```

其它文件禁止再写 `"attack"` 字符串。

- [ ] **Step 5: 更新 `scripts/PlayerInputComponent.cs` 为**

```csharp
using Godot;

public partial class PlayerInputComponent : Node
{
	private MovementComponent m_Movement;
	private CombatComponent m_Combat;

	public override void _Ready()
	{
		m_Movement = GetNodeOrNull<MovementComponent>("../MovementComponent");
		if (m_Movement == null)
		{
			GD.PushError("PlayerInputComponent: missing sibling MovementComponent at ../MovementComponent");
		}

		m_Combat = GetNodeOrNull<CombatComponent>("../CombatComponent");
		if (m_Combat == null)
		{
			GD.PushError("PlayerInputComponent: missing sibling CombatComponent at ../CombatComponent");
		}
	}

	public void PhysicsTick(double delta)
	{
		if (m_Movement != null)
		{
			m_Movement.SetMoveInput(InputActions.GetMoveVector());

			if (InputActions.IsJumpJustPressed())
			{
				m_Movement.Jump();
			}
		}

		if (m_Combat != null && InputActions.IsAttackJustPressed())
		{
			m_Combat.TryStartAttack();
		}
	}
}
```

- [ ] **Step 6: 在 `project.godot` 的 `[input]` 段、`jump={...}` 之前插入 `attack`**

与现有 `jump` 条目同格式，物理键 J = `74`：

```text
attack={
"deadzone": 0.5,
"events": [Object(InputEventKey,"resource_local_to_scene":false,"resource_name":"","device":-1,"window_id":0,"alt_pressed":false,"shift_pressed":false,"ctrl_pressed":false,"meta_pressed":false,"pressed":false,"keycode":0,"physical_keycode":74,"key_label":0,"unicode":0,"location":0,"echo":false,"script":null)
]
}
```

插完后 `[input]` 仍包含原有 `jump` / `move_*`，不要删改它们。

- [ ] **Step 7: 编译**

Run: `dotnet build ProjectC.csproj`  
Expected: 成功

- [ ] **Step 8: Commit（若用户要求再提交）**

```bash
git add scripts/data/AttackData.cs data/actors/attacks/player_melee_default.tres scripts/CombatComponent.cs scripts/InputActions.cs scripts/PlayerInputComponent.cs project.godot
git commit -m "接入攻击输入与 CombatComponent：按 J 开启一次近战攻击盒。"
```

---

### Task 6: Actor 调度接入 PhysicsTick

**Skills:** `godot-prompter:scene-organization`、`godot-prompter:component-system`

**Files:**
- Modify: `scripts/Actor.cs`
- Modify: `scripts/Player.cs`（只读确认，默认不改）

**Interfaces:**
- Consumes:
  - `MovementComponent.PhysicsTick(double)`
  - `HitboxComponent.PhysicsTick(double)`
  - `CombatComponent.PhysicsTick(double)`
  - `HurtboxComponent.RedrawDebug()`
- Produces:
  - `Actor._PhysicsProcess` 顺序：Movement → Hitbox → Combat → Hurtbox 调试
  - Hurtbox 缺失：`PushError`
  - Combat / Hitbox 缺失：合法（Enemy），跳过 tick

`Player.cs` 必须保持：

```csharp
m_PlayerInputComponent?.PhysicsTick(delta);
base._PhysicsProcess(delta);
```

不要把 Combat 挪到 `Player` 里、也不要让组件自挂 `_PhysicsProcess`。

- [ ] **Step 1: 将 `scripts/Actor.cs` 替换为**

```csharp
using Godot;

public partial class Actor : Node2D
{
	[Export]
	public ActorDefinition Definition { get; set; }

	private TransformComponent m_TransformComponent;
	private MovementComponent m_MovementComponent;
	private CombatComponent m_CombatComponent;
	private HitboxComponent m_HitboxComponent;
	private HurtboxComponent m_HurtboxComponent;

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

		m_HurtboxComponent = GetNodeOrNull<HurtboxComponent>("HurtboxComponent");
		if (m_HurtboxComponent == null)
		{
			GD.PushError($"{GetPath()}: missing child HurtboxComponent");
		}

		m_CombatComponent = GetNodeOrNull<CombatComponent>("CombatComponent");
		m_HitboxComponent = GetNodeOrNull<HitboxComponent>("HitboxComponent");
	}

	public override void _PhysicsProcess(double delta)
	{
		m_MovementComponent?.PhysicsTick(delta);
		m_HitboxComponent?.PhysicsTick(delta);
		m_CombatComponent?.PhysicsTick(delta);
		m_HurtboxComponent?.RedrawDebug();
	}
}
```

- [ ] **Step 2: 确认 `scripts/Player.cs` 仍为**

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

若文件已是上述内容，不要改。

- [ ] **Step 3: 编译**

Run: `dotnet build ProjectC.csproj`  
Expected: 成功

- [ ] **Step 4: Commit（若用户要求再提交）**

```bash
git add scripts/Actor.cs
git commit -m "Actor 在移动之后调度 Hitbox 查询与 Combat 开盒时长。"
```

---

### Task 7: 调试绘制

**Skills:** `godot-prompter:scene-organization`、`godot-prompter:2d-essentials`

**Files:**
- Modify: `scripts/HurtboxComponent.cs`（追加 `_Draw`）
- Modify: `scripts/HitboxComponent.cs`（追加 `_Draw`）

**Interfaces:**
- Consumes:
  - `LogicAabb.ToActorLocalRect(float, float)`
  - `MapContext.Instance.HasOrigin`
  - `TryGetWorldAabb`
- Produces:
  - Hurtbox：绿色半透明填充 + 描边
  - 激活中的 Hitbox：红色半透明填充 + 描边
  - 无 Origin / `DebugDrawEnabled==false` / 无体积：不画

不要添加 `Area2D`。投影常量只读 `MapCoordinates`。

- [ ] **Step 1: 在 `scripts/HurtboxComponent.cs` 的 `RedrawDebug` 方法之后追加**

```csharp
	public override void _Draw()
	{
		if (!DebugDrawEnabled)
		{
			return;
		}

		if (MapContext.Instance == null || !MapContext.Instance.HasOrigin)
		{
			return;
		}

		if (m_Transform == null || !TryGetWorldAabb(out var aabb))
		{
			return;
		}

		var rect = aabb.ToActorLocalRect(m_Transform.GetLogicX(), m_Transform.GetLogicDepth());
		DrawRect(rect, new Color(0.2f, 0.85f, 0.35f, 0.15f), filled: true);
		DrawRect(rect, new Color(0.2f, 0.85f, 0.35f, 0.9f), filled: false, width: 2f);
	}
```

追加后文件末尾 `}` 仍只关闭类。`ZIndex = 100` 已在 `_Ready` 中设置。

- [ ] **Step 2: 在 `scripts/HitboxComponent.cs` 的 `TryGetWorldAabb` 方法之后追加**

```csharp
	public override void _Draw()
	{
		if (!DebugDrawEnabled || !m_Active)
		{
			return;
		}

		if (MapContext.Instance == null || !MapContext.Instance.HasOrigin)
		{
			return;
		}

		if (m_Transform == null || !TryGetWorldAabb(out var aabb))
		{
			return;
		}

		var rect = aabb.ToActorLocalRect(m_Transform.GetLogicX(), m_Transform.GetLogicDepth());
		DrawRect(rect, new Color(0.95f, 0.2f, 0.2f, 0.2f), filled: true);
		DrawRect(rect, new Color(0.95f, 0.2f, 0.2f, 0.95f), filled: false, width: 2f);
	}
```

未激活时 `_Draw` 直接 return，因此 `Deactivate` 里的 `QueueRedraw` 会清掉红盒。

- [ ] **Step 3: 编译**

Run: `dotnet build ProjectC.csproj`  
Expected: 成功

- [ ] **Step 4: Commit（若用户要求再提交）**

```bash
git add scripts/HurtboxComponent.cs scripts/HitboxComponent.cs
git commit -m "将逻辑 Hurtbox/Hitbox 投影为屏幕调试矩形。"
```

---

### Task 8: 预制体接线与手动验收

**Skills:** `godot-prompter:scene-organization`、`godot-prompter:component-system`、`godot-prompter:resource-pattern`

**Files:**
- Modify: `prefabs/Player.tscn`
- Modify: `prefabs/Enemy.tscn`

**Interfaces:**
- Consumes: Task 3–7 的脚本与 `player_melee_default.tres`
- Produces: Player 可出招、Enemy 可挨打、调试盒可见

节点必须是 `Actor` 直下兄弟，不要挂到 `Privot` 下。Enemy **不要**加 Combat / Hitbox。

- [ ] **Step 1: 编辑 `prefabs/Player.tscn`**

在现有 `ext_resource` 列表末尾增加：

```text
[ext_resource type="Script" path="res://scripts/HurtboxComponent.cs" id="9_hurt"]
[ext_resource type="Script" path="res://scripts/HitboxComponent.cs" id="10_hit"]
[ext_resource type="Script" path="res://scripts/CombatComponent.cs" id="11_cbt"]
[ext_resource type="Resource" path="res://data/actors/attacks/player_melee_default.tres" id="12_atk"]
```

在 `MovementComponent` 节点块之后、`PlayerInputComponent` 节点块之前插入：

```text
[node name="HurtboxComponent" type="Node2D" parent="."]
script = ExtResource("9_hurt")
Team = 0
Offset = Vector3(0, 0, 36)
Size = Vector3(36, 24, 72)
DebugDrawEnabled = true

[node name="HitboxComponent" type="Node2D" parent="."]
script = ExtResource("10_hit")
Team = 0
DebugDrawEnabled = true

[node name="CombatComponent" type="Node" parent="."]
script = ExtResource("11_cbt")
Attack = ExtResource("12_atk")
```

`Team = 0` 对应 `CombatTeam.Player`。不要改 `Privot` / `Shadow` / `Definition` 既有条目。

- [ ] **Step 2: 编辑 `prefabs/Enemy.tscn`**

在现有 `ext_resource` 列表末尾增加：

```text
[ext_resource type="Script" path="res://scripts/HurtboxComponent.cs" id="9_hurt"]
```

在 `MovementComponent` 节点块之后、`Shadow` 节点块之前插入：

```text
[node name="HurtboxComponent" type="Node2D" parent="."]
script = ExtResource("9_hurt")
Team = 1
Offset = Vector3(0, 0, 36)
Size = Vector3(36, 24, 72)
DebugDrawEnabled = true
```

`Team = 1` 对应 `CombatTeam.Enemy`。不要给 Enemy 加 Hitbox 或 Combat。

- [ ] **Step 3: 编译**

Run: `dotnet build ProjectC.csproj`  
Expected: 成功

- [ ] **Step 4: 手动跑图验收**

用编辑器打开主场景（含 `MapOrigin`、已摆好的 Player 与至少一名 Enemy），运行游戏。

1. **开盒可见**：Player 贴地按 **J**。身前出现红色调试矩形，约 0.2 秒后消失。脚下/身上有绿色 Hurtbox。输出窗口无 `missing child HurtboxComponent` / `Attack is null`。
2. **打中一次**：走到 Enemy 同一条巷、水平距离大约一个身位以内，贴地按 J。输出恰好一行  
   `CombatComponent: hit Enemy attackId=1`  
   （若实例名不是 `Enemy`，以场景树实例名为准。）同一挥击内左右移动使盒子反复重叠，**不得**再打出第二行。
3. **下一挥可再中**：红盒消失后再按 J，应再出现一行且 `attackId` 递增。
4. **深度不中**：用向下/向上把 Player 与 Enemy 拉到明显不同巷（绿色盒在纵深上错开、几乎不叠），贴地挥击。不应出现新的 hit 日志。
5. **高度不中**：Enemy 贴地不动。Player 贴地起跳（**C**），在接近顶点时按 J。不应命中。也可让 Player 贴地挥击、但先把 Enemy 挪到空中（若不便，只做 Player 跳起挥击即可）。
6. **朝左**：走到 Enemy 右侧，面向左（左移一下）再按 J，红盒出现在左侧且仍能命中。
7. **自己不打自己**：贴地挥击、附近无敌人时，不应出现 hit 自己的日志。
8. **移动未被锁**：挥击过程中 WASD/方向键仍能走（本轮不硬直）。

失败时先看：Input Map 是否真有 `attack`；Combat 是否绑定了 `.tres`；Enemy `Team` 是否为 `1`；Hitbox 是否排在 Combat 扣时之前（Task 6）。

- [ ] **Step 5: Commit（若用户要求再提交）**

```bash
git add prefabs/Player.tscn prefabs/Enemy.tscn
git commit -m "Player/Enemy 预制体挂上逻辑 Hurtbox/Hitbox，供近战判定验收。"
```

---

## 规格覆盖对照

| 规格条目 | 任务 |
|----------|------|
| `LogicAabb` 中心+半伸展、闭区间、Facing 翻 X | Task 1 |
| Hurtbox 注册表进/离树 | Task 2、Task 3 |
| Hurtbox 跟随 Transform | Task 3 |
| Hitbox Activate/去重/`Hit` 信号 | Task 4 |
| `AttackData`、J 键、`TryStartAttack`、挥击中忽略 | Task 5 |
| Input → Movement → Hitbox → Combat | Task 6 |
| 无 Origin 跳过绘制；绿/红调试盒 | Task 7 |
| Player 出招、Enemy 挨打、预制体不挂 `Privot` 下 | Task 8 |
| 不做 Health/击退/硬直/连段/投射物/AI/自动测试 | 全任务未包含 |
| 修订「HurtBox 挂 Privot」 | 规格正文；实现按 Actor 直下组件 |

## 自检

- 无 TBD / TODO /「类似 Task N」/「加上错误处理」空话；缺 Transform、缺 Combat、自己打自己、无 Origin 的行为均有具体代码。
- `Activate` / `TryStartAttack` / `PhysicsTick` / `Hit` 在后续任务中的签名与 Task 4–5 一致。
- `ToActorLocalRect` 在 Task 1 一次定义，Task 7 只调用。
- 本计划不执行 `git commit`，除非用户另行要求。
