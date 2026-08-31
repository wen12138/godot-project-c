# 技能定义：统一招式与效果 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把普攻、战技、大招收成同一套 `SkillDefinition` + `TryActivate`：先无损迁移现有 J/X 键挥击，再落地当帧授予、Replace、普攻监听附加盒与充能爆发。

**Architecture:** 蓝图是 `SkillDefinition`（`ConfigId` + `AttackKind` + 模块数组）。运行时 `CombatComponent.TryActivate` 创建 `SkillInstance`，当帧跑模块；`PlayAttack` 与普攻共用招式播放器，事件只看 `Kind`。`EffectHolder` 管寿命与扇出，不自挂 `_PhysicsProcess`。`HitboxComponent` 支持多只并发盒（每只独立 `AttackId`），以便技能开头伤害与监听附加盒叠在普攻上。

**Tech Stack:** Godot 4.6、C# / Godot.NET.Sdk、`[GlobalClass]` Resource、既有逻辑 AABB 判定

**Spec:** `docs/superpowers/specs/2026-08-31-skill-definition-design.md`

## Global Constraints

- 文档与计划使用中文；代码标识符保持英文
- 全局命名空间（与现有 `scripts/` 一致），不使用额外 namespace
- 判定真相仍是逻辑三轴 AABB；不引入 `Area2D` 做命中
- 组件不自挂 `_PhysicsProcess` 做玩法；调度仍是 Input → Movement → Hitbox → Combat → Hurtbox 调试
- 缺依赖时 `GD.PushError`（含节点路径）并跳过，不抛未处理异常
- 不把 `AnimationPlayer` 当玩法时钟；不做时间轴编辑器、投射物飞行、资源池、Modifier 改攻击力公式、续招状态机、自动化测试
- `Kind == Skill` 的 `PlayAttack` 不得发 `Basic*` 事件
- 监听附加盒的命中只扣血，不再发 `*Hit`、不再给充能 +1
- 完成标准：`dotnet build ProjectC.csproj` 成功 + 手动跑图
- 保持节点名拼写 `Privot`
- 实现时读 `godot-prompter:resource-pattern`、`godot-prompter:ability-system`、`godot-prompter:component-system`；**不要**把 skill 里的 Area2D Hitbox 示例当本项目判定真相

## 文件结构

| 文件 | 职责 |
|------|------|
| `scripts/data/AttackKind.cs` | `AttackKind`、`SkillStacking`、`SkillTargeting` |
| `scripts/data/HitboxEntry.cs` | 单只判定盒窗口 |
| `scripts/data/AttackSpec.cs` | 前摇/判定/后摇 + 盒子列表（替代 `AttackData`） |
| `scripts/data/SkillModule.cs` | 模块基类 |
| `scripts/data/PlayAttackModule.cs` | 激活时开招 |
| `scripts/data/ApplyEffectModule.cs` | 按 Targeting 施加效果 |
| `scripts/data/GrantListenerModule.cs` | 给施放者挂监听效果 |
| `scripts/data/GameplayEffect.cs` | 效果蓝图 |
| `scripts/data/SkillDefinition.cs` | 技能蓝图 |
| `scripts/SkillInstance.cs` | 运行时实例（普通 C# 类，不是 Node） |
| `scripts/EffectHolder.cs` | 寿命、Tick、按来源卸掉；由 Combat 持有 |
| `scripts/data/JobDefinition.cs` | `Attack`/`Skill`/`Ultimate` 改为 `SkillDefinition` |
| `scripts/HitboxComponent.cs` | 多只并发盒；`Hit(hurtbox, attackId)` |
| `scripts/CombatComponent.cs` | `TryActivate`、播放、事件、Replace |
| `scripts/PlayerInputComponent.cs` / `InputActions.cs` / `project.godot` | 普攻走 `TryActivate`；战技 Z、大招 V |
| `data/actors/attacks/player_melee_spec.tres` | 迁移后的普攻盒子 |
| `data/actors/skills/player_default_attack.tres` | Kind=Basic 普攻定义 |
| `data/actors/skills/player_extra_blow.tres` | 测试用：小伤害 + 协同附加盒 |
| `data/actors/skills/player_charge_burst.tres` | 测试用：充能/到期爆发 |
| `data/actors/jobs/player_default_job.tres` | 改绑上述技能 |
| 删除 `scripts/data/AttackData.cs`、`data/actors/attacks/player_melee_default.tres` | 被 AttackSpec 替代 |

---

## 第一期：统一普攻

### Task 1: 技能蓝图类型（尚不改 Job / Combat）

**Skills:** `godot-prompter:resource-pattern`

**Files:**
- Create: `scripts/data/AttackKind.cs`
- Create: `scripts/data/HitboxEntry.cs`
- Create: `scripts/data/AttackSpec.cs`
- Create: `scripts/data/SkillModule.cs`
- Create: `scripts/data/PlayAttackModule.cs`
- Create: `scripts/data/SkillDefinition.cs`
- Create: `scripts/SkillInstance.cs`

**Interfaces:**
- Consumes: 无
- Produces:
  - `AttackKind`：`Basic = 0`、`Skill = 1`
  - `SkillStacking`：`Replace = 0`、`Independent = 1`、`Reject = 2`
  - `SkillTargeting`：`Self = 0`、`EnemiesInRadius = 1`、`AlliesInRadius = 2`、`EveryoneInRadius = 3`
  - `HitboxEntry`：`Start`/`End`（`< 0` 表示用 Spec 默认窗）、`Offset`、`Size`
  - `AttackSpec`：`Startup`、`Active`（默认 `0.2`）、`Recovery`、`CancelOpenAt`（默认 `-1`）、`Array<HitboxEntry> Hitboxes`
  - `SkillModule.OnActivate(CombatComponent combat, SkillInstance instance)`
  - `PlayAttackModule.Spec : AttackSpec`
  - `SkillDefinition`：`ConfigId`、`Kind`、`Cost`、`Cooldown`、`Stacking`、`Targeting`、`AreaRadius`、`Modules`
  - `SkillInstance`：`ConfigId`、`RuntimeId`、`Kind`、`Definition`、`PlayAttack`（可空）
  - `PlayAttackState`：见 `SkillInstance.cs`

- [ ] **Step 1: 创建 `scripts/data/AttackKind.cs`**

```csharp
public enum AttackKind
{
	Basic = 0,
	Skill = 1
}

public enum SkillStacking
{
	Replace = 0,
	Independent = 1,
	Reject = 2
}

public enum SkillTargeting
{
	Self = 0,
	EnemiesInRadius = 1,
	AlliesInRadius = 2,
	EveryoneInRadius = 3
}
```

- [ ] **Step 2: 创建 `scripts/data/HitboxEntry.cs`**

```csharp
using Godot;

[GlobalClass]
public partial class HitboxEntry : Resource
{
	[Export]
	public float Start { get; set; } = -1f;

	[Export]
	public float End { get; set; } = -1f;

	[Export]
	public Vector3 Offset { get; set; } = new(48f, 0f, 36f);

	[Export]
	public Vector3 Size { get; set; } = new(72f, 28f, 72f);
}
```

- [ ] **Step 3: 创建 `scripts/data/AttackSpec.cs`**

```csharp
using Godot;

[GlobalClass]
public partial class AttackSpec : Resource
{
	[Export]
	public float Startup { get; set; }

	[Export]
	public float Active { get; set; } = 0.2f;

	[Export]
	public float Recovery { get; set; }

	[Export]
	public float CancelOpenAt { get; set; } = -1f;

	[Export]
	public Godot.Collections.Array<HitboxEntry> Hitboxes { get; set; } = new();

	public float TotalDuration => Mathf.Max(0f, Startup) + Mathf.Max(0f, Active) + Mathf.Max(0f, Recovery);

	public bool TryResolveWindow(HitboxEntry entry, out float start, out float end)
	{
		start = 0f;
		end = 0f;
		if (entry == null)
		{
			return false;
		}

		start = entry.Start >= 0f ? entry.Start : Startup;
		end = entry.End >= 0f ? entry.End : Startup + Active;
		return end > start;
	}
}
```

- [ ] **Step 4: 创建 `scripts/data/SkillModule.cs`**

```csharp
using Godot;

[GlobalClass]
public partial class SkillModule : Resource
{
	public virtual void OnActivate(CombatComponent combat, SkillInstance instance)
	{
	}
}
```

- [ ] **Step 5: 创建 `scripts/data/PlayAttackModule.cs`**

```csharp
using Godot;

[GlobalClass]
public partial class PlayAttackModule : SkillModule
{
	[Export]
	public AttackSpec Spec { get; set; }

	public override void OnActivate(CombatComponent combat, SkillInstance instance)
	{
		_ = combat;
		_ = instance;
	}
}
```

- [ ] **Step 6: 创建 `scripts/SkillInstance.cs`**

```csharp
public sealed class PlayAttackState
{
	public AttackSpec Spec;
	public HitboxEntry Entry;
	public float Elapsed;
	public float Total;
	public float WindowStart;
	public float WindowEnd;
	public bool BoxOpen;
	public int BoxAttackId;
}

public sealed class SkillInstance
{
	public string ConfigId;
	public uint RuntimeId;
	public AttackKind Kind;
	public SkillDefinition Definition;
	public PlayAttackState PlayAttack;
}
```

- [ ] **Step 7: 创建 `scripts/data/SkillDefinition.cs`**

```csharp
using Godot;

[GlobalClass]
public partial class SkillDefinition : Resource
{
	[Export]
	public string ConfigId { get; set; } = "";

	[Export]
	public AttackKind Kind { get; set; } = AttackKind.Basic;

	[Export]
	public int Cost { get; set; }

	[Export]
	public float Cooldown { get; set; }

	[Export]
	public SkillStacking Stacking { get; set; } = SkillStacking.Replace;

	[Export]
	public SkillTargeting Targeting { get; set; } = SkillTargeting.Self;

	[Export]
	public float AreaRadius { get; set; }

	[Export]
	public Godot.Collections.Array<SkillModule> Modules { get; set; } = new();

	public bool HasPlayAttack()
	{
		if (Modules == null)
		{
			return false;
		}

		foreach (var module in Modules)
		{
			if (module is PlayAttackModule)
			{
				return true;
			}
		}

		return false;
	}
}
```

`HasGrantModules` 在 Task 4 再加。`PlayAttackModule.OnActivate` 本任务保持空实现，避免调用尚未存在的 `BeginPlayAttack`。

- [ ] **Step 8: 编译**

Run: `dotnet build ProjectC.csproj`

Expected: 成功。`Job.Attack` 仍是 `AttackData`。

- [ ] **Step 9: Commit**

```powershell
git add scripts/data/AttackKind.cs scripts/data/HitboxEntry.cs scripts/data/AttackSpec.cs scripts/data/SkillModule.cs scripts/data/PlayAttackModule.cs scripts/data/SkillDefinition.cs scripts/SkillInstance.cs
git commit -m "新增技能蓝图类型，尚不替换普攻 AttackData。"
```

---

### Task 2: Hitbox 支持多只并发盒

**Skills:** `godot-prompter:component-system`

**Files:**
- Modify: `scripts/HitboxComponent.cs`（全文替换）
- Modify: `scripts/CombatComponent.cs`（只改 `OnHit` 签名以通过编译）

**Interfaces:**
- Consumes: 现有 `LogicAabb` / `HurtboxRegistry` / `CombatDebugDraw`
- Produces:
  - `void Activate(int attackId, Vector3 offset, Vector3 size)` — 增加一只盒，不关掉其它盒
  - `void Deactivate(int attackId)` — 关掉指定盒；没有该 Id 则忽略
  - `void DeactivateAll()`
  - 信号 `Hit(HurtboxComponent hurtbox, int attackId)`
  - `CurrentAttackId` 删除；调用方改用信号里的 `attackId`

- [ ] **Step 1: 替换 `scripts/HitboxComponent.cs`**

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
	public delegate void HitEventHandler(HurtboxComponent hurtbox, int attackId);

	private sealed class ActiveStrike
	{
		public int AttackId;
		public Vector3 Offset;
		public Vector3 Size;
		public HashSet<HurtboxComponent> HitThisAttack = new();
	}

	private TransformComponent m_Transform;
	private Actor m_OwnerActor;
	private readonly List<ActiveStrike> m_Strikes = new();

	public bool IsActive => m_Strikes.Count > 0;

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
		for (var i = 0; i < m_Strikes.Count; i++)
		{
			if (m_Strikes[i].AttackId == attackId)
			{
				m_Strikes[i].Offset = offset;
				m_Strikes[i].Size = size;
				m_Strikes[i].HitThisAttack.Clear();
				QueueRedraw();
				return;
			}
		}

		m_Strikes.Add(new ActiveStrike
		{
			AttackId = attackId,
			Offset = offset,
			Size = size
		});
		QueueRedraw();
	}

	public void Deactivate(int attackId)
	{
		for (var i = m_Strikes.Count - 1; i >= 0; i--)
		{
			if (m_Strikes[i].AttackId == attackId)
			{
				m_Strikes.RemoveAt(i);
			}
		}

		QueueRedraw();
	}

	public void DeactivateAll()
	{
		m_Strikes.Clear();
		QueueRedraw();
	}

	public void PhysicsTick(double delta)
	{
		_ = delta;
		if (m_Strikes.Count == 0 || m_Transform == null)
		{
			return;
		}

		foreach (var strike in m_Strikes)
		{
			if (!TryGetWorldAabb(strike, out var myAabb))
			{
				continue;
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

				if (!strike.HitThisAttack.Add(hurtbox))
				{
					continue;
				}

				EmitSignal(SignalName.Hit, hurtbox, strike.AttackId);
			}
		}

		if (DebugDrawEnabled)
		{
			QueueRedraw();
		}
	}

	public override void _Draw()
	{
		if (!DebugDrawEnabled || m_Strikes.Count == 0)
		{
			return;
		}

		if (MapContext.Instance == null || !MapContext.Instance.HasOrigin)
		{
			return;
		}

		if (m_Transform == null)
		{
			return;
		}

		foreach (var strike in m_Strikes)
		{
			if (!TryGetWorldAabb(strike, out var aabb))
			{
				continue;
			}

			CombatDebugDraw.DrawVolume(
				this,
				aabb,
				m_Transform.GetLogicX(),
				m_Transform.GetLogicDepth(),
				new Color(0.95f, 0.2f, 0.2f, 0.95f));
		}
	}

	private bool TryGetWorldAabb(ActiveStrike strike, out LogicAabb aabb)
	{
		aabb = default;
		if (m_Transform == null)
		{
			return false;
		}

		var signed = LogicAabb.ApplyFacingOffset(strike.Offset, m_Transform.GetFacing());
		var center = new Vector3(
			m_Transform.GetLogicX() + signed.X,
			m_Transform.GetLogicDepth() + signed.Y,
			m_Transform.GetVirtualZ() + signed.Z);
		aabb = LogicAabb.FromCenterSize(center, strike.Size);
		return aabb.HasVolume;
	}
}
```

- [ ] **Step 2: 改 `scripts/CombatComponent.cs` 的 `OnHit` 与关盒**

把 `OnHit(HurtboxComponent hurtbox)` 改为 `OnHit(HurtboxComponent hurtbox, int attackId)`，日志用 `attackId`。把 `m_Hitbox.Deactivate()` 改为 `m_Hitbox.DeactivateAll()`（本期仍只有一只盒）。

```csharp
	private void OnHit(HurtboxComponent hurtbox, int attackId)
	{
		var target = hurtbox.GetOwnerActor();
		var name = target != null ? target.Name.ToString() : hurtbox.Name.ToString();
		GD.Print($"CombatComponent: hit {name} attackId={attackId}");

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
```

`PhysicsTick` 里到期调用 `m_Hitbox.DeactivateAll()`。

- [ ] **Step 3: 编译**

Run: `dotnet build ProjectC.csproj`

Expected: 成功。

- [ ] **Step 4: 手动确认**

跑图：X 普攻仍能打中 Enemy 并扣血。盒子调试绘制仍在。

- [ ] **Step 5: Commit**

```powershell
git add scripts/HitboxComponent.cs scripts/CombatComponent.cs
git commit -m "Hitbox 改为多只并发盒，命中信号带 AttackId。"
```

---

### Task 3: Job 槽改为 SkillDefinition，普攻走 TryActivate

**Skills:** `godot-prompter:resource-pattern`、`godot-prompter:ability-system`

**Files:**
- Modify: `scripts/data/JobDefinition.cs`
- Modify: `scripts/data/PlayAttackModule.cs`
- Modify: `scripts/CombatComponent.cs`（全文替换）
- Create: `data/actors/attacks/player_melee_spec.tres`
- Create: `data/actors/skills/player_default_attack.tres`
- Modify: `data/actors/jobs/player_default_job.tres`
- Delete: `scripts/data/AttackData.cs`
- Delete: `data/actors/attacks/player_melee_default.tres`

**Interfaces:**
- Consumes: Task 1 类型；`Job.Attack : SkillDefinition`
- Produces:
  - `CombatComponent.TryActivate(SkillDefinition def)` / `TryStartAttack()` 转调 `Job.Attack`
  - `BeginPlayAttack(SkillInstance instance, AttackSpec spec)`（供模块调用）
  - 信号 `BasicAttackStarted(int attackId, int runtimeId)`、`BasicAttackHit(int attackId, int runtimeId, HurtboxComponent hurtbox)`、对应 `SkillAttack*`（本期即可发出，尚无监听者）
  - `IsAttacking`：存在进行中的 `Kind==Basic` 的 `PlayAttack`

- [ ] **Step 1: 改 `JobDefinition`**

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
	public SkillDefinition Attack { get; set; }

	[Export]
	public PackedScene Dodge { get; set; }

	[Export]
	public SkillDefinition Skill { get; set; }

	[Export]
	public SkillDefinition Ultimate { get; set; }
}
```

- [ ] **Step 2: `PlayAttackModule.OnActivate` 调用 `BeginPlayAttack`**

```csharp
	public override void OnActivate(CombatComponent combat, SkillInstance instance)
	{
		combat?.BeginPlayAttack(instance, Spec);
	}
```

- [ ] **Step 3: 替换 `scripts/CombatComponent.cs`**

```csharp
using System.Collections.Generic;
using Godot;

public partial class CombatComponent : Node
{
	[Signal]
	public delegate void BasicAttackStartedEventHandler(int attackId, int runtimeId);

	[Signal]
	public delegate void BasicAttackHitEventHandler(int attackId, int runtimeId, HurtboxComponent hurtbox);

	[Signal]
	public delegate void SkillAttackStartedEventHandler(int attackId, int runtimeId);

	[Signal]
	public delegate void SkillAttackHitEventHandler(int attackId, int runtimeId, HurtboxComponent hurtbox);

	private HitboxComponent m_Hitbox;
	private Actor m_Actor;
	private SkillDefinition m_BasicAttack;
	private uint m_NextRuntimeId = 1;
	private int m_NextAttackId = 1;
	private readonly List<SkillInstance> m_Instances = new();
	private readonly Dictionary<int, StrikeInfo> m_Strikes = new();
	private readonly Dictionary<string, float> m_CooldownRemaining = new();

	private struct StrikeInfo
	{
		public uint RuntimeId;
		public AttackKind Kind;
		public bool FromListener;
	}

	public bool IsAttacking => FindBasicPlayAttack() != null;

	public override void _Ready()
	{
		m_Hitbox = GetNodeOrNull<HitboxComponent>("../HitboxComponent");
		if (m_Hitbox == null)
		{
			GD.PushError($"{GetPath()}: missing sibling HitboxComponent at ../HitboxComponent");
			return;
		}

		m_Actor = GetParentOrNull<Actor>();
		if (m_Actor == null)
		{
			GD.PushError($"{GetPath()}: parent is not Actor");
			return;
		}

		if (m_Actor.Definition?.Job == null)
		{
			GD.PushError($"{GetPath()}: Actor.Definition.Job is null");
			return;
		}

		m_BasicAttack = m_Actor.Definition.Job.Attack;
		if (m_BasicAttack == null)
		{
			GD.PushError($"{GetPath()}: Job.Attack is null");
			return;
		}

		ValidateSlot(m_BasicAttack, expectedKind: AttackKind.Basic, slotName: "Attack");
		ValidateSlot(m_Actor.Definition.Job.Skill, expectedKind: AttackKind.Skill, slotName: "Skill");
		ValidateSlot(m_Actor.Definition.Job.Ultimate, expectedKind: AttackKind.Skill, slotName: "Ultimate");

		m_Hitbox.Hit += OnHit;

		if (m_Actor.Health != null)
		{
			m_Actor.Health.Died += OnOwnerDied;
		}
	}

	public override void _ExitTree()
	{
		if (m_Hitbox != null)
		{
			m_Hitbox.Hit -= OnHit;
		}

		if (m_Actor?.Health != null)
		{
			m_Actor.Health.Died -= OnOwnerDied;
		}
	}

	public void TryStartAttack()
	{
		TryActivate(m_BasicAttack);
	}

	public void TryStartSkill()
	{
		TryActivate(m_Actor?.Definition?.Job?.Skill);
	}

	public void TryStartUltimate()
	{
		TryActivate(m_Actor?.Definition?.Job?.Ultimate);
	}

	public bool TryActivate(SkillDefinition def)
	{
		if (m_Hitbox == null || m_Actor == null || def == null)
		{
			return false;
		}

		if (string.IsNullOrEmpty(def.ConfigId))
		{
			GD.PushError($"{GetPath()}: SkillDefinition.ConfigId is empty");
			return false;
		}

		if (def.Cost != 0)
		{
			GD.PushError($"{GetPath()}: Cost={def.Cost} but resource pool is not implemented ({def.ConfigId})");
			return false;
		}

		if (def.Stacking != SkillStacking.Replace)
		{
			GD.PushError($"{GetPath()}: Stacking={def.Stacking} not implemented, using Replace ({def.ConfigId})");
		}

		if (!def.HasPlayAttack() && !HasGrantModules(def))
		{
			GD.PushError($"{GetPath()}: skill has no PlayAttack and no grant modules ({def.ConfigId})");
			return false;
		}

		if (def.Kind == AttackKind.Basic && FindBasicPlayAttack() != null)
		{
			return false;
		}

		if (m_CooldownRemaining.TryGetValue(def.ConfigId, out var cdLeft) && cdLeft > 0f)
		{
			return false;
		}

		if (def.Kind == AttackKind.Skill)
		{
			ReplaceByConfigId(def.ConfigId);
		}

		var instance = new SkillInstance
		{
			ConfigId = def.ConfigId,
			RuntimeId = m_NextRuntimeId,
			Kind = def.Kind,
			Definition = def
		};
		m_NextRuntimeId += 1;
		m_Instances.Add(instance);

		if (def.Modules != null)
		{
			foreach (var module in def.Modules)
			{
				module?.OnActivate(this, instance);
			}
		}

		if (def.Cooldown > 0f)
		{
			m_CooldownRemaining[def.ConfigId] = def.Cooldown;
		}

		return true;
	}

	public void BeginPlayAttack(SkillInstance instance, AttackSpec spec)
	{
		if (instance == null || spec == null)
		{
			GD.PushError($"{GetPath()}: BeginPlayAttack missing instance or spec");
			return;
		}

		if (spec.Hitboxes == null || spec.Hitboxes.Count == 0)
		{
			GD.PushError($"{GetPath()}: AttackSpec.Hitboxes is empty ({instance.ConfigId})");
			return;
		}

		if (spec.Hitboxes.Count > 1)
		{
			GD.PushError($"{GetPath()}: AttackSpec has {spec.Hitboxes.Count} hitboxes; using the first ({instance.ConfigId})");
		}

		var entry = spec.Hitboxes[0];
		if (!spec.TryResolveWindow(entry, out var start, out var end))
		{
			GD.PushError($"{GetPath()}: invalid hitbox window ({instance.ConfigId})");
			return;
		}

		instance.PlayAttack = new PlayAttackState
		{
			Spec = spec,
			Entry = entry,
			Elapsed = 0f,
			Total = spec.TotalDuration,
			WindowStart = start,
			WindowEnd = end,
			BoxOpen = false,
			BoxAttackId = 0
		};

		if (start <= 0f)
		{
			TryOpenPlayBox(instance, instance.PlayAttack, previous: -1f);
		}
	}

	public void PhysicsTick(double delta)
	{
		var dt = (float)delta;
		TickCooldowns(dt);
		TickPlayAttacks(dt);
	}

	private void TickCooldowns(float dt)
	{
		if (m_CooldownRemaining.Count == 0)
		{
			return;
		}

		var keys = new List<string>(m_CooldownRemaining.Keys);
		foreach (var key in keys)
		{
			var left = m_CooldownRemaining[key] - dt;
			if (left <= 0f)
			{
				m_CooldownRemaining.Remove(key);
			}
			else
			{
				m_CooldownRemaining[key] = left;
			}
		}
	}

	private void TickPlayAttacks(float dt)
	{
		for (var i = m_Instances.Count - 1; i >= 0; i--)
		{
			var instance = m_Instances[i];
			var play = instance.PlayAttack;
			if (play == null)
			{
				if (instance.Kind == AttackKind.Basic)
				{
					m_Instances.RemoveAt(i);
				}

				continue;
			}

			var previous = play.Elapsed;
			play.Elapsed += dt;
			TryOpenPlayBox(instance, play, previous);
			TryClosePlayBox(instance, play, previous);

			if (play.Elapsed >= play.Total)
			{
				if (play.BoxOpen)
				{
					m_Hitbox.Deactivate(play.BoxAttackId);
					m_Strikes.Remove(play.BoxAttackId);
					play.BoxOpen = false;
				}

				instance.PlayAttack = null;
				if (instance.Kind == AttackKind.Basic)
				{
					m_Instances.RemoveAt(i);
				}
			}
		}
	}

	private void TryOpenPlayBox(SkillInstance instance, PlayAttackState play, float previous)
	{
		if (play.BoxOpen)
		{
			return;
		}

		if (previous < play.WindowStart && play.Elapsed >= play.WindowStart)
		{
			var attackId = m_NextAttackId;
			m_NextAttackId += 1;
			play.BoxAttackId = attackId;
			play.BoxOpen = true;
			m_Strikes[attackId] = new StrikeInfo
			{
				RuntimeId = instance.RuntimeId,
				Kind = instance.Kind,
				FromListener = false
			};
			m_Hitbox.Activate(attackId, play.Entry.Offset, play.Entry.Size);
			EmitAttackStarted(instance.Kind, attackId, instance.RuntimeId);
		}
	}

	private void TryClosePlayBox(SkillInstance instance, PlayAttackState play, float previous)
	{
		_ = instance;
		if (!play.BoxOpen)
		{
			return;
		}

		if (previous < play.WindowEnd && play.Elapsed >= play.WindowEnd)
		{
			m_Hitbox.Deactivate(play.BoxAttackId);
			m_Strikes.Remove(play.BoxAttackId);
			play.BoxOpen = false;
		}
	}

	private void EmitAttackStarted(AttackKind kind, int attackId, uint runtimeId)
	{
		if (kind == AttackKind.Basic)
		{
			EmitSignal(SignalName.BasicAttackStarted, attackId, (int)runtimeId);
		}
		else
		{
			EmitSignal(SignalName.SkillAttackStarted, attackId, (int)runtimeId);
		}
	}

	private void OnHit(HurtboxComponent hurtbox, int attackId)
	{
		var target = hurtbox.GetOwnerActor();
		var name = target != null ? target.Name.ToString() : hurtbox.Name.ToString();
		GD.Print($"CombatComponent: hit {name} attackId={attackId}");

		if (m_Actor == null)
		{
			return;
		}

		var health = target?.Health;
		if (health != null)
		{
			health.TakeDamage(m_Actor.GetAttackPower());
		}

		if (!m_Strikes.TryGetValue(attackId, out var info) || info.FromListener)
		{
			return;
		}

		if (info.Kind == AttackKind.Basic)
		{
			EmitSignal(SignalName.BasicAttackHit, attackId, (int)info.RuntimeId, hurtbox);
		}
		else
		{
			EmitSignal(SignalName.SkillAttackHit, attackId, (int)info.RuntimeId, hurtbox);
		}
	}

	private void ReplaceByConfigId(string configId)
	{
		for (var i = m_Instances.Count - 1; i >= 0; i--)
		{
			var instance = m_Instances[i];
			if (instance.ConfigId != configId)
			{
				continue;
			}

			CancelPlayAttack(instance);
			m_Instances.RemoveAt(i);
		}
	}

	private void CancelPlayAttack(SkillInstance instance)
	{
		var play = instance.PlayAttack;
		if (play == null)
		{
			return;
		}

		if (play.BoxOpen)
		{
			m_Hitbox.Deactivate(play.BoxAttackId);
			m_Strikes.Remove(play.BoxAttackId);
		}

		instance.PlayAttack = null;
	}

	private SkillInstance FindBasicPlayAttack()
	{
		foreach (var instance in m_Instances)
		{
			if (instance.Kind == AttackKind.Basic && instance.PlayAttack != null)
			{
				return instance;
			}
		}

		return null;
	}

	private void ValidateSlot(SkillDefinition def, AttackKind expectedKind, string slotName)
	{
		if (def == null)
		{
			return;
		}

		if (def.Kind != expectedKind)
		{
			GD.PushError($"{GetPath()}: Job.{slotName} Kind is {def.Kind}, expected {expectedKind}");
		}

		if (expectedKind == AttackKind.Basic && HasGrantModules(def))
		{
			GD.PushError($"{GetPath()}: Job.Attack must not grant duration effects");
		}
	}

	private static bool HasGrantModules(SkillDefinition def)
	{
		if (def?.Modules == null)
		{
			return false;
		}

		foreach (var module in def.Modules)
		{
			if (module != null && module is not PlayAttackModule)
			{
				return true;
			}
		}

		return false;
	}

	private void OnOwnerDied()
	{
		for (var i = m_Instances.Count - 1; i >= 0; i--)
		{
			CancelPlayAttack(m_Instances[i]);
			m_Instances.RemoveAt(i);
		}

		m_Hitbox?.DeactivateAll();
		m_Strikes.Clear();
	}
}
```

`Startup=0` 时 `BeginPlayAttack` 已用 `previous: -1f` 当帧开盒。本帧 Hitbox 查询已在 Combat 之前跑过，当帧开的盒从**下一物理帧**开始命中，与迁移前「激活当帧开盒、次帧查询」一致。关盒只在 `PhysicsTick` 里做。

- [ ] **Step 4: 创建 `data/actors/attacks/player_melee_spec.tres`**

```
[gd_resource type="Resource" script_class="AttackSpec" load_steps=4 format=3]

[ext_resource type="Script" path="res://scripts/data/AttackSpec.cs" id="1_spec"]
[ext_resource type="Script" path="res://scripts/data/HitboxEntry.cs" id="2_entry"]

[sub_resource type="Resource" id="Hitbox_1"]
script = ExtResource("2_entry")
Start = -1.0
End = -1.0
Offset = Vector3(48, 0, 36)
Size = Vector3(72, 28, 72)

[resource]
script = ExtResource("1_spec")
Startup = 0.0
Active = 0.2
Recovery = 0.0
CancelOpenAt = -1.0
Hitboxes = Array[ExtResource("2_entry")]([SubResource("Hitbox_1")])
```

若 Godot 对 `Array[ExtResource("2_entry")]` 报错，改成：

```
Hitboxes = [SubResource("Hitbox_1")]
```

- [ ] **Step 5: 创建 `data/actors/skills/player_default_attack.tres`**

```
[gd_resource type="Resource" script_class="SkillDefinition" load_steps=4 format=3]

[ext_resource type="Script" path="res://scripts/data/SkillDefinition.cs" id="1_def"]
[ext_resource type="Script" path="res://scripts/data/PlayAttackModule.cs" id="2_mod"]
[ext_resource type="Resource" path="res://data/actors/attacks/player_melee_spec.tres" id="3_spec"]

[sub_resource type="Resource" id="Play_1"]
script = ExtResource("2_mod")
Spec = ExtResource("3_spec")

[resource]
script = ExtResource("1_def")
ConfigId = "skill.player_default.attack"
Kind = 0
Cost = 0
Cooldown = 0.0
Stacking = 0
Targeting = 0
AreaRadius = 0.0
Modules = [SubResource("Play_1")]
```

- [ ] **Step 6: 改 `data/actors/jobs/player_default_job.tres`**

把 `player_melee_default.tres` 换成 `res://data/actors/skills/player_default_attack.tres`。`Attack` 字段类型变为 `SkillDefinition`。Enemy 职业不设 Attack，保持 null。

- [ ] **Step 7: 删除 `AttackData.cs` 与 `player_melee_default.tres`**

全仓库搜索 `AttackData`，确认只剩已删文件。

- [ ] **Step 8: 编译**

Run: `dotnet build ProjectC.csproj`

Expected: 成功。

- [ ] **Step 9: 手动确认**

X 普攻：0.2s 红盒、命中扣 10 血、连按在挥击结束前无效、约 3 下打死默认 Enemy。日志仍有 `attackId=`。

- [ ] **Step 10: Commit**

```powershell
git add scripts/data/JobDefinition.cs scripts/data/PlayAttackModule.cs scripts/CombatComponent.cs data/actors/attacks/player_melee_spec.tres data/actors/skills/player_default_attack.tres data/actors/jobs/player_default_job.tres
git add -u scripts/data/AttackData.cs data/actors/attacks/player_melee_default.tres
git commit -m "普攻改为 SkillDefinition，用 TryActivate 播放 AttackSpec。"
```

---

## 第二期：当帧授予与 Replace

### Task 4: GameplayEffect、ApplyEffect、EffectHolder

**Skills:** `godot-prompter:ability-system`、`godot-prompter:resource-pattern`

**Files:**
- Create: `scripts/data/GameplayEffect.cs`
- Create: `scripts/data/ApplyEffectModule.cs`
- Create: `scripts/data/GrantListenerModule.cs`
- Create: `scripts/EffectHolder.cs`
- Modify: `scripts/data/SkillDefinition.cs`（加 `HasGrantModules`）
- Modify: `scripts/CombatComponent.cs`（接入 Holder；Replace 卸效果；Tick 效果）

**Interfaces:**
- Consumes: `SkillTargeting`、`HurtboxRegistry`、`CombatTeam`
- Produces:
  - `GameplayEffect` 字段见 Step 1
  - `EffectInstance`：`Blueprint`、`Target`、`SourceRuntimeId`、`SourceConfigId`、`ApplyOrder`、`Elapsed`、`TickAccum`、`Charge`、`BurstConsumed`
  - `EffectHolder.Apply` / `RemoveBySourceRuntimeId(uint, expire: false)` / `RemoveAll(expire: false)` / `PhysicsTick` / `GetActiveListeners`
  - `ApplyEffectModule.Effect`
  - `GrantListenerModule.Effect`（本任务只施加到 Self，扇出在 Task 5）

- [ ] **Step 1: 创建 `scripts/data/GameplayEffect.cs`**

```csharp
using Godot;

[GlobalClass]
public partial class GameplayEffect : Resource
{
	[Export]
	public float Duration { get; set; }

	[Export]
	public float Period { get; set; }

	[Export]
	public int TickDamage { get; set; }

	[Export]
	public bool SubscribeBasic { get; set; } = true;

	[Export]
	public bool SubscribeSkill { get; set; }

	[Export]
	public HitboxEntry ExtraHitbox { get; set; }

	[Export]
	public float ExtraHitboxDuration { get; set; } = 0.15f;

	[Export]
	public int ChargeMax { get; set; }

	[Export]
	public int BurstDamage { get; set; }

	[Export]
	public float BurstRadius { get; set; } = 80f;
}
```

`Duration <= 0`：瞬时，只 Apply 立刻 Remove，不 Expire、不 Burst。

- [ ] **Step 2: 创建 `ApplyEffectModule.cs` 与 `GrantListenerModule.cs`**

```csharp
using Godot;

[GlobalClass]
public partial class ApplyEffectModule : SkillModule
{
	[Export]
	public GameplayEffect Effect { get; set; }

	public override void OnActivate(CombatComponent combat, SkillInstance instance)
	{
		combat?.ApplyModuleEffect(instance, Effect, toSelfOnly: false);
	}
}

[GlobalClass]
public partial class GrantListenerModule : SkillModule
{
	[Export]
	public GameplayEffect Effect { get; set; }

	public override void OnActivate(CombatComponent combat, SkillInstance instance)
	{
		combat?.ApplyModuleEffect(instance, Effect, toSelfOnly: true);
	}
}
```

两个类分两个文件：`scripts/data/ApplyEffectModule.cs`、`scripts/data/GrantListenerModule.cs`。

- [ ] **Step 3: 创建 `scripts/EffectHolder.cs`**

```csharp
using System.Collections.Generic;
using Godot;

public sealed class EffectInstance
{
	public GameplayEffect Blueprint;
	public Actor Target;
	public uint SourceRuntimeId;
	public string SourceConfigId;
	public int ApplyOrder;
	public float Elapsed;
	public float TickAccum;
	public int Charge;
	public bool BurstConsumed;
}

public sealed class EffectHolder
{
	private readonly List<EffectInstance> m_Effects = new();
	private int m_NextApplyOrder = 1;

	public IReadOnlyList<EffectInstance> Effects => m_Effects;

	public EffectInstance Apply(GameplayEffect blueprint, Actor target, uint sourceRuntimeId, string sourceConfigId)
	{
		if (blueprint == null || target == null)
		{
			return null;
		}

		var instance = new EffectInstance
		{
			Blueprint = blueprint,
			Target = target,
			SourceRuntimeId = sourceRuntimeId,
			SourceConfigId = sourceConfigId,
			ApplyOrder = m_NextApplyOrder
		};
		m_NextApplyOrder += 1;
		m_Effects.Add(instance);
		GD.Print($"EffectHolder: apply src={sourceRuntimeId} cfg={sourceConfigId} dur={blueprint.Duration} -> {target.Name}");
		return instance;
	}

	public void RemoveBySourceRuntimeId(uint sourceRuntimeId, bool expire)
	{
		for (var i = m_Effects.Count - 1; i >= 0; i--)
		{
			if (m_Effects[i].SourceRuntimeId == sourceRuntimeId)
			{
				RemoveAt(i, expire);
			}
		}
	}

	public void RemoveAll(bool expire)
	{
		for (var i = m_Effects.Count - 1; i >= 0; i--)
		{
			RemoveAt(i, expire);
		}
	}

	public void PhysicsTick(float dt, CombatComponent combat)
	{
		for (var i = m_Effects.Count - 1; i >= 0; i--)
		{
			var effect = m_Effects[i];
			if (effect.Blueprint.Duration <= 0f)
			{
				RemoveAt(i, expire: false);
				continue;
			}

			effect.Elapsed += dt;
			if (effect.Blueprint.Period > 0f)
			{
				effect.TickAccum += dt;
				if (effect.TickAccum >= effect.Blueprint.Period)
				{
					effect.TickAccum -= effect.Blueprint.Period;
					combat.HandleEffectTick(effect);
				}
			}

			if (effect.Elapsed >= effect.Blueprint.Duration)
			{
				RemoveAt(i, expire: true);
			}
		}
	}

	public List<EffectInstance> SnapshotListeners()
	{
		var list = new List<EffectInstance>();
		foreach (var effect in m_Effects)
		{
			list.Add(effect);
		}

		list.Sort((a, b) =>
		{
			var cmp = a.ApplyOrder.CompareTo(b.ApplyOrder);
			if (cmp != 0)
			{
				return cmp;
			}

			return a.SourceRuntimeId.CompareTo(b.SourceRuntimeId);
		});
		return list;
	}

	private void RemoveAt(int index, bool expire)
	{
		var effect = m_Effects[index];
		m_Effects.RemoveAt(index);
		if (expire)
		{
			GD.Print($"EffectHolder: expire src={effect.SourceRuntimeId} cfg={effect.SourceConfigId}");
		}
		else
		{
			GD.Print($"EffectHolder: remove src={effect.SourceRuntimeId} cfg={effect.SourceConfigId}");
		}
	}
}
```

瞬时效果：`Apply` 后同一 `OnActivate` 结束前不要立刻删。在 `ApplyModuleEffect` 末尾，若 `Duration <= 0` 立刻 `RemoveBySource` 会误删同实例其它持续效果。瞬时效果在 `PhysicsTick` 开头删（`Duration <= 0` 且 `expire: false`）。`Apply` 当帧不 Tick。

- [ ] **Step 4: `SkillDefinition` 增加 `HasGrantModules`**

```csharp
	public bool HasGrantModules()
	{
		if (Modules == null)
		{
			return false;
		}

		foreach (var module in Modules)
		{
			if (module is ApplyEffectModule || module is GrantListenerModule)
			{
				return true;
			}
		}

		return false;
	}
```

`CombatComponent.HasGrantModules` 改为调用 `def.HasGrantModules()`。

- [ ] **Step 5: 在 Combat 中接入 Holder**

字段：`private readonly EffectHolder m_Effects = new();`

`TryActivate` 在 `Kind==Skill` 时 `ReplaceByConfigId` 必须先 `m_Effects.RemoveBySourceRuntimeId(old.RuntimeId, expire: false)`，再 `CancelPlayAttack`，再从 `m_Instances` 移除。

新增：

```csharp
	public void ApplyModuleEffect(SkillInstance instance, GameplayEffect effect, bool toSelfOnly)
	{
		if (instance == null || effect == null || m_Actor == null)
		{
			return;
		}

		var targeting = toSelfOnly ? SkillTargeting.Self : instance.Definition.Targeting;
		var radius = instance.Definition.AreaRadius;
		if (targeting != SkillTargeting.Self && radius <= 0f)
		{
			GD.PushError($"{GetPath()}: AreaRadius must be > 0 for targeting {targeting}");
			return;
		}

		foreach (var target in CollectTargets(targeting, radius))
		{
			m_Effects.Apply(effect, target, instance.RuntimeId, instance.ConfigId);
		}
	}

	public void HandleEffectTick(EffectInstance effect)
	{
		if (effect.Blueprint.TickDamage <= 0 || effect.Target?.Health == null)
		{
			return;
		}

		effect.Target.Health.TakeDamage(effect.Blueprint.TickDamage);
	}
```

`CollectTargets`：

- `Self`：`m_Actor` 一人
- 范围：扫 `HurtboxRegistry.Snapshot()`，取 `GetOwnerActor()`，水平距离 `sqrt(dx*dx+depth*depth) <= radius`，双方 Hurtbox `VirtualZ` 重叠（用 `TryGetWorldAabb` 的 Z 轴 `Overlaps` 或比较 Center.Z / HalfExtents.Z）
- `EnemiesInRadius`：`hurtbox.Team !=` 施放者 Hitbox.Team
- `AlliesInRadius`：同 Team（含自己；自己可能没有登记在别人的敌对规则里，Self 已含自己，范围盟友再加同 Team 其它 Actor）
- `EveryoneInRadius`：范围内所有 Actor

自己：施放者一定加入 `Self` / `AlliesInRadius` / `EveryoneInRadius`。

`PhysicsTick` 顺序：**不要**在 Hitbox 查询之前关盒。本方法在 Actor 里已经是 Hitbox 之后。先 `TickPlayAttacks`，再 `m_Effects.PhysicsTick(dt, this)`。效果到期 `expire: true` 时调用将在 Task 6 才接 Burst；本期 `RemoveAt(expire:true)` 只打日志。

`OnOwnerDied`：`m_Effects.RemoveAll(expire: false)`。

Skill 实例在 PlayAttack 结束后若仍有 `SourceRuntimeId` 匹配的效果，**不要**从 `m_Instances` 移除。新增：

```csharp
	private bool InstanceStillAlive(SkillInstance instance)
	{
		if (instance.PlayAttack != null)
		{
			return true;
		}

		foreach (var effect in m_Effects.Effects)
		{
			if (effect.SourceRuntimeId == instance.RuntimeId)
			{
				return true;
			}
		}

		return false;
	}
```

`TickPlayAttacks` 末尾：Basic 无 PlayAttack 则移除；Skill 则 `!InstanceStillAlive` 才移除。无 PlayAttack 且无授予的 Skill 在 `TryActivate` 已被拒绝。

- [ ] **Step 6: 编译**

Run: `dotnet build ProjectC.csproj`

Expected: 成功。普攻手感不变（无授予模块）。

- [ ] **Step 7: Commit**

```powershell
git add scripts/data/GameplayEffect.cs scripts/data/ApplyEffectModule.cs scripts/data/GrantListenerModule.cs scripts/EffectHolder.cs scripts/data/SkillDefinition.cs scripts/CombatComponent.cs
git commit -m "加入效果容器与施加模块，Replace 会卸掉旧来源效果。"
```

---

### Task 5: 战技键、测试用持续效果、Replace 手感

**Skills:** `godot-prompter:input-handling`

**Files:**
- Modify: `scripts/InputActions.cs`
- Modify: `project.godot`（`skill`、`ultimate`）
- Modify: `scripts/PlayerInputComponent.cs`
- Create: `data/actors/effects/player_dummy_aura.tres`
- Create: `data/actors/skills/player_dummy_aura_skill.tres`
- Modify: `data/actors/jobs/player_default_job.tres`（`Skill` 绑 dummy）

**Interfaces:**
- Consumes: `TryStartSkill` / `TryStartUltimate`
- Produces: 输入 `skill`（Z，physical_keycode `90`）、`ultimate`（V，`86`）

- [ ] **Step 1: `InputActions` 增加**

```csharp
	public const string Skill = "skill";
	public const string Ultimate = "ultimate";

	public static bool IsSkillJustPressed()
	{
		return Input.IsActionJustPressed(Skill);
	}

	public static bool IsUltimateJustPressed()
	{
		return Input.IsActionJustPressed(Ultimate);
	}
```

`project.godot` 的 `[input]` 里按现有 `attack` 块复制两份，分别改名为 `skill` / `ultimate`，`physical_keycode` 为 `90` 与 `86`。

`PlayerInputComponent.PhysicsTick` 在普攻之后：

```csharp
		if (m_Combat != null && InputActions.IsSkillJustPressed())
		{
			m_Combat.TryStartSkill();
		}

		if (m_Combat != null && InputActions.IsUltimateJustPressed())
		{
			m_Combat.TryStartUltimate();
		}
```

- [ ] **Step 2: dummy 效果与技能**

`player_dummy_aura.tres`：`Duration = 3`，其余默认。

`player_dummy_aura_skill.tres`：`ConfigId = "skill.player_default.dummy_aura"`，`Kind = 1`（Skill），`Cooldown = 0`，一个 `ApplyEffectModule` 指向该效果，`Targeting = Self`，无 `PlayAttack`。

`player_default_job.tres` 增加 `Skill =` 该技能。

- [ ] **Step 3: 编译并手测**

Run: `dotnet build ProjectC.csproj`

Expected: 成功。

手测：Z 打出 `EffectHolder: apply` 日志；3 秒后 `expire`；3 秒内再按 Z 应先 `remove`（Replace，不 `expire`）再 `apply`。X 普攻不受影响。

- [ ] **Step 4: Commit**

```powershell
git add scripts/InputActions.cs project.godot scripts/PlayerInputComponent.cs data/actors/effects/player_dummy_aura.tres data/actors/skills/player_dummy_aura_skill.tres data/actors/jobs/player_default_job.tres
git commit -m "战技键施加持续效果，同 ConfigId 再放走 Replace。"
```

---

## 第三期：监听扇出、附加盒、充能爆发

### Task 6: 普攻监听附加盒

**Skills:** `godot-prompter:event-bus`（仅用 Combat 自身信号，不建全局 EventBus）

**Files:**
- Modify: `scripts/CombatComponent.cs`
- Create: `data/actors/attacks/player_extra_hit_spec.tres`（或只在效果上填 `HitboxEntry`）
- Create: `data/actors/effects/player_extra_blow_listener.tres`
- Create: `data/actors/attacks/player_skill_poke_spec.tres`
- Create: `data/actors/skills/player_extra_blow.tres`
- Modify: `data/actors/jobs/player_default_job.tres`（`Skill` 改为 extra_blow）

**Interfaces:**
- Consumes: `BasicAttackStarted`；`GameplayEffect.ExtraHitbox`
- Produces: `OpenListenerHitbox(EffectInstance effect)` — 新 `AttackId`，`FromListener = true`，寿命 `ExtraHitboxDuration`，到期 `Deactivate(attackId)`

- [ ] **Step 1: Combat 增加监听盒状态与扇出**

结构：

```csharp
	private struct ListenerBox
	{
		public int AttackId;
		public float Remaining;
	}

	private readonly List<ListenerBox> m_ListenerBoxes = new();
```

`_Ready` 在 `m_Hitbox.Hit += OnHit` 之后：

```csharp
		BasicAttackStarted += OnBasicAttackStarted;
		SkillAttackStarted += OnSkillAttackStarted;
		BasicAttackHit += OnBasicAttackHit;
		SkillAttackHit += OnSkillAttackHit;
```

`_ExitTree` 对称断开。

```csharp
	private void OnBasicAttackStarted(int attackId, int runtimeId)
	{
		_ = attackId;
		_ = runtimeId;
		FanOutStarted(subscribeSkill: false);
	}

	private void OnSkillAttackStarted(int attackId, int runtimeId)
	{
		_ = attackId;
		_ = runtimeId;
		FanOutStarted(subscribeSkill: true);
	}

	private void FanOutStarted(bool subscribeSkill)
	{
		foreach (var effect in m_Effects.SnapshotListeners())
		{
			if (effect.Target != m_Actor)
			{
				continue;
			}

			var listen = subscribeSkill ? effect.Blueprint.SubscribeSkill : effect.Blueprint.SubscribeBasic;
			if (!listen)
			{
				continue;
			}

			OpenListenerHitbox(effect);
		}
	}

	private void OpenListenerHitbox(EffectInstance effect)
	{
		var entry = effect.Blueprint.ExtraHitbox;
		if (entry == null)
		{
			return;
		}

		var attackId = m_NextAttackId;
		m_NextAttackId += 1;
		m_Strikes[attackId] = new StrikeInfo
		{
			RuntimeId = effect.SourceRuntimeId,
			Kind = AttackKind.Skill,
			FromListener = true
		};
		m_Hitbox.Activate(attackId, entry.Offset, entry.Size);
		m_ListenerBoxes.Add(new ListenerBox
		{
			AttackId = attackId,
			Remaining = Mathf.Max(0.01f, effect.Blueprint.ExtraHitboxDuration)
		});
	}
```

`OnBasicAttackHit` / `OnSkillAttackHit` 本期只留空方法（或只打日志），Task 7 再写充能。必须先挂上空方法以便编译。

`PhysicsTick` 在 `TickPlayAttacks` 之后扣 `m_ListenerBoxes`：`Remaining -= dt`，`<=0` 则 `Deactivate(AttackId)`、`m_Strikes.Remove`。

`ReplaceByConfigId` / `OnOwnerDied` 时关掉该 `RuntimeId` 名下仍开着的监听盒（扫 `m_Strikes` 中 `RuntimeId` 匹配且 `FromListener` 的 Id）。

`Kind` 记为 `Skill` 仅用于调试；`FromListener=true` 保证不发 `*Hit`、不加充能。

- [ ] **Step 2: extra_blow 数据**

`player_skill_poke_spec.tres`：`Active=0.12`，盒子 Offset/Size 可与普攻相同或略小。

`player_extra_blow_listener.tres`：`Duration=6`，`SubscribeBasic=true`，`SubscribeSkill=false`，`ExtraHitbox` 为 SubResource（Offset 例如 `(64,0,36)`，Size 与普攻相近），`ExtraHitboxDuration=0.15`。

`player_extra_blow.tres`：`ConfigId = "skill.player_default.extra_blow"`，`Kind=Skill`，模块顺序：`PlayAttack(poke)` 然后 `GrantListener(listener)`。

Job.Skill 改绑 extra_blow（可删 dummy 引用，文件可留着）。

- [ ] **Step 3: 编译并手测**

Expected: `dotnet build` 成功。

手测：

1. Z：应有一小段技能红盒；该下**不要**再出现第二只因技能自己触发的协同盒。
2. 随后 6 秒内 X：普攻盒 + 更靠前的附加盒；Enemy 可被打两次（两次 `TakeDamage`，两次 hit 日志，两个 `attackId`）。
3. 6 秒内再 Z：旧监听 `remove` 不 `expire`，新 6 秒重新算。

- [ ] **Step 4: Commit**

```powershell
git add scripts/CombatComponent.cs data/actors/attacks/player_skill_poke_spec.tres data/actors/effects/player_extra_blow_listener.tres data/actors/skills/player_extra_blow.tres data/actors/jobs/player_default_job.tres
git commit -m "战技当帧挂监听，普攻额外开独立 AttackId 的附加盒。"
```

---

### Task 7: 充能、到期爆发、Replace 不爆发

**Files:**
- Modify: `scripts/CombatComponent.cs`、`scripts/EffectHolder.cs`
- Create: `data/actors/effects/player_charge_burst.tres`
- Create: `data/actors/skills/player_charge_burst.tres`
- Modify: `data/actors/jobs/player_default_job.tres`（`Ultimate` 绑 charge_burst）

**Interfaces:**
- Consumes: `ChargeMax`、`BurstDamage`、`BurstRadius`、`BasicAttackHit`
- Produces: `HandleChargeHit`、`HandleBurst(EffectInstance, fromExpire: bool)`；`EffectHolder.RemoveAt` 在 `expire:true` 时回调 Combat `OnEffectExpired`

- [ ] **Step 1: EffectHolder 到期回调**

给 `EffectHolder` 构造函数传入 `CombatComponent`，或 `PhysicsTick` 里 `expire:true` 时先 `combat.OnEffectExpired(effect)` 再从列表删除。`RemoveBySourceRuntimeId(expire:false)` **不得**调用 `OnEffectExpired`。

`OnEffectExpired`：若 `BurstConsumed` 则只打 remove 日志；否则 `HandleBurst(effect)`。

- [ ] **Step 2: 充能与爆发**

```csharp
	private void OnBasicAttackHit(int attackId, int runtimeId, HurtboxComponent hurtbox)
	{
		_ = attackId;
		_ = runtimeId;
		_ = hurtbox;
		foreach (var effect in m_Effects.SnapshotListeners())
		{
			if (effect.Target != m_Actor || !effect.Blueprint.SubscribeBasic)
			{
				continue;
			}

			TryAddCharge(effect);
		}
	}

	private void OnSkillAttackHit(int attackId, int runtimeId, HurtboxComponent hurtbox)
	{
		_ = attackId;
		_ = runtimeId;
		_ = hurtbox;
		foreach (var effect in m_Effects.SnapshotListeners())
		{
			if (effect.Target != m_Actor || !effect.Blueprint.SubscribeSkill)
			{
				continue;
			}

			TryAddCharge(effect);
		}
	}

	private void TryAddCharge(EffectInstance effect)
	{
		if (effect.Blueprint.ChargeMax <= 0 || effect.BurstConsumed)
		{
			return;
		}

		effect.Charge += 1;
		GD.Print($"CombatComponent: charge {effect.Charge}/{effect.Blueprint.ChargeMax} cfg={effect.SourceConfigId}");
		if (effect.Charge >= effect.Blueprint.ChargeMax)
		{
			HandleBurst(effect);
			m_Effects.RemoveBySourceRuntimeId(effect.SourceRuntimeId, expire: false);
			RemoveInstanceIfOrphan(effect.SourceRuntimeId);
		}
	}

	public void OnEffectExpired(EffectInstance effect)
	{
		if (!effect.BurstConsumed)
		{
			HandleBurst(effect);
		}
	}

	private void HandleBurst(EffectInstance effect)
	{
		effect.BurstConsumed = true;
		var damage = effect.Blueprint.BurstDamage;
		if (damage <= 0)
		{
			return;
		}

		var radius = effect.Blueprint.BurstRadius;
		foreach (var target in CollectTargets(SkillTargeting.EnemiesInRadius, radius))
		{
			target.Health?.TakeDamage(damage);
			GD.Print($"CombatComponent: burst hit {target.Name} dmg={damage}");
		}
	}

	private void RemoveInstanceIfOrphan(uint runtimeId)
	{
		for (var i = m_Instances.Count - 1; i >= 0; i--)
		{
			if (m_Instances[i].RuntimeId == runtimeId && !InstanceStillAlive(m_Instances[i]))
			{
				m_Instances.RemoveAt(i);
			}
		}
	}
```

满能 `RemoveBySourceRuntimeId(expire:false)`：不走 `OnEffectExpired`，避免 Burst 两次。先 `HandleBurst` 再 Remove。

- [ ] **Step 3: charge_burst 数据**

效果：`Duration=8`，`ChargeMax=3`，`BurstDamage=10`，`BurstRadius=96`，`SubscribeBasic=true`，无 ExtraHitbox。

技能：`ConfigId = "skill.player_default.charge_burst"`，`Kind=Skill`，仅 `GrantListener` 该效果。Job.Ultimate 绑定。

- [ ] **Step 4: 编译并手测**

Expected: `dotnet build` 成功。

手测：

1. V 挂充能；X 命中三次打出 `burst hit`，效果消失（`remove` 不是先 expire 再 burst 两次）。
2. V 后等 8 秒不打满：一次 `expire` + 一次 burst。
3. V 后未满再按 V：`remove` 无 burst，Charge 清零重积。
4. extra_blow（Z）与 charge_burst（V）同时存在时，一次 X 既出附加盒又 +1 充能。

- [ ] **Step 5: Commit**

```powershell
git add scripts/CombatComponent.cs scripts/EffectHolder.cs data/actors/effects/player_charge_burst.tres data/actors/skills/player_charge_burst.tres data/actors/jobs/player_default_job.tres
git commit -m "监听效果可充能并在满能或到期爆发，Replace 不爆发。"
```

---

## 规格对照（写计划后自检）

| 规格要点 | 任务 |
|----------|------|
| 普攻/战技/大招统一 `SkillDefinition` | 1, 3 |
| `AttackData` → `AttackSpec` | 3 |
| `AttackKind` 分事件通道 | 3, 6 |
| 双 Id、Replace 按 ConfigId | 3, 4 |
| 激活当帧授予 | 4, 5 |
| 技能伤害不发 Basic 事件 | 3, 6 |
| 多监听扇出、附加盒独立 AttackId | 2, 6 |
| 附加盒命中不加充能 | 3 `FromListener`、6 |
| Charge / Burst / Expire vs Replace | 7 |
| 范围 Targeting + Period Tick | 4 `CollectTargets` / `HandleEffectTick` |
| 无资源池时 Cost≠0 拒绝 | 3 |
| Basic 禁止自身持续授予 | 3 `ValidateSlot` |
| 第一期多 Hitbox 只播第一只 | 3 |
| Dodge / 投射物 / Modifier 公式 / 时间轴编辑器 | 不做 |

---

## 完成标准

1. X 普攻手感、0.2s 盒、扣血与迁移前一致；`Job.Attack` 为 `Kind=Basic`
2. Z extra_blow：当帧监听；开头技能伤害不触发附加盒；之后普攻双盒双伤害
3. 同 ConfigId 再放：旧效果 `remove` 不爆发
4. V + 普攻满 3 次或 8 秒到期各爆发一次；Z 与 V 可同时生效
5. `dotnet build ProjectC.csproj` 成功
