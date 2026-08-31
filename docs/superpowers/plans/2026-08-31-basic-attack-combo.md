# 普攻连段 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 职业普攻按 `PlayAttackModule.Specs` 有序连段，收招后开续招窗；Player 普攻 / 跳跃+移动 / 战技 / 大招互斥。

**Architecture:** `Job.Attack` 仍是一份 `Kind=Basic` 的 `SkillDefinition`。`PlayAttackModule.Spec` 换成 `Specs` 数组。`CombatComponent` 持有 `ComboNextIndex` 与 `FollowUpRemaining`，收招后开窗。任意进行中的 `PlayAttack` 占用期间拒绝其它互斥动作。每一刀仍是新 `SkillInstance`，发 `BasicAttack*`。

**Tech Stack:** Godot 4.6 / C# / `Godot.NET.Sdk/4.6.2` / `net8.0`

**Spec:** `docs/superpowers/specs/2026-08-31-basic-attack-combo-design.md`

## Global Constraints

- 不做自动化测试 / CI；验证用 `dotnet build` 与手测清单
- 不消费 `CancelOpenAt`，不做预输入
- 技能 `PlayAttack` 不走连段，只播 `Specs[0]`
- 不自挂 `_PhysicsProcess` 做玩法
- 保持 `Privot` 拼写
- Skills: `godot-prompter:resource-pattern`、`godot-prompter:ability-system`、`godot-prompter:component-system`、`godot-prompter:input-handling`、`godot-prompter:player-controller`

---

### Task 1: AttackSpec 续招窗与 PlayAttackModule.Specs

**Files:**
- Modify: `scripts/data/AttackSpec.cs`
- Modify: `scripts/data/PlayAttackModule.cs`
- Modify: `data/actors/skills/player_default_attack.tres`
- Modify: `data/actors/skills/player_extra_blow.tres`

**Interfaces:**
- Consumes: 现有 `AttackSpec`、`PlayAttackModule.OnActivate`
- Produces: `AttackSpec.FollowUpWindow`（默认 `0.5f`）；`PlayAttackModule.Specs`（`Array<AttackSpec>`）；`OnActivate` 仍调用 `BeginPlayAttack`，暂传 `Specs` 第 0 项以保持现有单刀行为

- [ ] **Step 1: 给 `AttackSpec` 加 `FollowUpWindow`**

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
	public float FollowUpWindow { get; set; } = 0.5f;

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

- [ ] **Step 2: `PlayAttackModule` 改为 `Specs` 数组**

```csharp
using Godot;

[GlobalClass]
public partial class PlayAttackModule : SkillModule
{
	[Export]
	public Godot.Collections.Array<AttackSpec> Specs { get; set; } = new();

	public override void OnActivate(CombatComponent combat, SkillInstance instance)
	{
		if (combat == null)
		{
			return;
		}

		combat.BeginPlayAttack(instance, this);
	}
}
```

本任务先把 `CombatComponent.BeginPlayAttack` 改成接受 `PlayAttackModule`，内部仍只用 `Specs[0]`（连段在 Task 2）。签名：

```csharp
public void BeginPlayAttack(SkillInstance instance, PlayAttackModule module)
{
	if (instance == null || module == null)
	{
		GD.PushError($"{GetPath()}: BeginPlayAttack missing instance or module");
		return;
	}

	var spec = GetFirstSpec(module);
	if (spec == null)
	{
		GD.PushError($"{GetPath()}: AttackSpec list is empty ({instance.ConfigId})");
		return;
	}

	BeginPlayAttack(instance, spec);
}

private static AttackSpec GetFirstSpec(PlayAttackModule module)
{
	if (module.Specs == null)
	{
		return null;
	}

	foreach (var spec in module.Specs)
	{
		if (spec != null)
		{
			return spec;
		}
	}

	return null;
}
```

保留原 `BeginPlayAttack(SkillInstance, AttackSpec)` 私有或内部实现，避免 Task 1 改变手感。`PlayAttackModule.OnActivate` 不再传 `Spec`。

- [ ] **Step 3: 迁移两份技能 `.tres`**

`player_default_attack.tres` 中 `Spec = ExtResource("3_spec")` 改为：

```
Specs = Array[AttackSpec]([ExtResource("3_spec")])
```

`player_extra_blow.tres` 同样把 poke 的 `Spec` 改成一项的 `Specs`。

Godot 4 文本资源里类型名用脚本类：`Array[ExtResource("...HitboxEntry 或 AttackSpec 脚本")]`。参照现有 `Hitboxes = Array[ExtResource("2_entry")]([...])` 写法，`Specs` 应对 `AttackSpec.cs` 的 ext_resource。若 AttackSpec 已作为独立 `.tres` 引用，数组元素用该 `ExtResource`。

- [ ] **Step 4: `dotnet build`**

在项目根执行：

```
dotnet build
```

Expected: 成功，0 Error。现有 X 普攻仍是单刀 0.2s。

- [ ] **Step 5: Commit**

```
git add scripts/data/AttackSpec.cs scripts/data/PlayAttackModule.cs scripts/CombatComponent.cs data/actors/skills/player_default_attack.tres data/actors/skills/player_extra_blow.tres
git commit -m "普攻 PlayAttack 改为 Specs 数组，AttackSpec 增加续招窗字段。"
```

---

### Task 2: Combat 连段状态与收招开窗

**Files:**
- Modify: `scripts/SkillInstance.cs`
- Modify: `scripts/CombatComponent.cs`

**Interfaces:**
- Consumes: `PlayAttackModule.Specs`、`AttackSpec.FollowUpWindow`、`AttackSpec.TotalDuration`
- Produces: `CombatComponent.BreakCombo()`、`IsPlayOccupied`、`ComboNextIndex` / `FollowUpRemaining`；Basic 按索引播 `Specs[i]`；Skill 只播 `[0]`

- [ ] **Step 1: `PlayAttackState` 记下本刀下标**

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
	public int ComboIndex;
	public bool IsLastComboHit;
}
```

- [ ] **Step 2: Combat 增加连段字段与 Reset / Break**

在 `CombatComponent` 字段区增加：

```csharp
private int m_ComboNextIndex;
private float m_FollowUpRemaining;

public bool IsPlayOccupied => FindAnyPlayAttack() != null;

public void BreakCombo()
{
	m_ComboNextIndex = 0;
	m_FollowUpRemaining = 0f;
}
```

`FindAnyPlayAttack`：任一 `instance.PlayAttack != null` 即返回该实例。`TryActivate` 把原来的 `Kind == Basic && FindBasicPlayAttack() != null` 换成 `FindAnyPlayAttack() != null`（Basic 与 Skill 共用占用锁）。

- [ ] **Step 3: `BeginPlayAttack(instance, module)` 按 Kind 选下标**

```csharp
public void BeginPlayAttack(SkillInstance instance, PlayAttackModule module)
{
	if (instance == null || module == null)
	{
		GD.PushError($"{GetPath()}: BeginPlayAttack missing instance or module");
		return;
	}

	var specs = module.Specs;
	if (specs == null || CountNonNull(specs) == 0)
	{
		GD.PushError($"{GetPath()}: AttackSpec list is empty ({instance.ConfigId})");
		if (instance.Kind == AttackKind.Basic)
		{
			BreakCombo();
		}

		return;
	}

	var index = 0;
	if (instance.Kind == AttackKind.Basic)
	{
		index = m_ComboNextIndex;
		if (index < 0 || index >= specs.Count)
		{
			GD.PushError($"{GetPath()}: ComboNextIndex={index} out of range ({instance.ConfigId})");
			index = 0;
		}
	}
	else if (specs.Count > 1)
	{
		GD.PushError($"{GetPath()}: Skill PlayAttack uses Specs[0] only ({instance.ConfigId})");
	}

	var spec = specs[index];
	if (spec == null || spec.Hitboxes == null || spec.Hitboxes.Count == 0)
	{
		GD.PushError($"{GetPath()}: invalid AttackSpec at {index} ({instance.ConfigId})");
		if (instance.Kind == AttackKind.Basic)
		{
			BreakCombo();
		}

		return;
	}

	if (instance.Kind == AttackKind.Basic)
	{
		m_FollowUpRemaining = 0f;
	}

	BeginPlayAttackFromSpec(instance, spec, index, isLast: index >= specs.Count - 1);
}
```

把现有 `BeginPlayAttack(instance, spec)` 改名为 `BeginPlayAttackFromSpec`，并给 `PlayAttackState` 赋 `ComboIndex` / `IsLastComboHit`。多盒仍 `PushError` 只用第一只。

`TryActivate` 在 `Kind == Skill` 且最终会 `return true` 之前调用 `BreakCombo()`。不要在 Basic 成功时清连。

- [ ] **Step 4: 收招开窗，并 tick 续招窗**

`TickPlayAttacks` 在 `instance.PlayAttack = null` 之前，若 `instance.Kind == AttackKind.Basic`：

```csharp
if (instance.Kind == AttackKind.Basic)
{
	if (play.IsLastComboHit || play.Spec == null || play.Spec.FollowUpWindow <= 0f)
	{
		BreakCombo();
	}
	else
	{
		m_ComboNextIndex = play.ComboIndex + 1;
		m_FollowUpRemaining = play.Spec.FollowUpWindow;
	}
}
```

`PhysicsTick` 在 `TickPlayAttacks` 之后：

```csharp
TickFollowUpWindow(dt);
```

```csharp
private void TickFollowUpWindow(float dt)
{
	if (m_FollowUpRemaining <= 0f)
	{
		return;
	}

	m_FollowUpRemaining -= dt;
	if (m_FollowUpRemaining <= 0f)
	{
		BreakCombo();
	}
}
```

`OnOwnerDied` 末尾调用 `BreakCombo()`。

- [ ] **Step 5: `dotnet build`**

Expected: 成功。此时 Player 仍是 1 段 `Specs`，行为应与单刀相同（最后一项不开窗）。多段数据在 Task 4 才配。

- [ ] **Step 6: Commit**

```
git add scripts/SkillInstance.cs scripts/CombatComponent.cs
git commit -m "普攻按 Specs 下标连段，收招后开启续招窗。"
```

---

### Task 3: Player 动作互斥与跳跃清连

**Files:**
- Modify: `scripts/MovementComponent.cs`
- Modify: `scripts/PlayerInputComponent.cs`

**Interfaces:**
- Consumes: `CombatComponent.IsPlayOccupied`、`BreakCombo()`、`TryStartAttack/Skill/Ultimate`
- Produces: `MovementComponent.Jump() -> bool`；`IsOnGround`；输入优先级大招 > 战技 > 普攻 > 跳；占用时移动输入清零；离地拒绝普攻 / 战技 / 大招

- [ ] **Step 1: `Jump` 返回 bool，公开落地**

```csharp
public bool IsOnGround
{
	get
	{
		if (m_Transform == null)
		{
			return true;
		}

		return IsGrounded(m_Transform.GetVirtualZ());
	}
}

public bool Jump()
{
	if (m_MovementConfig == null || m_Transform == null)
	{
		return false;
	}

	if (!IsGrounded(m_Transform.GetVirtualZ()))
	{
		return false;
	}

	m_VerticalVelocity = m_MovementConfig.BaseJumpForce;
	return true;
}
```

删除旧的 `public void Jump()`。

- [ ] **Step 2: 重写 `PlayerInputComponent.PhysicsTick`**

```csharp
public void PhysicsTick(double delta)
{
	_ = delta;
	var playBusy = m_Combat != null && m_Combat.IsPlayOccupied;
	var grounded = m_Movement == null || m_Movement.IsOnGround;

	if (m_Movement != null)
	{
		m_Movement.SetMoveInput(playBusy ? Vector2.Zero : InputActions.GetMoveVector());
	}

	if (m_Combat == null)
	{
		if (m_Movement != null && InputActions.IsJumpJustPressed() && !playBusy)
		{
			m_Movement.Jump();
		}

		return;
	}

	if (InputActions.IsUltimateJustPressed())
	{
		if (grounded && !playBusy)
		{
			m_Combat.TryStartUltimate();
		}

		return;
	}

	if (InputActions.IsSkillJustPressed())
	{
		if (grounded && !playBusy)
		{
			m_Combat.TryStartSkill();
		}

		return;
	}

	if (InputActions.IsAttackJustPressed())
	{
		if (grounded && !playBusy)
		{
			m_Combat.TryStartAttack();
		}

		return;
	}

	if (InputActions.IsJumpJustPressed() && !playBusy && m_Movement != null)
	{
		if (m_Movement.Jump())
		{
			m_Combat.BreakCombo();
		}
	}
}
```

同一帧多个互斥键因 `return` 只走最高优先级。占用或离地时仍消费该键，不落到更低优先级。

- [ ] **Step 3: `dotnet build`**

Expected: 成功。手测：挥击中 WASD 不位移、空格不跳、Z/V 无效；空中 X/Z/V 无效；落地可同时走和跳。

- [ ] **Step 4: Commit**

```
git add scripts/MovementComponent.cs scripts/PlayerInputComponent.cs
git commit -m "普攻与技能占用期间互斥跳跃和移动，成功起跳清连。"
```

---

### Task 4: Player 三段普攻配置

**Files:**
- Create: `data/actors/attacks/player_melee_spec_2.tres`
- Create: `data/actors/attacks/player_melee_spec_3.tres`
- Modify: `data/actors/skills/player_default_attack.tres`

**Interfaces:**
- Consumes: `AttackSpec`、`PlayAttackModule.Specs`
- Produces: 至少 3 段可区分判定盒的 Player 普攻

- [ ] **Step 1: 第 2 段 `player_melee_spec_2.tres`**

与第 1 段相同 `Active=0.2`，盒子更靠前、略大：

```
Offset = Vector3(72, 0, 36)
Size = Vector3(88, 28, 80)
```

`FollowUpWindow` 可不写，走默认 0.5。

- [ ] **Step 2: 第 3 段 `player_melee_spec_3.tres`**

再靠前、再大（最后一段，窗字段可留默认，运行时不开窗）：

```
Offset = Vector3(96, 0, 36)
Size = Vector3(104, 28, 88)
```

- [ ] **Step 3: `player_default_attack.tres` 的 `Specs` 改为三项**

按顺序引用 spec、spec_2、spec_3。`load_steps` 相应增加。

- [ ] **Step 4: `dotnet build`**

Expected: 成功。

- [ ] **Step 5: 手测清单（有编辑器时）**

1. 只按一下 X：第 1 段盒子；等 0.5s 再按仍是第 1 段
2. 每段收招后立刻再按：1→2→3，第 3 段后再按回到 1
3. 挥击中连按不变段
4. 窗内跳，落地后再 X：第 1 段；滞空 X/Z/V 无效
5. 窗内 Z：清连；戳击期间不能 X；收招后 X 仍能叠 extra_blow 附加盒
6. 挥击中不能走

- [ ] **Step 6: Commit**

```
git add data/actors/attacks/player_melee_spec_2.tres data/actors/attacks/player_melee_spec_3.tres data/actors/skills/player_default_attack.tres
git commit -m "为默认职业配置三段判定盒不同的普攻连段。"
```

---

## Spec coverage

| 规格条目 | Task |
|----------|------|
| `FollowUpWindow` 默认 0.5、`Specs` 有序不设上限 | 1 |
| 最后一项不开窗；1 段等于单刀 | 2（逻辑）+ 4（3 段数据） |
| 收招后开窗、无预输入 | 2 |
| 占用锁、互斥优先级、跳成功清连、死亡清连 | 2–3 |
| Skill 只播 `[0]`、监听战后仍可普攻 | 2 + 既有 EffectHolder |
| Player 至少 3 段可区分盒子 | 4 |
| `CancelOpenAt` / 预输入 / 自动测试 | 明确不做 |
