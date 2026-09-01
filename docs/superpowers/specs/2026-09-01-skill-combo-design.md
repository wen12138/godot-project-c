# 战技多段续招：按 ConfigId 的 Specs 链

日期：2026-09-01  
项目：ProjectC（Godot 4.6 / C#）  
状态：已定稿

## 目标

在既有 `SkillDefinition` + `PlayAttackModule.Specs` + 普攻连段上，给 **`Kind=Skill`（战技与大招）** 加上有序多段续招：点按打第 1 段，该段整段播完后进入续招窗，窗内再点按同一份技能接下一段。

与普攻的关键差异：

- 第 1 段激活成功的瞬间开始该 `ConfigId` 的总冷却
- 续招**只**因窗口超时而失败并清段；窗内跳、普攻、放另一份技能都不清本技能段数
- 超时或打完最后一段后，必须等这条 CD 结束才能再从第 1 段起手
- 本轮不考虑「续招窗尚未关闭时 CD 已经走完」

本轮用一份 3 段战技替换职业槽上的 extra_blow，作为可玩样例。

## 约束

- 沿用 `2026-08-31-skill-definition-design.md` 与 `2026-08-31-basic-attack-combo-design.md`：技能是 Resource 蓝图；运行时状态不得写回共享 `.tres`；判定仍是逻辑 AABB；组件不自挂 `_PhysicsProcess` 做玩法
- **不大改数据结构**：不新增 `SkillDefinition` 字段、不新增模块类型、不改 `AttackSpec` 字段。多段就是 `Specs` 长度 > 1
- 调度仍是 Player 先输入，Actor 再位移 → 判定 → 战斗
- 每一段仍是新的短寿命 `SkillInstance`，战技/大招照旧发 `SkillAttack*`
- 不做预输入；挥击进行中的键丢掉
- 完成标准：C# 编译通过 + 手动跑图；不做自动化测试 / CI
- 保持节点名拼写 `Privot`

## 对既有文档的修订

| 文档 | 旧约定 | 新约定 |
|------|--------|--------|
| `2026-08-31-basic-attack-combo-design.md` | `Kind=Skill` 的 `PlayAttack` 不走连段，始终播 `Specs[0]`；`Specs.Count > 1` 时 `PushError` | `Kind=Skill` 按本文走 ConfigId 续招表；`Specs.Count == 1` 仍只播 `[0]`、收招不开窗，不再报错 |
| 同上「清连」 | 战技/大招激活成功 → `BreakCombo()` 清普攻连 | 仍清**普攻连**。不清其它 `ConfigId` 的技能续招表 |
| `2026-08-31-skill-definition-design.md` | `Cooldown` 激活成功当帧起算 | 仍是。续招成功再激活**不**重新写入 CD。死亡时清空 CD 字典，避免节点复用脏状态 |

旧文档不逐行改写；技能多段、CD 绕过与清段以本文为准。普攻连段、占用锁、效果寿命、监听扇出、Replace 仍以既有两份规格为准。

---

## 架构

```text
Job.Skill / Job.Ultimate
  ──► SkillDefinition (Kind=Skill)
        Cooldown          总 CD；仅第 1 段（窗关着时的起手）写入
        Modules ──► PlayAttackModule
                      Specs[]  有序，长度 >= 1
                        [0] 第 1 段
                        [1] 第 2 段
                        …
                        [n] 最后一段（收招后不开窗）
```

`Specs` 长度为 1、或没有 `PlayAttack`（纯授予）：行为与现网一致，不进技能续招表。本轮样例只有 `PlayAttack`。若某份多段技能同时带授予模块，每段续招仍会 `Replace` 并再跑全部模块（与现 `TryActivate` 相同）；不另做「只在第 1 段授予」。

运行时普攻连与技能连分开记，都在 `CombatComponent`，不进 Resource。

```text
CombatComponent
  ComboNextIndex / FollowUpRemaining     仅普攻，不变
  SkillCombos[ConfigId]
      NextIndex                          下一段下标，默认 0
      FollowUpRemaining                  该技能续招窗剩余；<=0 窗关着
  CooldownRemaining[ConfigId]            已有；第 1 段起算，续招不刷新
```

Z 与 V 可以同时各有一个未过期的续招窗。按 Z 只推进战技的表项，按 V 只推进大招的表项，按 J 只动普攻那对字段。

```text
按某份 Kind=Skill 且未占用
  该 ConfigId 窗开着
    → 跳过 CD 检查，不重写 CD
    → TryActivate 成功，按 NextIndex 播 Specs[i]
  该 ConfigId 窗关着
    → CD 未转好则拒绝
    → 否则从 Specs[0] 起手，写入 Cooldown
整段播完
  ├─ 最后一段或 FollowUpWindow<=0 → 清该 ConfigId 续招表项（CD 不动）
  └─ 否则 NextIndex=i+1，开该 ConfigId 的续招窗
窗扣完 → 清该 ConfigId 续招表项（CD 不动；再按必须等 CD）
```

---

## 数据

不新增蓝图字段。

| 已有字段 | 本轮用法 |
|----------|----------|
| `SkillDefinition.Cooldown` | 总冷却（秒）。样例 `10`。`0` 仍表示无此 CD |
| `PlayAttackModule.Specs` | 有序多段。长度 1 合法 |
| `AttackSpec.Startup` / `Active` / `Recovery` | 各段窗口 |
| `AttackSpec.FollowUpWindow` | 本段 **Startup+Active+Recovery 走尽之后** 的续招窗。最后一段即使字段 > 0 也不开窗 |

非最后一段 `FollowUpWindow <= 0` 合法：该段收招后立刻清该项，下次必须等 CD（若有）从 `[0]` 起手。

`CancelOpenAt` 本轮仍不读。

---

## 运行时

### 续招判定

某 `ConfigId` **窗开着** 当且仅当：`SkillCombos` 含该项，且 `FollowUpRemaining > 0`。

窗开着时的再激活是**续招**；窗关着时的激活是**起手**。

### `TryActivate`（`Kind=Skill` 相对现逻辑的增量）

占用、`Cost`、空 `ConfigId`、无模块、离地门禁（输入侧）不变。

在创建 `SkillInstance`、跑模块、写 CD **之前**：

1. 若定义含 `PlayAttack`：按下面「取段下标」解析要播的 `AttackSpec`。无效（空列表、该项 null、无 Hitbox）→ `PushError`，`return false`。**不**建实例、**不**跑授予、**不**写 CD、**不**改续招表。
2. 起手且 `CooldownRemaining[id] > 0` → `return false`。
3. 续招 → 跳过步骤 2，且稍后**不**把 `Cooldown` 写回字典（已在走的 CD 继续扣）。

然后与现逻辑相同：`Replace` 同 `ConfigId` 旧实例 → 新 `SkillInstance` → 按模块数组 `OnActivate` → 若是起手且 `Cooldown > 0` 则写入 CD → `BreakCombo()`（只清普攻连）。

`Kind=Basic` 不读 `SkillCombos`，不改普攻 CD 语义。普攻的 Spec 无效仍 `BreakCombo()`，与现规格一致；为与「失败不算起手」对齐，Basic 在模块执行前校验失败时同样不建实例。

**取段下标**

| 种类 | 下标 |
|------|------|
| Basic | `ComboNextIndex`（越界当 0 并 `PushError`） |
| Skill 窗开着 | `SkillCombos[id].NextIndex`（越界当 0 并 `PushError`） |
| Skill 窗关着 | `0` |

`BeginPlayAttack` 必须使用同一套取下标规则，避免校验与开招不一致。取消「Skill 只用 Specs[0]」的 `PushError`。

开招成功后：Basic 仍将 `FollowUpRemaining = 0`（挥击中关普攻窗）。Skill 续招开招成功后将该 `ConfigId` 的 `FollowUpRemaining = 0`（挥击中关该技能窗，收招后再按规则开窗）。

### 收招开窗

仅该次 `PlayAttack` 的 Total 走尽之后。`Kind=Skill`：

- `i` 已是最后一项，或该段 `FollowUpWindow <= 0` → `ClearSkillCombo(configId)`
- 否则 `NextIndex = i + 1`，`FollowUpRemaining = FollowUpWindow`

`ClearSkillCombo`：从字典删除该 `ConfigId`。**不**改 `CooldownRemaining`，**不**取消正在播的 `PlayAttack`（超时发生在收招之后；挥击中窗本就关着）。

窗在 Combat `PhysicsTick` 里扣时间，仍在 `TickPlayAttacks` 之后。普攻窗与各技能窗同一帧扣。某技能扣完只 `ClearSkillCombo` 该项。

招式在 Combat tick 里收束并开窗；本帧输入已经跑过，故**最早下一物理帧**才能续招。

### 动作互斥（不改）

任意进行中的 `PlayAttack` 仍拒绝普攻、战技、大招、跳跃；位移输入忽略。同一物理帧优先级仍 **大招 > 战技 > 普攻 > 跳**。无预输入。

因此：窗内正在打普攻时按 Z，键被丢掉，窗继续扣；普攻收招后若窗还在，再按 Z 才能续招。

### 清段 / 清连

| 来源 | 普攻连 | 该技能 `SkillCombos` | 该技能 CD |
|------|--------|----------------------|-----------|
| 该技能窗口超时 | 不清 | 清该项 | 不动 |
| 该技能打完最后一段 | 不清 | 清该项 | 不动 |
| 跳跃成功 | 清（现逻辑） | 不清 | 不动 |
| 另一份技能激活成功 | 清（现逻辑） | 不清另一份的窗 | 另一份按自己规则起 CD |
| 本技能起手成功 | 清（现 `BreakCombo`） | 该项从 0 开打 | 写入 `Cooldown` |
| 本技能续招成功 | 清普攻连 | 推进该项 | 不刷新 |
| 死亡 | 清 | 清全部表项 | 清全部 CD 字典 |

「断招」在本轮可玩范围内：没打完所有段 + 窗已关 + CD 还在走。无受击硬直、无挥击中取消。

死亡沿用 `OnOwnerDied` 卸实例；本轮补上 `SkillCombos` 与 `m_CooldownRemaining` 一并清空。

---

## 错误处理

| 情况 | 行为 |
|------|------|
| `Specs` 为空或全 null | `PushError`；`TryActivate` 失败；CD / 续招表不动 |
| 本段 `Specs[i]` 为 null 或 `Hitboxes` 为空 | 同上 |
| `NextIndex` 越界 | `PushError`；当 `0` 打 |
| 非最后一段 `FollowUpWindow <= 0` | 合法，收招后清该项 |
| `Specs.Count == 1` | 合法；收招不开窗；不再报「只用 Specs[0]」 |
| 某段多只 Hitbox | 沿用：`PushError`，只用第一只 |
| 窗内再按但占用中 | 静默丢掉，窗继续扣 |
| 窗已关且 CD 未转好 | 静默拒绝 |
| 无 `PlayAttack` 的授予技 | 不进续招表；CD 仍按起手写入 |
| `Cost != 0` 等 | 仍按技能规格 |

缺依赖时 `GD.PushError` 并跳过，不抛未处理异常。

窗内 CD 走完：本轮不实现专门分支，也不在样例配置里出现（窗 3s、CD 10s）。

---

## 试玩资源

新做一份战技，替换职业槽绑定。**不删除** `player_extra_blow.tres`。

| 文件 | 内容 |
|------|------|
| `data/actors/attacks/player_skill_combo_spec_1.tres` | 前摇 0.1、判定 0.2、后摇 0.1、续招窗 3；判定盒可区分 |
| `data/actors/attacks/player_skill_combo_spec_2.tres` | 时间同第 1 段；判定盒与第 1、3 段可区分 |
| `data/actors/attacks/player_skill_combo_spec_3.tres` | 前摇 0.2、判定 0.2、后摇 0.15；最后一段不开窗 |
| `data/actors/skills/player_skill_combo.tres` | `ConfigId = skill.player_default.skill_combo`，`Kind=Skill`，`Cooldown=10`，仅 `PlayAttack`，`Specs` 为上三项 |
| `data/actors/jobs/player_default_job.tres` | `Skill` 改为 `player_skill_combo.tres` |

判定盒 Offset/Size 与现普攻三段类似地彼此错开，便于跑图看出打到第几段。

当前职业 Z 键不再装备 extra_blow，因此不再挂普攻监听附加盒。`Job.Ultimate` 仍为充能爆发，本轮不改。

---

## 文件清单（实现时）

### 修改

| 文件 | 改动 |
|------|------|
| `scripts/CombatComponent.cs` | `SkillCombos`；起手/续招 CD 门禁；开招前校验 Spec；Skill 按表取段；收招开窗；扣窗；死亡清表与 CD；去掉「Skill 只用 Specs[0]」 |
| `data/actors/jobs/player_default_job.tres` | `Skill` 改绑新战技 |

`PlayerInputComponent`、`PlayAttackModule`、`AttackSpec`、`SkillDefinition` **不改字段**。输入优先级与占用查询不改。

### 新增

上表四份攻击/技能 `.tres`。

### 不改

位移积分、属性公式、Health、Hurtbox 几何、效果寿命 / 监听扇出 / Replace 语义、存档、Dodge 槽、`Privot` 拼写、`player_extra_blow.tres` 内容。

---

## 范围外

- 预输入、`CancelOpenAt`、后摇取消进下一段
- 受击硬直、霸体、被打清段（死亡除外）
- 把普攻连收进同一张 `Dictionary`
- 窗内 CD 走完的规则
- 技能 UI、CD 条、段数提示
- 自动化测试 / CI
- 将 `Privot` 更正为 `Pivot`

---

## 完成标准

1. Player 按 Z：第 1 段出手即开始 10 秒 CD；只按一次只出第 1 段；超时后再按，CD 没好转不出第 1 段
2. 每段收招后 3 秒内再按 Z：1→2→3；第 3 段后再按，CD 没好转不出
3. 窗内可以走、跳、打普攻、按 V；这些动作不掉战技段数；未占用且未超时再按 Z 仍接下一段
4. 挥击中不能走、不能跳、不能普攻、不能放另一技能（现有占用锁）
5. `player_extra_blow.tres` 仍在工程中；当前职业 Z 键不再触发监听附加盒
6. `dotnet build` 成功

## 实现时建议加载的技能

- `godot-prompter:resource-pattern` — 新 `.tres` 与现有 `AttackSpec` / `SkillDefinition` 资产
- `godot-prompter:ability-system` — 激活门禁、CD、续招绕过
- `godot-prompter:component-system` — 状态留在 Combat，输入不钻续招表
- `godot-prompter:input-handling` — 占用时丢键、无预输入（不改优先级）
- `godot-prompter:godot-testing` — 本规格不要求自动化测试，若补测再加载
