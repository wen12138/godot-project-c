# 短招 Cue 后周期开盒 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans（本轮用户指定会话内联执行）。Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** `ApplyEffect` 按 `ApplyEffectCue` 在招式时钟指定时刻施加；Tick 在施放者身上开短命 ExtraHitbox；样例绑大招槽。

**Architecture:** Cue 与 `ApplyAt` 只加在 `ApplyEffectModule`。有 `PlayAttack` 时模块 `OnActivate` 不施加，由 `CombatComponent` 在 elapsed 跨过解析时刻时施加一次。`OnTick` 复用 `OpenListenerHitbox`。无 `PlayAttack` 且 `Cue=PlayStartupStart` 仍当帧施加（dummy aura）。

**Tech Stack:** Godot 4.6 / C# / `Godot.NET.Sdk/4.6.2` / `net8.0`

**Spec:** `docs/superpowers/specs/2026-09-02-skill-pulse-hitbox-design.md`

## Global Constraints

- 不做自动化测试 / CI；验证用 `dotnet build` 与手测
- 不新增 `SkillDefinition` / `AttackSpec` / `GameplayEffect` 字段
- 不要 `FirstTickDelay`；不做到期宽限
- 不自挂 `_PhysicsProcess` 做玩法
- 保持 `Privot` 拼写
- 不删除 `player_charge_burst.tres` / `player_skill_burst.tres`
- 战技槽仍留爆发；脉冲样例绑 Ultimate
- Skills: `godot-prompter:resource-pattern`、`godot-prompter:ability-system`、`godot-prompter:component-system`、`godot-prompter:csharp-godot`

---

### Task 1: ApplyEffectCue 与模块 OnActivate

**Files:**
- Modify: `scripts/data/ApplyEffectModule.cs`

**Interfaces:**
- Produces: `ApplyEffectCue`（`PlayStartupStart=0` … `PlayElapsed=4`）；`ApplyEffectModule.Cue` 默认 `PlayStartupStart`；`ApplyAt` 默认 `-1`；`OnActivate` 仅在无 `PlayAttack` 且 Cue 为 `PlayStartupStart` 时立即施加

Skills: `godot-prompter:resource-pattern`、`godot-prompter:csharp-godot`

- [x] **Step 1: 写入枚举与字段、改 OnActivate**

把 `scripts/data/ApplyEffectModule.cs` 换成：

```csharp
using Godot;

public enum ApplyEffectCue
{
	PlayStartupStart = 0,
	PlayActiveStart = 1,
	PlayRecoveryStart = 2,
	PlayComplete = 3,
	PlayElapsed = 4
}

[GlobalClass]
public partial class ApplyEffectModule : SkillModule
{
	[Export]
	public GameplayEffect Effect { get; set; }

	[Export]
	public ApplyEffectCue Cue { get; set; } = ApplyEffectCue.PlayStartupStart;

	[Export]
	public float ApplyAt { get; set; } = -1f;

	public override void OnActivate(CombatComponent combat, SkillInstance instance)
	{
		if (combat == null || instance?.Definition == null)
		{
			return;
		}

		if (instance.Definition.HasPlayAttack())
		{
			return;
		}

		if (Cue != ApplyEffectCue.PlayStartupStart)
		{
			GD.PushError($"{combat.GetPath()}: ApplyEffect Cue={Cue} requires PlayAttack ({instance.ConfigId})");
			return;
		}

		combat.ApplyModuleEffect(instance, Effect, toSelfOnly: false);
	}
}
```

- [x] **Step 2: `dotnet build`**

Run: `dotnet build`（仓库根目录）  
Expected: 0 错误。`dummy_aura` 无 PlayAttack、Cue 默认 0，行为与现网当帧施加一致。

---

### Task 2: 招式时钟跨过 Cue 则施加；Tick 开 ExtraHitbox

**Files:**
- Modify: `scripts/SkillInstance.cs`
- Modify: `scripts/CombatComponent.cs`

**Interfaces:**
- Consumes: `ApplyEffectCue`、`ApplyEffectModule.Cue` / `ApplyAt`、`AttackSpec.Startup` / `Active` / `TotalDuration`、`OpenListenerHitbox`
- Produces: `PlayAttackState.AppliedEffectModules`；`TryResolveApplyCue`；`TryApplyCuedEffects`；`HandleEffectTick` 在 `effect.Target == m_Actor` 且 `ExtraHitbox != null` 时开盒

Skills: `godot-prompter:ability-system`、`godot-prompter:component-system`

- [x] **Step 1: PlayAttackState 增加已施加下标**

`scripts/SkillInstance.cs` 的 `PlayAttackState` 增加：

```csharp
public HashSet<int> AppliedEffectModules = new();
```

文件已有 `using System.Collections.Generic;`。

- [x] **Step 2: Combat 解析时刻与扫模块**

在 `CombatComponent` 中增加（`BeginPlayAttackFromSpec` 附近）：

```csharp
private static bool TryResolveApplyCue(ApplyEffectModule module, AttackSpec spec, Node owner, string configId, out float at)
{
	at = 0f;
	if (module == null || spec == null)
	{
		return false;
	}

	switch (module.Cue)
	{
		case ApplyEffectCue.PlayStartupStart:
			at = 0f;
			return true;
		case ApplyEffectCue.PlayActiveStart:
			at = Mathf.Max(0f, spec.Startup);
			return true;
		case ApplyEffectCue.PlayRecoveryStart:
			at = Mathf.Max(0f, spec.Startup) + Mathf.Max(0f, spec.Active);
			return true;
		case ApplyEffectCue.PlayComplete:
			at = spec.TotalDuration;
			return true;
		case ApplyEffectCue.PlayElapsed:
			if (module.ApplyAt < 0f)
			{
				GD.PushError($"{owner.GetPath()}: PlayElapsed ApplyAt < 0 ({configId})");
				return false;
			}

			at = Mathf.Min(module.ApplyAt, spec.TotalDuration);
			return true;
		default:
			GD.PushError($"{owner.GetPath()}: unknown ApplyEffectCue {module.Cue} ({configId})");
			return false;
	}
}

private void TryApplyCuedEffects(SkillInstance instance, float previous, float elapsed)
{
	var play = instance?.PlayAttack;
	if (play?.Spec == null || instance.Definition?.Modules == null)
	{
		return;
	}

	var modules = instance.Definition.Modules;
	for (var i = 0; i < modules.Count; i++)
	{
		if (modules[i] is not ApplyEffectModule apply)
		{
			continue;
		}

		if (play.AppliedEffectModules.Contains(i))
		{
			continue;
		}

		if (!TryResolveApplyCue(apply, play.Spec, this, instance.ConfigId, out var at))
		{
			continue;
		}

		if (previous < at && elapsed >= at)
		{
			WarnPulseTargeting(instance, apply.Effect);
			ApplyModuleEffect(instance, apply.Effect, toSelfOnly: false);
			play.AppliedEffectModules.Add(i);
		}
	}
}

private void WarnPulseTargeting(SkillInstance instance, GameplayEffect effect)
{
	if (effect?.ExtraHitbox == null || instance?.Definition == null)
	{
		return;
	}

	if (instance.Definition.Targeting != SkillTargeting.Self)
	{
		GD.PushError($"{GetPath()}: ExtraHitbox pulse requires Targeting=Self ({instance.ConfigId})");
	}
}
```

- [x] **Step 3: BeginPlayAttackFromSpec 结束时扫 Cue**

在成功写入 `instance.PlayAttack` 并 `TryOpenPlayBox(..., previous: -1f)` 之后立刻：

```csharp
TryApplyCuedEffects(instance, previous: -1f, elapsed: 0f);
```

- [x] **Step 4: TickPlayAttacks 在关招前扫 Cue**

`elapsed += dt` 且开/关盒之后、`if (play.Elapsed >= play.Total)` **之前**：

```csharp
TryApplyCuedEffects(instance, previous, play.Elapsed);
```

这样 `PlayComplete` 能在 `PlayAttack` 置空前跨过 `Total`。`CancelPlayAttack` 不要调用 `TryApplyCuedEffects`。

- [x] **Step 5: HandleEffectTick 开附加盒**

替换为：

```csharp
public void HandleEffectTick(EffectInstance effect)
{
	if (effect?.Blueprint == null)
	{
		return;
	}

	if (effect.Blueprint.TickDamage > 0 && effect.Target?.Health != null)
	{
		effect.Target.Health.TakeDamage(effect.Blueprint.TickDamage);
	}

	if (effect.Blueprint.ExtraHitbox != null && effect.Target == m_Actor)
	{
		OpenListenerHitbox(effect);
	}
}
```

- [x] **Step 6: `dotnet build`**

Run: `dotnet build`  
Expected: 0 错误 0 警告。

---

### Task 3: 脉冲样例绑 Ultimate

**Files:**
- Create: `data/actors/attacks/player_skill_pulse_open_spec.tres`
- Create: `data/actors/effects/player_skill_pulse_effect.tres`
- Create: `data/actors/skills/player_skill_pulse.tres`
- Modify: `data/actors/jobs/player_default_job.tres`（Ultimate → pulse；保留 charge_burst 文件）

**Interfaces:**
- Consumes: `Cue=1`（`PlayActiveStart`）；`Startup=0.3` `Active=0.1` `Recovery=0.1`；`Duration=20.1` `Period=1.0` `ExtraHitboxDuration=0.1`
- Produces: `ConfigId=skill.player_default.pulse`，V 键可放

Skills: `godot-prompter:resource-pattern`

- [x] **Step 1: 起手 AttackSpec**

`data/actors/attacks/player_skill_pulse_open_spec.tres`：脚本 UID 与现有 AttackSpec 一致（`uid://bcefrd1075htg`、HitboxEntry `uid://4ay68nn16gn1`）。`Startup=0.3` `Active=0.1` `Recovery=0.1` `FollowUpWindow=0`。一只盒 `Offset=Vector3(48, 0, 36)` `Size=Vector3(72, 28, 72)`，不填 Start/End。

- [x] **Step 2: 周期效果**

`data/actors/effects/player_skill_pulse_effect.tres`：`Duration=20.1` `Period=1.0` `TickDamage=0` `SubscribeBasic=true` `SubscribeSkill=false` `ExtraHitboxDuration=0.1`，内嵌 HitboxEntry 几何与起手盒相同。`ChargeMax=0` `BurstDamage=0`。

- [x] **Step 3: 技能定义**

`data/actors/skills/player_skill_pulse.tres`：`Kind=1` `Targeting=0` `Cooldown=8`。Modules：`PlayAttack`（Specs 仅 open spec）+ `ApplyEffect`（`Cue=1`，Effect 指向上一步）。`load_steps` 按 ext + sub + 主资源计数。

- [x] **Step 4: 职业槽**

`player_default_job.tres` 的 `id="6_ult"` 改为 `res://data/actors/skills/player_skill_pulse.tres`。`Skill` 仍指向 burst。

- [x] **Step 5: `dotnet build`**

Run: `dotnet build`  
Expected: 0 错误。

手测：V → 0.3s 后挂效果；1.3s 起每秒红盒 0.1s；0.5s 后可移动/普攻；前摇结束前再放会被占用挡住，无法在 Active 前 Replace 同一技能（占用中 `IsPlayOccupied`）。CD 8s 后可 Replace。

---

## Spec coverage

| 规格项 | 任务 |
|--------|------|
| Cue 枚举与 ApplyAt | Task 1 |
| 无 PlayAttack + PlayStartupStart 当帧 | Task 1 |
| 有 PlayAttack 不在 OnActivate 施加 | Task 1 |
| 跨过解析时刻施加一次 | Task 2 |
| PlayComplete 置空前施加 | Task 2 |
| Cancel 不扫未跨过 Cue | Task 2（不调用） |
| Tick ExtraHitbox 仅施放者 | Task 2 |
| Targeting≠Self + ExtraHitbox PushError | Task 2 `WarnPulseTargeting` |
| Duration=20.1 样例、绑 Ultimate | Task 3 |
| 战技槽爆发不变 | Task 3 |

本轮不提交 git（除非用户另行要求）。
