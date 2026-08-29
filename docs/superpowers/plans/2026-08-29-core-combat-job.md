# 核心战斗：属性、职业与位移装配 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 落地 `ActorDefinition` 的属性 + 职业：进场按 `Job.Locomotion` 生成位移、命中按结算后的攻击力扣血，默认地面走跳手感不变。

**Architecture:** `CombatAttributes` 只存基础生命/攻击；`JobDefinition` 持有位移预制体、移动配置与普攻。`Actor._Ready` Duplicate 属性、实例化 Locomotion、按类型缓存组件并初始化生命。`Player` 把 `Movement` / `Combat` 注入 `PlayerInputComponent`。`CombatComponent` 从 `Job.Attack` 读普攻，命中调用目标 `HealthComponent.TakeDamage`。

**Tech Stack:** Godot 4.6、C# / Godot.NET.Sdk 4.6.2、`[GlobalClass]` Resource、既有 `MovementComponent` / `TransformComponent` / 逻辑 AABB 判定

**Spec:** `docs/superpowers/specs/2026-08-29-core-combat-job-design.md`

## Global Constraints

- 文档与计划使用中文；代码标识符保持英文
- 全局命名空间（与现有 `scripts/` 一致），不使用额外 namespace
- 判定真相仍是逻辑三轴 AABB；不引入 `Area2D` 做命中
- 位移物理只在 `MovementComponent.PhysicsTick`；职业 Resource 不改坐标
- 走与跳不拆成两个写 Transform 的预制体
- `Actor` 只 Export `Definition`；职业是 `Definition.Job`
- `MaxHealth` / `AttackPower` 本轮 = 运行时副本的 `BaseHealth` / `BaseAttack`；不写回蓝图
- 组件不自挂 `_PhysicsProcess` 做玩法；调度仍是 Input → Movement → Hitbox → Combat → Hurtbox 调试
- 缺依赖时 `GD.PushError`（含节点路径）并跳过，不抛未处理异常，不静默套默认职业/移速
- 不做闪避/战技/大招逻辑、第二套飞行位移、StatModifier、HP UI、存档当前生命、自动化测试
- 完成标准：`dotnet build ProjectC.csproj` 成功 + 手动跑图
- 保持节点名拼写 `Privot`
- 实现时读 `godot-prompter:resource-pattern`、`godot-prompter:component-system`、`godot-prompter:scene-organization`；**不要**把 skill 里的 Area2D Hitbox 示例当本项目判定真相

## 文件结构

| 文件 | 职责 |
|------|------|
| `scripts/data/CombatAttributes.cs` | 基础生命 / 基础攻击 Resource |
| `scripts/data/JobDefinition.cs` | 职业：Locomotion 场景、Movement、Attack、预留槽 |
| `scripts/data/ActorDefinition.cs` | `Id` + `Attributes` + `Job`（删除 `Movement`） |
| `scripts/HealthComponent.cs` | 当前生命、受伤、死亡信号 |
| `scripts/Actor.cs` | Duplicate 属性、实例化位移、按类型缓存、生命初始化、`Died` |
| `scripts/MovementComponent.cs` | 读 `Job.Movement`；按类型找 Transform |
| `scripts/Player.cs` | `base._Ready` 后 `Bind` |
| `scripts/PlayerInputComponent.cs` | `Bind`；不再路径查找 |
| `scripts/CombatComponent.cs` | 读 `Job.Attack`；命中扣血 |
| `prefabs/locomotion/ground_locomotion.tscn` | 根为 `MovementComponent` 的地面位移 |
| `data/actors/attributes/*.tres` | Player / Enemy 属性 |
| `data/actors/jobs/*.tres` | Player / Enemy 职业 |
| `data/actors/player_default.tres` / `enemy_default.tres` | 改绑 Attributes + Job |
| `prefabs/Player.tscn` / `Enemy.tscn` | 去掉静态 Movement；挂 Health；Combat 不再 Export Attack |

---

### Task 1: 属性与职业 Resource 类型

**Skills:** `godot-prompter:resource-pattern`

**Files:**
- Create: `scripts/data/CombatAttributes.cs`
- Create: `scripts/data/JobDefinition.cs`

**Interfaces:**
- Consumes: 无（新类型）
- Produces:
  - `CombatAttributes`：`int BaseHealth`（默认 `100`）、`int BaseAttack`（默认 `10`）
  - `JobDefinition`：`PackedScene Locomotion`、`ActorMovementConfig Movement`、`AttackData Attack`、`PackedScene Dodge`、`PackedScene Skill`、`PackedScene Ultimate`（后三者允许 null）

- [ ] **Step 1: 创建 `scripts/data/CombatAttributes.cs`**

```csharp
using Godot;

[GlobalClass]
public partial class CombatAttributes : Resource
{
	[Export]
	public int BaseHealth { get; set; } = 100;

	[Export]
	public int BaseAttack { get; set; } = 10;
}
```

- [ ] **Step 2: 创建 `scripts/data/JobDefinition.cs`**

```csharp
using Godot;

[GlobalClass]
public partial class JobDefinition : Resource
{
	[Export]
	public PackedScene Locomotion { get; set; }

	[Export]
	public ActorMovementConfig Movement { get; set; }

	[Export]
	public AttackData Attack { get; set; }

	[Export]
	public PackedScene Dodge { get; set; }

	[Export]
	public PackedScene Skill { get; set; }

	[Export]
	public PackedScene Ultimate { get; set; }
}
```

本轮不读取 `Dodge` / `Skill` / `Ultimate`，字段必须存在以便 Inspector 配槽。

- [ ] **Step 3: 编译**

Run: `dotnet build ProjectC.csproj`

Expected: 成功（`Build succeeded`）。现有 `ActorDefinition.Movement` 尚未删除，工程仍能编过。

- [ ] **Step 4: Commit**

```bash
git add scripts/data/CombatAttributes.cs scripts/data/JobDefinition.cs
git commit -m "$(cat <<'EOF'
新增战斗属性与职业 Resource 类型。

为 Actor 定义拆出基础生命/攻击与职业槽，供后续装配位移与扣血。
EOF
)"
```

Windows PowerShell 若 HEREDOC 不可用，改用：

```powershell
git add scripts/data/CombatAttributes.cs scripts/data/JobDefinition.cs
git commit -m "新增战斗属性与职业 Resource 类型。"
```

---

### Task 2: ActorDefinition 迁移与 Movement 读 Job

**Skills:** `godot-prompter:resource-pattern`、`godot-prompter:component-system`

**Files:**
- Modify: `scripts/data/ActorDefinition.cs`
- Modify: `scripts/MovementComponent.cs`

**Interfaces:**
- Consumes: `CombatAttributes`、`JobDefinition`（Task 1）
- Produces:
  - `ActorDefinition.Attributes`、`ActorDefinition.Job`；**删除** `ActorDefinition.Movement`
  - `MovementComponent._Ready` 读取 `actor.Definition.Job.Movement`；Transform 向父 Actor 直接子节点按类型查找

- [ ] **Step 1: 替换 `scripts/data/ActorDefinition.cs` 全文**

```csharp
using Godot;

[GlobalClass]
public partial class ActorDefinition : Resource
{
	[Export]
	public string Id { get; set; } = "";

	[Export]
	public CombatAttributes Attributes { get; set; }

	[Export]
	public JobDefinition Job { get; set; }
}
```

- [ ] **Step 2: 改 `MovementComponent._Ready`**

删除对 `actor.Definition.Movement` 和 `GetNodeOrNull<TransformComponent>("../TransformComponent")` 的使用。`_Ready` 整段替换为：

```csharp
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

	if (actor.Definition.Job == null)
	{
		GD.PushError($"{GetPath()}: Actor.Definition.Job is null (Id={actor.Definition.Id})");
		return;
	}

	if (actor.Definition.Job.Movement == null)
	{
		GD.PushError($"{GetPath()}: Actor.Definition.Job.Movement is null (Id={actor.Definition.Id})");
		return;
	}

	m_MovementConfig = actor.Definition.Job.Movement;

	foreach (var child in actor.GetChildren())
	{
		if (child is TransformComponent transform)
		{
			m_Transform = transform;
			break;
		}
	}

	if (m_Transform == null)
	{
		GD.PushError($"{GetPath()}: missing TransformComponent under parent Actor");
	}
}
```

`SetMoveInput` / `Jump` / `PhysicsTick` / `IsGrounded` **不要改**。

- [ ] **Step 3: 编译**

Run: `dotnet build ProjectC.csproj`

Expected: 成功。此时 `.tres` 仍是旧字段，**不要**在编辑器里跑游戏验收走跳（会缺 Job）。

- [ ] **Step 4: Commit**

```powershell
git add scripts/data/ActorDefinition.cs scripts/MovementComponent.cs
git commit -m "将移动配置从 ActorDefinition 迁到 Job，并按类型查找 Transform。"
```

---

### Task 3: 属性 / 职业资产与地面位移预制体

**Skills:** `godot-prompter:resource-pattern`、`godot-prompter:scene-organization`

**Files:**
- Create: `prefabs/locomotion/ground_locomotion.tscn`
- Create: `data/actors/attributes/player_default_attr.tres`
- Create: `data/actors/attributes/enemy_default_attr.tres`
- Create: `data/actors/jobs/player_default_job.tres`
- Create: `data/actors/jobs/enemy_default_job.tres`
- Modify: `data/actors/player_default.tres`
- Modify: `data/actors/enemy_default.tres`

**Interfaces:**
- Consumes: `CombatAttributes`、`JobDefinition`、`ActorMovementConfig` 现有 `.tres`、`AttackData` 现有 `.tres`、`MovementComponent.cs`
- Produces: 可被 `ActorDefinition` 引用的完整默认数据；Player / Enemy 共用同一位移场景

- [ ] **Step 1: 创建 `prefabs/locomotion/ground_locomotion.tscn`**

根节点必须是挂了 `MovementComponent` 的 `Node`，不要再包容器。

```
[gd_scene load_steps=2 format=3]

[ext_resource type="Script" path="res://scripts/MovementComponent.cs" id="1_move"]

[node name="GroundLocomotion" type="Node"]
script = ExtResource("1_move")
```

- [ ] **Step 2: 创建 `data/actors/attributes/player_default_attr.tres`**

```
[gd_resource type="Resource" script_class="CombatAttributes" load_steps=2 format=3]

[ext_resource type="Script" path="res://scripts/data/CombatAttributes.cs" id="1_attr"]

[resource]
script = ExtResource("1_attr")
BaseHealth = 100
BaseAttack = 10
```

- [ ] **Step 3: 创建 `data/actors/attributes/enemy_default_attr.tres`**

与上相同，仅数值：`BaseHealth = 30`，`BaseAttack = 5`。

- [ ] **Step 4: 创建 `data/actors/jobs/player_default_job.tres`**

```
[gd_resource type="Resource" script_class="JobDefinition" load_steps=5 format=3]

[ext_resource type="Script" path="res://scripts/data/JobDefinition.cs" id="1_job"]
[ext_resource type="PackedScene" path="res://prefabs/locomotion/ground_locomotion.tscn" id="2_loco"]
[ext_resource type="Resource" path="res://data/actors/movement/player_default_move.tres" id="3_move"]
[ext_resource type="Resource" path="res://data/actors/attacks/player_melee_default.tres" id="4_atk"]

[resource]
script = ExtResource("1_job")
Locomotion = ExtResource("2_loco")
Movement = ExtResource("3_move")
Attack = ExtResource("4_atk")
```

不要给 `Dodge` / `Skill` / `Ultimate` 赋值（保持 null）。

- [ ] **Step 5: 创建 `data/actors/jobs/enemy_default_job.tres`**

与 Player 职业相同结构，但 `Movement` 指向 `res://data/actors/movement/enemy_default_move.tres`，**不要** `ext_resource` 普攻、**不要**写 `Attack =`（保持 null）。`Locomotion` 仍指向 `ground_locomotion.tscn`。`load_steps=4`。

```
[gd_resource type="Resource" script_class="JobDefinition" load_steps=4 format=3]

[ext_resource type="Script" path="res://scripts/data/JobDefinition.cs" id="1_job"]
[ext_resource type="PackedScene" path="res://prefabs/locomotion/ground_locomotion.tscn" id="2_loco"]
[ext_resource type="Resource" path="res://data/actors/movement/enemy_default_move.tres" id="3_move"]

[resource]
script = ExtResource("1_job")
Locomotion = ExtResource("2_loco")
Movement = ExtResource("3_move")
```

- [ ] **Step 6: 替换 `data/actors/player_default.tres`**

```
[gd_resource type="Resource" script_class="ActorDefinition" load_steps=4 format=3]

[ext_resource type="Script" path="res://scripts/data/ActorDefinition.cs" id="1_def"]
[ext_resource type="Resource" path="res://data/actors/attributes/player_default_attr.tres" id="2_attr"]
[ext_resource type="Resource" path="res://data/actors/jobs/player_default_job.tres" id="3_job"]

[resource]
script = ExtResource("1_def")
Id = "player_default"
Attributes = ExtResource("2_attr")
Job = ExtResource("3_job")
```

- [ ] **Step 7: 替换 `data/actors/enemy_default.tres`**

结构同上，`Id = "enemy_default"`，Attributes / Job 指向 Enemy 的 `.tres`。

- [ ] **Step 8: 编译**

Run: `dotnet build ProjectC.csproj`

Expected: 成功。Player/Enemy 场景里此时仍有静态 `MovementComponent`，走跳应仍可用（读 `Job.Movement`）。

- [ ] **Step 9: Commit**

```powershell
git add prefabs/locomotion/ground_locomotion.tscn data/actors/attributes data/actors/jobs data/actors/player_default.tres data/actors/enemy_default.tres
git commit -m "接入默认职业与属性资产，并抽出地面位移预制体。"
```

---

### Task 4: HealthComponent

**Skills:** `godot-prompter:component-system`

**Files:**
- Create: `scripts/HealthComponent.cs`

**Interfaces:**
- Consumes: 父 `Actor` 的 `GetMaxHealth()`（Task 5 才实现；本任务先写调用，编译会失败直到 Task 5 补上 Actor API——为避免半截不能编，本任务的 `InitializeFromActor` / `Heal` 调用父 Actor 上**即将**存在的方法，与 Task 5 **同一提交批次若拆开则必须先做 Task 5 的 getter**）

为保持每步可编译：本任务 Health 通过 `GetParentOrNull<Actor>()` 调用 `GetMaxHealth()`。**先做 Task 5 的 Actor 查询 API 再写 Health 会导致顺序反了。** 正确顺序改为：本任务 Health 写全；Task 5 立刻补 `GetMaxHealth`。若只提交 Task 4，编译失败。

**因此 Task 4 与 Task 5 的 Actor 公开 API 必须连续完成后再编译。** 本任务只创建 Health 文件；**不要**在本任务结束时单独 `dotnet build` 作为绿灯（下一步 Task 5 一起编）。

- Consumes: `Actor` 父节点（运行时）
- Produces:
  - `int CurrentHealth`（外部只读）
  - `bool IsDead`
  - `void InitializeFromActor()`
  - `void TakeDamage(int amount)`
  - `void Heal(int amount)`
  - 信号 `HealthChanged(int oldValue, int newValue)`、`Died`

- [ ] **Step 1: 创建 `scripts/HealthComponent.cs`**

```csharp
using Godot;

public partial class HealthComponent : Node
{
	[Signal]
	public delegate void HealthChangedEventHandler(int oldValue, int newValue);

	[Signal]
	public delegate void DiedEventHandler();

	private Actor m_Actor;

	public int CurrentHealth { get; private set; }

	public bool IsDead => CurrentHealth <= 0;

	public void InitializeFromActor()
	{
		m_Actor = GetParentOrNull<Actor>();
		if (m_Actor == null)
		{
			GD.PushError($"{GetPath()}: parent is not Actor");
			CurrentHealth = 0;
			return;
		}

		var maxHealth = m_Actor.GetMaxHealth();
		CurrentHealth = maxHealth <= 0 ? 0 : maxHealth;
	}

	public void TakeDamage(int amount)
	{
		if (amount <= 0 || IsDead)
		{
			return;
		}

		var oldValue = CurrentHealth;
		CurrentHealth = Mathf.Max(0, CurrentHealth - amount);
		EmitSignal(SignalName.HealthChanged, oldValue, CurrentHealth);
		if (CurrentHealth == 0)
		{
			EmitSignal(SignalName.Died);
		}
	}

	public void Heal(int amount)
	{
		if (amount <= 0 || IsDead || m_Actor == null)
		{
			return;
		}

		var oldValue = CurrentHealth;
		var maxHealth = m_Actor.GetMaxHealth();
		CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + amount);
		if (CurrentHealth != oldValue)
		{
			EmitSignal(SignalName.HealthChanged, oldValue, CurrentHealth);
		}
	}
}
```

不要在 `_Ready` 里读属性或调用 `InitializeFromActor`。

- [ ] **Step 2: 不单独编译、不单独提交**

立刻进入 Task 5，与 Actor API 一起编译提交。

---

### Task 5: Actor 装配、生命初始化、输入注入

**Skills:** `godot-prompter:component-system`、`godot-prompter:scene-organization`、`godot-prompter:player-controller`

**Files:**
- Modify: `scripts/Actor.cs`（全文替换）
- Modify: `scripts/Player.cs`
- Modify: `scripts/PlayerInputComponent.cs`

**Interfaces:**
- Consumes: `JobDefinition.Locomotion` / `Movement`、`CombatAttributes`、`HealthComponent`（Task 4）
- Produces:
  - `Actor.Movement` / `Health` / `Combat`（只读，Combat 可空）
  - `int GetMaxHealth()`、`int GetAttackPower()`
  - `PlayerInputComponent.Bind(MovementComponent movement, CombatComponent combat)`

- [ ] **Step 1: 替换 `scripts/Actor.cs` 全文**

```csharp
using Godot;

public partial class Actor : Node2D
{
	[Export]
	public ActorDefinition Definition { get; set; }

	private CombatAttributes m_RuntimeAttributes;
	private TransformComponent m_TransformComponent;
	private MovementComponent m_MovementComponent;
	private HealthComponent m_HealthComponent;
	private CombatComponent m_CombatComponent;
	private HitboxComponent m_HitboxComponent;
	private HurtboxComponent m_HurtboxComponent;

	public MovementComponent Movement => m_MovementComponent;

	public HealthComponent Health => m_HealthComponent;

	public CombatComponent Combat => m_CombatComponent;

	public int GetMaxHealth()
	{
		return m_RuntimeAttributes != null ? m_RuntimeAttributes.BaseHealth : 0;
	}

	public int GetAttackPower()
	{
		return m_RuntimeAttributes != null ? m_RuntimeAttributes.BaseAttack : 0;
	}

	public override void _Ready()
	{
		var attributesOk = TryDuplicateAttributes();
		var jobOk = ValidateJobForLocomotion();
		if (attributesOk && jobOk)
		{
			TrySpawnLocomotion();
		}

		m_TransformComponent = FindDirectChild<TransformComponent>();
		if (m_TransformComponent == null)
		{
			GD.PushError($"{GetPath()}: missing child TransformComponent");
		}

		m_MovementComponent = FindDirectChild<MovementComponent>();
		m_HealthComponent = FindDirectChild<HealthComponent>();
		m_HurtboxComponent = FindDirectChild<HurtboxComponent>();
		m_CombatComponent = FindDirectChild<CombatComponent>();
		m_HitboxComponent = FindDirectChild<HitboxComponent>();

		if (m_HurtboxComponent == null)
		{
			GD.PushError($"{GetPath()}: missing child HurtboxComponent");
		}

		if (m_HealthComponent == null)
		{
			GD.PushError($"{GetPath()}: missing child HealthComponent");
		}
		else
		{
			m_HealthComponent.InitializeFromActor();
			m_HealthComponent.Died += OnHealthDied;
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		m_MovementComponent?.PhysicsTick(delta);
		m_HitboxComponent?.PhysicsTick(delta);
		m_CombatComponent?.PhysicsTick(delta);
		m_HurtboxComponent?.RedrawDebug();
	}

	private bool TryDuplicateAttributes()
	{
		if (Definition == null)
		{
			GD.PushError($"{GetPath()}: Definition is null");
			return false;
		}

		if (Definition.Attributes == null)
		{
			GD.PushError($"{GetPath()}: Definition.Attributes is null (Id={Definition.Id})");
			return false;
		}

		m_RuntimeAttributes = Definition.Attributes.Duplicate() as CombatAttributes;
		if (m_RuntimeAttributes == null)
		{
			GD.PushError($"{GetPath()}: failed to Duplicate Definition.Attributes (Id={Definition.Id})");
			return false;
		}

		return true;
	}

	private bool ValidateJobForLocomotion()
	{
		if (Definition == null)
		{
			return false;
		}

		if (Definition.Job == null)
		{
			GD.PushError($"{GetPath()}: Definition.Job is null (Id={Definition.Id})");
			return false;
		}

		if (Definition.Job.Locomotion == null)
		{
			GD.PushError($"{GetPath()}: Definition.Job.Locomotion is null (Id={Definition.Id})");
			return false;
		}

		if (Definition.Job.Movement == null)
		{
			GD.PushError($"{GetPath()}: Definition.Job.Movement is null (Id={Definition.Id})");
			return false;
		}

		return true;
	}

	private void TrySpawnLocomotion()
	{
		if (FindDirectChild<MovementComponent>() != null)
		{
			GD.PushError($"{GetPath()}: unexpected static MovementComponent; skip Job.Locomotion instantiate");
			return;
		}

		var instance = Definition.Job.Locomotion.Instantiate();
		if (instance is not MovementComponent)
		{
			GD.PushError($"{GetPath()}: Job.Locomotion root is not MovementComponent");
			instance.QueueFree();
			return;
		}

		AddChild(instance);
	}

	private T FindDirectChild<T>() where T : Node
	{
		foreach (var child in GetChildren())
		{
			if (child is T match)
			{
				return match;
			}
		}

		return null;
	}

	private void OnHealthDied()
	{
		if (this is Player)
		{
			GD.Print($"{GetPath()}: player died");
			return;
		}

		QueueFree();
	}
}
```

注意：属性 Duplicate 与职业校验都要跑（避免 Attributes 缺失时吞掉 Job 报错）；两者都成功才实例化位移。随后仍缓存已有子节点并初始化 Health。

- [ ] **Step 2: 替换 `scripts/PlayerInputComponent.cs` 全文**

```csharp
using Godot;

public partial class PlayerInputComponent : Node
{
	private MovementComponent m_Movement;
	private CombatComponent m_Combat;

	public void Bind(MovementComponent movement, CombatComponent combat)
	{
		m_Movement = movement;
		m_Combat = combat;
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

删除整个 `_Ready`。未 `Bind` 时两引用为 null，`PhysicsTick` 自然 no-op。

- [ ] **Step 3: 改 `scripts/Player.cs` 的 `_Ready`**

```csharp
public override void _Ready()
{
	base._Ready();
	m_PlayerInputComponent = GetNodeOrNull<PlayerInputComponent>("PlayerInputComponent");
	if (m_PlayerInputComponent == null)
	{
		GD.PushError($"{GetPath()}: missing child PlayerInputComponent");
		return;
	}

	m_PlayerInputComponent.Bind(Movement, Combat);
}
```

`_PhysicsProcess` 不要改。

- [ ] **Step 4: 编译 Task 4 + Task 5**

Run: `dotnet build ProjectC.csproj`

Expected: 成功。此时场景还没有 `HealthComponent` 节点，运行会 `PushError: missing child HealthComponent`；静态 Movement 仍在，会 `unexpected static MovementComponent` 并继续用那一份走跳。本任务不要求手动跑图通过生命，只要求能编过。

- [ ] **Step 5: Commit（含 Task 4 的 HealthComponent.cs）**

```powershell
git add scripts/HealthComponent.cs scripts/Actor.cs scripts/Player.cs scripts/PlayerInputComponent.cs
git commit -m "由职业实例化位移，并把 Movement 注入玩家输入。"
```

---

### Task 6: 普攻改读 Job，命中扣血

**Skills:** `godot-prompter:component-system`、`godot-prompter:ability-system`（只取其「组件读结算后的攻击力」，不要实现 StatModifier）

**Files:**
- Modify: `scripts/CombatComponent.cs`

**Interfaces:**
- Consumes: 父 `Actor.Definition.Job.Attack`、`Actor.GetAttackPower()`、目标 `Actor.Health`
- Produces: 去掉 `[Export] Attack`；`OnHit` 在日志之后 `TakeDamage`

- [ ] **Step 1: 替换 `scripts/CombatComponent.cs` 全文**

```csharp
using Godot;

public partial class CombatComponent : Node
{
	private AttackData m_Attack;
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

		var actor = GetParentOrNull<Actor>();
		if (actor == null)
		{
			GD.PushError($"{GetPath()}: parent is not Actor");
			return;
		}

		if (actor.Definition?.Job == null)
		{
			GD.PushError($"{GetPath()}: Actor.Definition.Job is null");
			return;
		}

		m_Attack = actor.Definition.Job.Attack;
		if (m_Attack == null)
		{
			GD.PushError($"{GetPath()}: Job.Attack is null");
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
		if (m_Hitbox == null || m_Attack == null)
		{
			return;
		}

		if (m_Remaining > 0f)
		{
			return;
		}

		var attackId = m_NextAttackId;
		m_NextAttackId += 1;
		m_Hitbox.Activate(attackId, m_Attack.HitboxOffset, m_Attack.HitboxSize);
		m_Remaining = m_Attack.ActiveDuration;
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

		var attacker = GetParentOrNull<Actor>();
		if (attacker == null)
		{
			return;
		}

		var health = target?.Health;
		if (health == null)
		{
			return;
		}

		health.TakeDamage(attacker.GetAttackPower());
	}
}
```

`AttackData` 不加伤害字段。`GetAttackPower() <= 0` 时 `TakeDamage` 内部直接 return。

- [ ] **Step 2: 编译**

Run: `dotnet build ProjectC.csproj`

Expected: 成功。

- [ ] **Step 3: Commit**

```powershell
git add scripts/CombatComponent.cs
git commit -m "普攻改从职业读取，命中按攻击力扣血。"
```

---

### Task 7: 预制体去掉静态位移、挂上 Health

**Skills:** `godot-prompter:scene-organization`

**Files:**
- Modify: `prefabs/Player.tscn`
- Modify: `prefabs/Enemy.tscn`

**Interfaces:**
- Consumes: `HealthComponent.cs`、`ground_locomotion.tscn`（由 Actor 运行时实例化）
- Produces: 场景无 `MovementComponent` 节点；有 `HealthComponent`；Combat 无 `Attack` 属性绑定

- [ ] **Step 1: 改 `prefabs/Player.tscn`**

1. 删除 `ext_resource`：`MovementComponent.cs`（id `4_64pgi`）、`player_melee_default.tres`（id `12_atk`）。
2. 增加 Health 脚本引用（新 id，例如 `13_hp`）：`path="res://scripts/HealthComponent.cs"`。
3. 删除节点 `MovementComponent` 整块。
4. 删除 `CombatComponent` 上的 `Attack = ExtResource("12_atk")` 行。
5. 在 `TransformComponent` 节点之后插入：

```
[node name="HealthComponent" type="Node" parent="."]
script = ExtResource("13_hp")
```

`Hurtbox` / `Hitbox` / `Combat` / `PlayerInput` / `Shadow` / `Privot` 不要改。`Player` 根上 `Definition = ExtResource("8_actor_def")` 保留。

改完后 `Player.tscn` 的资源头与节点应等价于：

```
[gd_scene format=3 uid="uid://d0c82o02o7ci3"]

[ext_resource type="Script" uid="uid://ddk7lans1eohn" path="res://scripts/Player.cs" id="1_p3xyx"]
[ext_resource type="Texture2D" uid="uid://cr3y2v2ur2ny3" path="res://sprites/Player.png" id="2_wuy1y"]
[ext_resource type="Script" uid="uid://b3gamf6set2xf" path="res://scripts/TransformComponent.cs" id="3_1nynx"]
[ext_resource type="Texture2D" uid="uid://1su0dahvuixp" path="res://sprites/shadow.png" id="4_1nynx"]
[ext_resource type="Texture2D" uid="uid://86mipi2fra0q" path="res://sprites/forward_arrow.png" id="6_64pgi"]
[ext_resource type="Script" uid="uid://h4k2254ksqs3" path="res://scripts/PlayerInputComponent.cs" id="7_input"]
[ext_resource type="Resource" path="res://data/actors/player_default.tres" id="8_actor_def"]
[ext_resource type="Script" path="res://scripts/HurtboxComponent.cs" id="9_hurt"]
[ext_resource type="Script" path="res://scripts/HitboxComponent.cs" id="10_hit"]
[ext_resource type="Script" path="res://scripts/CombatComponent.cs" id="11_cbt"]
[ext_resource type="Script" path="res://scripts/HealthComponent.cs" id="13_hp"]

[node name="Player" type="Node2D" unique_id=1297045618]
script = ExtResource("1_p3xyx")
Definition = ExtResource("8_actor_def")

[node name="TransformComponent" type="Node" parent="." unique_id=1863022047]
script = ExtResource("3_1nynx")

[node name="HealthComponent" type="Node" parent="."]
script = ExtResource("13_hp")

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

[node name="PlayerInputComponent" type="Node" parent="." unique_id=1731902976]
script = ExtResource("7_input")

[node name="Shadow" type="Sprite2D" parent="." unique_id=1583956380]
texture = ExtResource("4_1nynx")

[node name="Privot" type="Node2D" parent="." unique_id=2008766164]

[node name="ForwardArrow" type="Sprite2D" parent="Privot" unique_id=482277145]
position = Vector2(21, 0)
texture = ExtResource("6_64pgi")

[node name="Render" type="Sprite2D" parent="Privot" unique_id=2131274872]
position = Vector2(0, -75)
texture = ExtResource("2_wuy1y")
```

若 Godot 保存时改写 uid / unique_id，保留编辑器生成值，不要强行改回。

- [ ] **Step 2: 改 `prefabs/Enemy.tscn`**

同样删除 `MovementComponent.cs` 的 `ext_resource` 与 `MovementComponent` 节点；增加 `HealthComponent.cs` 引用；在 `TransformComponent` 后插入 `HealthComponent` 节点。不要加 Combat / Hitbox。`Definition` 仍绑 `enemy_default.tres`。`Privot` 树不动。

- [ ] **Step 3: 编译**

Run: `dotnet build ProjectC.csproj`

Expected: 成功。

- [ ] **Step 4: 手动跑图核对（规格完成标准）**

1. 运行主场景：Player / Enemy 能四向走、能跳；输出中**没有** `unexpected static MovementComponent`。
2. 远程调试或场景树：Player 下出现名为 `GroundLocomotion` 的子节点（脚本 `MovementComponent`）。
3. 按 **J** 打中 Enemy：有命中日志；打满三次（30 / 10）后 Enemy 从场景消失。
4. 自己的攻击打不中自己。

- [ ] **Step 5: Commit**

```powershell
git add prefabs/Player.tscn prefabs/Enemy.tscn
git commit -m "预制体改为职业生成位移，并挂上生命组件。"
```

---

## 规格覆盖对照

| 规格条目 | 任务 |
|----------|------|
| `CombatAttributes` BaseHealth / BaseAttack | Task 1、3 |
| `JobDefinition` 六槽 | Task 1、3 |
| `ActorDefinition` 删 Movement，加 Attributes + Job | Task 2、3 |
| 地面 `PackedScene` 根为 MovementComponent | Task 3 |
| Movement 读 `Job.Movement`、按类型找 Transform | Task 2 |
| Duplicate 属性、GetMaxHealth / GetAttackPower 恒等 | Task 5 |
| HealthComponent 与 InitializeFromActor | Task 4、5 |
| 实例化 Locomotion、残留静态则报错不双挂 | Task 5、7 |
| Player Bind 输入 | Task 5 |
| Job.Attack、命中扣血、Enemy QueueFree、Player 只打日志 | Task 5、6 |
| 预制体去 Movement、加 Health、去 Attack Export | Task 7 |
| Dodge/Skill/Ultimate 不实例化 | Task 1（字段）+ 无读取代码 |
| 不做自动化测试 | 各任务以 `dotnet build` + Task 7 手动跑图 |
