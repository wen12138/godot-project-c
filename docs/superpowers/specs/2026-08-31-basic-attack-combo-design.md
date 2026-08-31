# 普攻连段：有序 AttackSpec 链

日期：2026-08-31  
项目：ProjectC（Godot 4.6 / C#）  
状态：已定稿

## 目标

在已落地的 `SkillDefinition` + `TryActivate` 上，给职业普攻加上**有序多段**：点按普攻打第 1 段；该段整段播完后进入续招窗，窗内再点按接下一段；漏窗或打完列表最后一项后，下次从第 1 段开始。每段是完整 `AttackSpec`（各自前摇 / 判定 / 后摇 / 判定盒 / 续招窗）。

本轮同时落地 Player 动作互斥：普攻、（跳跃+移动）、战技、大招四类占用不能并行；跳跃与移动彼此不互斥。

## 约束

- 沿用 `2026-08-31-skill-definition-design.md`：技能是 Resource 蓝图；运行时状态不得写回共享 `.tres`；判定仍是逻辑 AABB；组件不自挂 `_PhysicsProcess` 做玩法
- 调度仍是 Player 先输入，Actor 再位移 → 判定 → 战斗
- `Job.Attack` 仍是一份 `Kind=Basic` 的 `SkillDefinition`，不另开职业槽
- 每一刀普攻仍是新的短寿命 `SkillInstance`，照旧发 `BasicAttackStarted` / `BasicAttackHit`
- 不做预输入；挥击进行中的普攻键丢掉
- 完成标准：C# 编译通过 + 手动跑图；不做自动化测试 / CI
- 保持节点名拼写 `Privot`

## 对既有文档的修订

| 文档 | 旧约定 | 新约定 |
|------|--------|--------|
| `2026-08-31-skill-definition-design.md` | 不实现续招状态机；`CancelOpenAt` 字段先落地不消费；`Kind==Basic` 且已有 Basic `PlayAttack` 则拒绝 | 普攻连段按本文；`CancelOpenAt` 仍不消费。占用锁升级为「任意进行中的 `PlayAttack`」（Basic 与 Skill 都算） |
| 技能规格「Basic 招式锁」 | 只挡下一刀 Basic | 任意 `PlayAttack` 占用期间拒绝普攻、战技、大招、跳跃；位移输入忽略 |
| 输入规格 | 跳 / 普攻 / 战技 / 大招可在同一帧各自尝试 | 同一物理帧只执行一个互斥动作，优先级大招 > 战技 > 普攻 > 跳 |

旧文档不逐行改写；连段、占用与清连以本文为准。技能模块、效果寿命、监听扇出、Replace 仍以技能规格为准。

---

## 架构

```text
Job.Attack ──► SkillDefinition (Kind=Basic)
                 Modules ──► PlayAttackModule
                               Specs[]  AttackSpec 有序，长度 >= 1，不设上限
                                          [0] 第 1 段
                                          [1] 第 2 段
                                          …
                                          [n] 最后一段（收招后不开窗）
```

`Kind=Skill` 的 `PlayAttack` **不走连段**，始终播 `Specs[0]`。

运行时只在 `CombatComponent` 记连段；`PlayAttackModule` 只提供列表。

```text
按普攻且未占用
  → TryActivate(Job.Attack)
  → 用 ComboNextIndex 取 Specs[i] 开招
  → 新 SkillInstance，发 Basic*
  → 整段播完
       ├─ 最后一段或 FollowUpWindow<=0 → ResetCombo
       └─ 否则 ComboNextIndex=i+1，开续招窗
窗内再按 → 播下一项
窗扣完 → ResetCombo（下次从 [0]）
```

---

## 数据

### `PlayAttackModule`

| 字段 | 类型 | 含义 |
|------|------|------|
| `Specs` | `Array[AttackSpec]` | 有序招式列表。取代原单字段 `Spec` |

空数组或全是 null：`PushError`，这次 `PlayAttack` 跳过；若是 Basic 则激活失败。

### `AttackSpec` 新增

| 字段 | 类型 | 默认 | 含义 |
|------|------|------|------|
| `FollowUpWindow` | `float` | `0.5` | 本段 **Startup+Active+Recovery 走尽之后** 的续招窗（秒） |

`Startup` / `Active` / `Recovery` / `Hitboxes` / `CancelOpenAt` 不变。`CancelOpenAt` 本轮仍不读。

列表最后一项即使 `FollowUpWindow` 仍为 0.5，运行时也不开窗。非最后一段 `FollowUpWindow <= 0` 合法：该刀收招后立刻收链，下次从 `[0]` 开始。

只有 1 项时，行为与现单刀普攻相同（它就是最后一项，不开窗）。

---

## 运行时

### 连段状态（`CombatComponent`，不进 Resource）

| 状态 | 含义 |
|------|------|
| `ComboNextIndex` | 下一刀要播的下标，默认 `0` |
| `FollowUpRemaining` | 续招窗剩余秒数；`<= 0` 表示窗关着 |

**按普攻**

1. 动作占用检查失败 → return（见下节）
2. `TryActivate(Job.Attack)`
3. 用当前 `ComboNextIndex` 取 `Specs[i]`；该项无效则激活失败，`ResetCombo()`
4. 激活成功：`FollowUpRemaining = 0`；`BeginPlayAttack` 播该项；`PlayAttackState` 记下 `ComboIndex = i`
5. 每一刀仍是新 `SkillInstance`，`ConfigId` 仍是那份普攻蓝图

`Kind=Skill`：始终 `Specs[0]`；`Specs.Count > 1` 时 `PushError`，仍只播 `[0]`。`ComboNextIndex` 越界：`PushError`，当 `0` 打。

**收招（该次 `PlayAttack` 的 Total 走尽）**

仅 Basic 收招后才可能开窗。记刚打完的下标 `i`：

- `i` 已是最后一项，或该段 `FollowUpWindow <= 0` → `ResetCombo()`
- 否则 `ComboNextIndex = i + 1`，`FollowUpRemaining = FollowUpWindow`

窗在 Combat 的 `PhysicsTick` 里扣时间（在 `TickPlayAttacks` 之后）。扣完 `ResetCombo()`。

招式在 Combat tick 里收束并开窗；本帧输入已经跑过，故**最早下一物理帧**才能续招。挥击中的按键仍被占用锁丢掉，不做预输入。

**`ResetCombo()`**

`ComboNextIndex = 0`，`FollowUpRemaining = 0`。不取消正在播的 `PlayAttack`（互斥下挥击中进不了跳 / 战技，本轮无需 `SuppressFollowUp`）。

普攻自己的 `TryActivate(Basic)` **不清连**。

---

## 动作互斥

互斥的是四类**占用**，同一时刻只能有一类：

1. 普通攻击（Basic `PlayAttack` 整段）
2. 跳跃和移动（二者不互斥；空中仍可走）
3. 战技（该次激活里的 Skill `PlayAttack`）
4. 大招（同上）

占用看的是**正在播的招式 / 是否离地**，不是 `SkillInstance` 是否还活着。因此「当帧戳一下再挂监听」的战技：戳击期间不能普攻；收招后监听还在，可以普攻。只有授予、没有 `PlayAttack` 的战技不当作占用。

**占用期间拒绝其它互斥动作（不排队、不预输入）：**

| 当前占用 | 普攻 | 跳 | 移动 | 战技 / 大招 |
|----------|------|----|------|-------------|
| 普攻挥击中 | 拒绝 | 拒绝 | 忽略输入 | 拒绝 |
| 战技 / 大招挥击中 | 拒绝 | 拒绝 | 忽略输入 | 拒绝（原 CD / Replace 仍有效） |
| 离地 | 拒绝 | 拒绝（已有落地判定） | 允许 | 拒绝 |
| 续招窗（已收招、在地） | 允许 | 允许 | 允许 | 允许 |

`CombatComponent`：任意实例存在进行中的 `PlayAttack` 时，`TryActivate` 一律拒绝（Basic 与 Skill 都是）。离地门禁由 `PlayerInputComponent` 查 `MovementComponent`（Combat 不依赖位移组件）。Enemy 无 Player 输入，本轮不套这套互斥。

同一物理帧多个互斥键只执行一个，优先级 **大招 > 战技 > 普攻 > 跳**。本帧若处理了大招 / 战技 / 普攻（无论成功失败），不再跳。位移：仅在存在进行中 `PlayAttack` 时清零；离地仍读方向。

`MovementComponent.Jump()` 返回 `bool`：落地起跳成功为 `true`。

---

## 清连

`BreakCombo()` = `ResetCombo()`。

| 来源 | 何时 |
|------|------|
| 跳跃成功 | 续招窗内落地起跳成功 → 清连并进入跳；此后离地，普攻 / 战技 / 大招被拒直到落地 |
| 战技 / 大招激活成功 | `Kind == Skill` 且 `TryActivate` 返回 true 时由 Combat 自己清连。校验 / CD / 占用失败不清 |
| 死亡 | 现有 `Died` 卸实例时一并清连 |

---

## 错误处理

| 情况 | 行为 |
|------|------|
| `Specs` 为空或全 null | `PushError`；该次 `PlayAttack` 跳过；Basic 激活失败并 `ResetCombo` |
| 本刀要播的 `Specs[i]` 为 null 或 `Hitboxes` 为空 | `PushError`；激活失败；`ResetCombo` |
| `ComboNextIndex` 越界 | `PushError`；当 `0` 打 |
| 非最后一段 `FollowUpWindow <= 0` | 合法，收招后收链 |
| Skill 的 `Specs.Count > 1` | `PushError`；仍播 `[0]` |
| 某段多只 Hitbox | 沿用技能规格：`PushError`，只用第一只 |
| `Cost != 0` 等 | 仍按技能规格 |

缺依赖时 `GD.PushError` 并跳过，不抛未处理异常。

---

## 文件清单（实现时）

### 修改

| 文件 | 改动 |
|------|------|
| `scripts/data/AttackSpec.cs` | 新增 `FollowUpWindow`，默认 `0.5` |
| `scripts/data/PlayAttackModule.cs` | `Spec` → `Specs` |
| `scripts/SkillInstance.cs` | `PlayAttackState` 增加 `ComboIndex` |
| `scripts/CombatComponent.cs` | 连段状态、占用锁、收招开窗、`BreakCombo`、Skill 成功清连、死亡清连 |
| `scripts/MovementComponent.cs` | `Jump()` 返回 `bool`；公开落地查询 |
| `scripts/PlayerInputComponent.cs` | 互斥优先级；占用时忽略移动；跳成功清连；离地拒普攻 / 战技 / 大招 |
| `data/actors/skills/player_default_attack.tres` | `Specs` 数组，至少 3 段 |
| `data/actors/skills/player_extra_blow.tres` | `Spec` 迁为一项的 `Specs` |
| `data/actors/attacks/player_melee_spec.tres` | 可依赖默认 `FollowUpWindow` |

### 新增

| 文件 | 职责 |
|------|------|
| `data/actors/attacks/player_melee_spec_2.tres` | 第 2 段，判定盒与第 1 段可区分 |
| `data/actors/attacks/player_melee_spec_3.tres` | 第 3 段，最后一段 |

### 不改

位移积分、属性公式、Health、Hurtbox 几何、效果寿命 / 监听扇出 / Replace、存档、Dodge 槽、`Privot` 拼写。

---

## 范围外

- `CancelOpenAt`、预输入、后摇取消进下一段
- 技能 / 大招自己的连段
- 受击硬直、霸体、被打清连（死亡除外）
- 普攻锁面向
- 把 `AnimationPlayer` 当玩法时钟
- Enemy 套用 Player 动作互斥
- 自动化测试 / CI
- 将 `Privot` 更正为 `Pivot`

---

## 完成标准

1. Player 普攻 `Specs` 至少 3 段，判定盒可区分；只按一下只出第 1 段，0.5s 后再按仍是第 1 段
2. 每段收招后再按：1→2→3，第 3 段后再按回到 1
3. 挥击中连按不变段；挥击中不能走、不能跳、不能 Z/V
4. 窗内跳：下次普攻是第 1 段；滞空不能普攻 / 战技 / 大招；落地走动与跳可以同时
5. 窗内 Z：清连；戳击占用期间不能普攻；收招后监听仍能吃之后每一段普攻的 `BasicAttack*`
6. `dotnet build` 成功

## 实现时建议加载的技能

- `godot-prompter:resource-pattern` — `AttackSpec` / `PlayAttackModule.Specs`
- `godot-prompter:ability-system` — 激活门禁与占用
- `godot-prompter:component-system` — Combat / Movement / Input 不互相钻兄弟节点名
- `godot-prompter:input-handling` — 物理帧轮询与互斥优先级
- `godot-prompter:player-controller` — 占用期间忽略位移输入
