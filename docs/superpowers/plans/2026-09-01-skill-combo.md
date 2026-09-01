# 战技多段续招 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `Kind=Skill` 按 `PlayAttackModule.Specs` 有序续招；第 1 段起手写入总 CD，窗内再按绕过 CD 且不刷新；超时或打完最后一段后必须等 CD 才能再从第 1 段起手。

**Architecture:** 蓝图不改字段。`CombatComponent` 为每个技能 `ConfigId` 另记 `SkillComboState`（与普攻 `ComboNextIndex` / `FollowUpRemaining` 分开）。`TryActivate` 在建实例前校验要播的 `AttackSpec`；续招跳过 CD 检查且不重写 CD。收招后开该 ConfigId 的窗。死亡清空技能续招表与 CD 字典。

**Tech Stack:** Godot 4.6 / C# / `Godot.NET.Sdk/4.6.2` / `net8.0`

**Spec:** `docs/superpowers/specs/2026-09-01-skill-combo-design.md`

## Global Constraints

- 不做自动化测试 / CI；验证用 `dotnet build` 与手测清单
- 不新增 `SkillDefinition` / `AttackSpec` / 模块字段
- 不消费 `CancelOpenAt`，不做预输入
- 窗内跳 / 普攻 / 另一技能不清本技能续招表
- 不考虑窗内 CD 走完
- 不自挂 `_PhysicsProcess` 做玩法
- 保持 `Privot` 拼写
- 不删除 `player_extra_blow.tres`
- Skills: `godot-prompter:resource-pattern`、`godot-prompter:ability-system`、`godot-prompter:component-system`、`godot-prompter:input-handling`

---

### Task 1: Combat 技能续招表、CD 绕过、收招开窗

**Files:**
- Modify: `scripts/CombatComponent.cs`

**Interfaces:**
- Consumes: 现有 `TryActivate`、`BeginPlayAttack`、`TickPlayAttacks`、`TickFollowUpWindow`、`OnOwnerDied`、`PlayAttackModule.Specs`、`AttackSpec.FollowUpWindow`
- Produces: `SkillComboState`（`NextIndex`、`FollowUpRemaining`）；`IsSkillFollowUpOpen(configId)`；`ClearSkillCombo(configId)`；`TryResolvePlaySpec` / `ResolveComboIndex`；Skill 收招开窗；死亡清 `m_SkillCombos` 与 `m_CooldownRemaining`

Skills: `godot-prompter:ability-system`、`godot-prompter:component-system`

- [ ] **Step 1: 增加技能续招表与辅助方法**

在 `m_FollowUpRemaining` 旁增加：

```csharp
private struct SkillComboState
{
	public int NextIndex;
	public float FollowUpRemaining;
}

private readonly Dictionary<string, SkillComboState> m_SkillCombos = new();
```

在 `BreakCombo` 之后增加（仅清技能表项，不动 CD、不动普攻连）：

```csharp
public void ClearSkillCombo(string configId)
{
	if (string.IsNullOrEmpty(configId))
	{
		return;
	}

	m_SkillCombos.Remove(configId);
}

private bool IsSkillFollowUpOpen(string configId)
{
	return !string.IsNullOrEmpty(configId)
		&& m_SkillCombos.TryGetValue(configId, out var state)
		&& state.FollowUpRemaining > 0f;
}

private static PlayAttackModule FindPlayAttackModule(SkillDefinition def)
{
	if (def?.Modules == null)
	{
		return null;
	}

	foreach (var module in def.Modules)
	{
		if (module is PlayAttackModule play)
		{
			return play;
		}
	}

	return null;
}

private int ResolveComboIndex(SkillDefinition def, int specCount)
{
	var index = 0;
	if (def.Kind == AttackKind.Basic)
	{
		index = m_ComboNextIndex;
	}
	else if (IsSkillFollowUpOpen(def.ConfigId))
	{
		index = m_SkillCombos[def.ConfigId].NextIndex;
	}

	if (index < 0 || index >= specCount)
	{
		GD.PushError($"{GetPath()}: combo index {index} out of range ({def.ConfigId})");
		index = 0;
	}

	return index;
}

private bool TryResolvePlaySpec(SkillDefinition def, out AttackSpec spec, out int index)
{
	spec = null;
	index = 0;
	if (def == null || !def.HasPlayAttack())
	{
		return true;
	}

	var module = FindPlayAttackModule(def);
	var specs = module?.Specs;
	if (specs == null || CountNonNull(specs) == 0)
	{
		GD.PushError($"{GetPath()}: AttackSpec list is empty ({def.ConfigId})");
		if (def.Kind == AttackKind.Basic)
		{
			BreakCombo();
		}

		return false;
	}

	index = ResolveComboIndex(def, specs.Count);
	spec = specs[index];
	if (spec == null || spec.Hitboxes == null || spec.Hitboxes.Count == 0)
	{
		GD.PushError($"{GetPath()}: invalid AttackSpec at {index} ({def.ConfigId})");
		if (def.Kind == AttackKind.Basic)
		{
			BreakCombo();
		}

		return false;
	}

	return true;
}
```

`FindPlayAttackModule` 取模块数组中**第一个** `PlayAttackModule`（本项目每份定义至多一个）。

- [ ] **Step 2: 改 `TryActivate`——先校验 Spec，续招绕过 CD**

占用检查之后、建实例之前：

```csharp
if (def.HasPlayAttack() && !TryResolvePlaySpec(def, out _, out _))
{
	return false;
}

var followUp = def.Kind == AttackKind.Skill && IsSkillFollowUpOpen(def.ConfigId);
if (!followUp && m_CooldownRemaining.TryGetValue(def.ConfigId, out var cdLeft) && cdLeft > 0f)
{
	return false;
}
```

删掉原来无条件的 CD 拒绝块（已并入上段）。

写 CD 改为仅起手：

```csharp
if (!followUp && def.Cooldown > 0f)
{
	m_CooldownRemaining[def.ConfigId] = def.Cooldown;
}
```

`Kind == Skill` 时仍 `ReplaceByConfigId` 与 `BreakCombo()`（只清普攻连）。续招成功也 `BreakCombo()`，但**不得** `ClearSkillCombo`。

- [ ] **Step 3: `BeginPlayAttack` 与普攻共用取下标，取消「只用 Specs[0]」**

用 `ResolveComboIndex(instance.Definition, specs.Count)` 替换 Basic/Skill 分叉里的 `PushError Skill uses Specs[0]`。`instance.Definition` 为空时 `PushError` 并 return。

空列表 / 无效 Spec 的 `PushError` 保留（防御）；Skill 失败**不要** `BreakCombo`（普攻失败仍 `BreakCombo`）。

开招成功后关窗（挥击中不算窗开着）：

```csharp
if (instance.Kind == AttackKind.Basic)
{
	m_FollowUpRemaining = 0f;
}
else if (instance.Kind == AttackKind.Skill
	&& m_SkillCombos.TryGetValue(instance.ConfigId, out var skillCombo))
{
	skillCombo.FollowUpRemaining = 0f;
	m_SkillCombos[instance.ConfigId] = skillCombo;
}
```

`TickFollowUpWindow` 对技能表：`FollowUpRemaining <= 0` 的表项**跳过、不删除**（挥击中关窗时 NextIndex 仍可能留着，收招逻辑会覆写或 `ClearSkillCombo`）。

- [ ] **Step 4: 收招开窗、扣窗、死亡清空**

`TickPlayAttacks` 在 Basic 收招分支旁增加 Skill：

```csharp
if (instance.Kind == AttackKind.Skill)
{
	if (play.IsLastComboHit || play.Spec == null || play.Spec.FollowUpWindow <= 0f)
	{
		ClearSkillCombo(instance.ConfigId);
	}
	else
	{
		m_SkillCombos[instance.ConfigId] = new SkillComboState
		{
			NextIndex = play.ComboIndex + 1,
			FollowUpRemaining = play.Spec.FollowUpWindow
		};
	}
}
```

把 `TickFollowUpWindow` 改成先扣普攻窗（逻辑不变），再扣技能窗：

```csharp
private void TickFollowUpWindow(float dt)
{
	if (m_FollowUpRemaining > 0f)
	{
		m_FollowUpRemaining -= dt;
		if (m_FollowUpRemaining <= 0f)
		{
			BreakCombo();
		}
	}

	if (m_SkillCombos.Count == 0)
	{
		return;
	}

	var keys = new List<string>(m_SkillCombos.Keys);
	foreach (var key in keys)
	{
		var state = m_SkillCombos[key];
		if (state.FollowUpRemaining <= 0f)
		{
			continue;
		}

		state.FollowUpRemaining -= dt;
		if (state.FollowUpRemaining <= 0f)
		{
			m_SkillCombos.Remove(key);
		}
		else
		{
			m_SkillCombos[key] = state;
		}
	}
}
```

`OnOwnerDied` 在现有 `BreakCombo()` 之后：

```csharp
m_SkillCombos.Clear();
m_CooldownRemaining.Clear();
```

- [ ] **Step 5: `dotnet build`**

在项目根：

```
dotnet build
```

Expected: 成功，0 Error。

- [ ] **Step 6: Commit**（仅当用户明确要求提交时执行）

```
git add scripts/CombatComponent.cs
git commit -m "战技按 ConfigId 续招，第 1 段起手开始冷却。"
```

---

### Task 2: 三阶段技资源并绑到职业槽

**Files:**
- Create: `data/actors/attacks/player_skill_combo_spec_1.tres`
- Create: `data/actors/attacks/player_skill_combo_spec_2.tres`
- Create: `data/actors/attacks/player_skill_combo_spec_3.tres`
- Create: `data/actors/skills/player_skill_combo.tres`
- Modify: `data/actors/jobs/player_default_job.tres`

**Interfaces:**
- Consumes: 现有 `AttackSpec` / `SkillDefinition` / `PlayAttackModule` 脚本 UID
- Produces: `ConfigId = skill.player_default.skill_combo`，`Kind=Skill`，`Cooldown=10`，三段 Specs；`Job.Skill` 指向新资源；`player_extra_blow.tres` 仍在工程中

Skills: `godot-prompter:resource-pattern`

脚本 UID（与现有资产一致，禁止新造脚本 UID）：

- `AttackSpec.cs`：`uid://bcefrd1075htg`
- `HitboxEntry.cs`：`uid://4ay68nn16gn1`
- `SkillDefinition.cs`：`uid://bunw65kkbmxys`
- `SkillModule.cs`：`uid://ihoicuf6r0uw`
- `PlayAttackModule.cs`：`uid://rshcksbj33gl`

新 `.tres` 自身可以不写 `uid=`（与 `player_melee_spec_2.tres` 相同，交给编辑器生成）。

- [ ] **Step 1: 写第 1 段 AttackSpec**

`data/actors/attacks/player_skill_combo_spec_1.tres`：

```
[gd_resource type="Resource" script_class="AttackSpec" format=3]

[ext_resource type="Script" uid="uid://bcefrd1075htg" path="res://scripts/data/AttackSpec.cs" id="1_spec"]
[ext_resource type="Script" uid="uid://4ay68nn16gn1" path="res://scripts/data/HitboxEntry.cs" id="2_entry"]

[sub_resource type="Resource" id="Hitbox_1"]
script = ExtResource("2_entry")
Offset = Vector3(84, 0, 36)
Size = Vector3(56, 28, 72)

[resource]
script = ExtResource("1_spec")
Startup = 0.1
Active = 0.2
Recovery = 0.1
FollowUpWindow = 3.0
Hitboxes = Array[ExtResource("2_entry")]([SubResource("Hitbox_1")])
```

- [ ] **Step 2: 写第 2 段 AttackSpec**

`data/actors/attacks/player_skill_combo_spec_2.tres`：时间字段与第 1 段相同；判定盒：

```
Offset = Vector3(72, 0, 36)
Size = Vector3(88, 28, 80)
```

其余与 Step 1 相同（含 `FollowUpWindow = 3.0`）。

- [ ] **Step 3: 写第 3 段 AttackSpec**

`data/actors/attacks/player_skill_combo_spec_3.tres`：

```
Startup = 0.2
Active = 0.2
Recovery = 0.15
Offset = Vector3(96, 0, 36)
Size = Vector3(104, 28, 88)
```

不要设 `FollowUpWindow`（默认 0.5）；运行时因 `IsLastComboHit` 不开窗。

- [ ] **Step 4: 写技能定义并改职业槽**

`data/actors/skills/player_skill_combo.tres` 参照 `player_default_attack.tres` 的 `Specs` 数组写法，但：

```
ConfigId = "skill.player_default.skill_combo"
Kind = 1
Cooldown = 10.0
```

`Modules` 仅一个 `PlayAttackModule`，`Specs` 为 spec_1、spec_2、spec_3 三个 `ExtResource`。

`player_default_job.tres` 把 id `5_skill` 的 path 从 `player_extra_blow.tres` 改为 `res://data/actors/skills/player_skill_combo.tres`。不要删 extra_blow 文件，也不要改 `Ultimate`。

- [ ] **Step 5: `dotnet build`**

```
dotnet build
```

Expected: 成功，0 Error。

手测清单（本任务不强制在 agent 内跑游戏，实现者在编辑器 F5）：

1. Z 出第 1 段后等超过 3 秒再按 Z，CD 没好转不出
2. 每段收招后立刻再按 Z：1→2→3；第 3 段后再按转不出
3. 第 1 段收招后窗内跳、普攻、按 V，再按 Z 仍是第 2 段
4. 挥击中不能走 / 跳 / 普攻 / 另一技能
5. 当前 Z 不再给普攻挂 extra_blow 附加盒

- [ ] **Step 6: Commit**（仅当用户明确要求提交时执行）

```
git add data/actors/attacks/player_skill_combo_spec_1.tres data/actors/attacks/player_skill_combo_spec_2.tres data/actors/attacks/player_skill_combo_spec_3.tres data/actors/skills/player_skill_combo.tres data/actors/jobs/player_default_job.tres
git commit -m "新增三段续招战技并替换默认职业战技槽。"
```
