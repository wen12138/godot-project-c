# 短招成功后的周期开盒

日期：2026-09-02  
项目：ProjectC（Godot 4.6 / C#）  
状态：已定稿

## 目标

让一份 `Kind=Skill` 的技能可以：**短 `PlayAttack` 打出起手伤害并占用出招；只有这次招式正常收招后，才在施放者身上按固定间隔开短命 HitBox。** 收招之后施放者可以移动、普攻、放别的技能；盒子跟着人走，每跳当场扫重叠。

覆盖的样例：前摇 0.3s、判定 0.1s、后摇 0.1s；成功收招后每 1.0s 开一次 0.1s 盒子。效果 `Duration=20.1`，保证最后一窗完整；UI 以后再显示成整 20s，本规格不做 UI。

## 约束

- 沿用 `2026-08-31-skill-definition-design.md`：技能是 Resource 蓝图；运行时不写回共享 `.tres`；判定仍是逻辑三轴 AABB；组件不自挂 `_PhysicsProcess` 做玩法
- 命中仍走 `HitboxComponent` / `HurtboxComponent`，伤害公式仍是攻击方 `GetAttackPower()`
- 不把 20s 级持续做成一次 `PlayAttack`（会锁死 `IsPlayOccupied`）
- 不新增 `SkillDefinition` / `AttackSpec` / `GameplayEffect` 的周期延迟字段（不要 `FirstTickDelay`）
- 不做打断状态机；只保证「短招没正常收招 → 周期效果不启动」，给以后的打断留钩子
- 完成标准：C# 编译通过 + 手动跑图；不做自动化测试 / CI
- 保持节点名拼写 `Privot`

## 对既有文档的修订

| 文档 | 旧约定 | 新约定 |
|------|--------|--------|
| `2026-08-31-skill-definition-design.md` 模块表 | `ApplyEffect` 激活成功当帧按 Targeting 施加 | 默认仍当帧施加。`ApplyEffectModule.WaitForPlayAttack==true` 时推迟到**本次** `PlayAttack` 正常结束 |
| 同上效果寿命 | 第一次 `OnTick` 在施加后经过 `Period` | 仍是。周期时钟从效果真正挂上算起，不是从技能激活算起 |
| 同上 `ExtraHitbox` | 仅监听 `*Started` 时开短命盒 | 保留。`OnTick` 若 `ExtraHitbox` 非空且效果挂在施放者自己身上，也走同一条短命盒路径 |

旧文档不逐行改写；推迟施加与 Tick 开盒以本文为准。多段 HitBox 播放、续招、Replace、监听扇出仍以既有规格为准。

---

## 架构

```text
激活成功
  PlayAttack 开始（0.5s 占用）
  WaitForPlayAttack 的 ApplyEffect 本帧跳过
  其它当帧模块照旧

PlayAttack 正常结束（elapsed >= Total，非 Cancel）
  → 按模块数组顺序施加 WaitForPlayAttack 的 ApplyEffect
  → 效果挂在 Targeting 选中的目标上
  → 周期从这一刻起算

PlayAttack 被取消（Replace / 死亡 / 以后的打断）
  → 不施加 WaitForPlayAttack 的效果
```

周期盒子开在**施放者**的 `HitboxComponent` 上（与现有 `GrantListener` 附加盒相同），每物理帧跟 `TransformComponent` 走，所以人跑到哪盒就在哪。`IsPlayOccupied` 只在短招期间为真。

```text
SkillDefinition (Kind=Skill)
  Modules
    PlayAttack     Specs[0]：Startup=0.3 Active=0.1 Recovery=0.1，一只盒
    ApplyEffect    WaitForPlayAttack=true
                     Targeting=Self
                     GameplayEffect
                       Duration=20.1
                       Period=1.0
                       ExtraHitboxDuration=0.1
                       ExtraHitbox（几何）
                       TickDamage=0
```

---

## 数据

### `ApplyEffectModule`

| 字段 | 类型 | 含义 |
|------|------|------|
| `Effect` | `GameplayEffect` | 已有 |
| `WaitForPlayAttack` | `bool` | 默认 `false`。`true`：激活当帧不施加；等本次 `PlayAttack` 正常结束再施加 |

不新增模块类型。`GrantListener` 本轮不增加此开关，仍当帧施加。

### `GameplayEffect`

不新增字段。例 2 用已有：

| 字段 | 样例 | 含义 |
|------|------|------|
| `Duration` | `20.1` | 相对**施加时刻**。策划把值填到盖住最后一窗（最后一跳时刻 + `ExtraHitboxDuration`）。本轮不做到期宽限，也不做「撑不满就跳过」 |
| `Period` | `1.0` | `<=0` 不 Tick。`>0` 时 `OnApply` 不打周期伤；第一次 `OnTick` 在施加后经过 `Period`，之后每 `Period` 一次 |
| `ExtraHitbox` | 一只 `HitboxEntry` | Tick 开盒的几何。`Offset`/`Size` 与起手盒可以相同；`Start`/`End` 忽略（寿命用 `ExtraHitboxDuration`） |
| `ExtraHitboxDuration` | `0.1` | 脉冲盒打开时长。实现上仍 `Max(0.01, 值)` |
| `TickDamage` | `0` | 例 2 为 0。`>0` 时仍对 `effect.Target` 直接扣血（快照 DoT），与开盒独立 |

### 起手表

`AttackSpec`：`Startup=0.3`，`Active=0.1`，`Recovery=0.1`，`FollowUpWindow=0`，一只 `HitboxEntry`（可用默认窗，不必填 `Start`/`End`）。

`SkillDefinition`：`Kind=Skill`，`Targeting=Self`，`Specs` 长度必须为 1（本轮样例；多段续招 + `WaitForPlayAttack` 的交互见范围外）。

---

## 运行时

### 激活

`TryActivate` 仍当帧跑全部 `OnActivate`。`ApplyEffectModule`：

- `WaitForPlayAttack==false`：现网，立即 `ApplyModuleEffect`
- `WaitForPlayAttack==true`：**无条件跳过本帧施加**（不要看 `instance.PlayAttack` 在不在——模块数组里若 ApplyEffect 写在 PlayAttack 前面，当时还没开招）

激活流程末尾：定义里存在 `WaitForPlayAttack==true` 的模块，但 `HasPlayAttack()==false`：`PushError`。`BeginPlayAttack` 失败因而从未挂上招式时，不会走到正常收招，效果自然不挂。

CD 仍在激活成功当帧写入，与短招是否收完无关。

### 正常收招

`TickPlayAttacks` 在 `elapsed >= Total`、关盒、处理续招窗之后、把 `PlayAttack` 置空之前或之后均可，但必须：

1. 确认这次结束不是 `CancelPlayAttack`
2. 按定义 `Modules` 数组顺序，对每个 `WaitForPlayAttack==true` 的 `ApplyEffectModule` 调用 `ApplyModuleEffect`
3. 再判断 `InstanceStillAlive`（刚挂上的效果必须能把实例留住）

同一实例只在**这一次** `PlayAttack` 正常结束时施加一次。不要在后续物理帧重复施加。

### 取消

`CancelPlayAttack`（Replace、死亡清实例、以及以后的打断）**不**跑 `WaitForPlayAttack` 施加。短招期间已经开出的起手盒按现网关盒；周期效果未挂上。

### `OnTick`

`HandleEffectTick`：

1. `TickDamage > 0` 且 `effect.Target.Health` 有效：对该目标 `TakeDamage`（现网）
2. `ExtraHitbox != null` 且 `effect.Target ==` 本 `CombatComponent` 的 Actor：调用现有 `OpenListenerHitbox`（新 `AttackId`，`FromListener=true`，不发 `SkillAttackStarted` / `SkillAttackHit`）
3. 否则有 `ExtraHitbox` 但目标不是自己：不开盒（避免范围施加出多份效果时每跳在施放者身上叠开多只盒）

盒的查询与关闭顺序不变：Actor 先 Hitbox 查询，再 Combat 扣时间。`TickListenerBoxes` 仍按 `Remaining` 关附加盒。

### 到期与 Replace

- `elapsed >= Duration`：现网 `OnExpire` → `OnRemove`。本轮**不**把卸效果与关脉冲盒绑在一起；脉冲盒继续按 `ExtraHitboxDuration` 自己倒数。策划用 `Duration=20.1` 盖住最后一窗。
- Replace / 死亡：现网 `CloseListenerBoxesForSource`，立刻关掉该来源还开着的附加盒。
- 脉冲期间再放同一 `ConfigId`：若 CD 已转好且当前无 `PlayAttack` 占用，走 Replace，卸旧效果；新周期仍要等新的短招成功收招。

### 占用

短招 0.5s 内 `IsPlayOccupied==true`（不能普攻 / 放技 / 跳 / 移动输入，与现网一致）。收招后占用结束；开着的脉冲盒不占用。

---

## 时间轴（样例，短招成功）

相对技能激活 t=0；效果在 t=0.5 挂上。

| 世界时间 | 事件 |
|----------|------|
| 0.00–0.30 | 前摇，占用 |
| 0.30–0.40 | 起手盒，占用 |
| 0.40–0.50 | 后摇，占用 |
| 0.50 | 短招结束，施加效果，占用解除 |
| 1.50–1.60 | 第 1 次脉冲盒 |
| 2.50–2.60 | 第 2 次 |
| … | 每 1.0s 一次 |
| 20.50–20.60 | 第 20 次脉冲盒（效果 elapsed=20.0 开盒） |
| 20.60 | 效果 elapsed=20.1，到期 |

短招在 0.50 之前被取消：无上表脉冲。

---

## 错误处理

| 情况 | 行为 |
|------|------|
| 定义含 `WaitForPlayAttack` 但没有 `PlayAttack` | 激活时 `PushError`；推迟模块不会被施加 |
| `WaitForPlayAttack` 且 `Effect` 为空 | 与现网空效果一样跳过 |
| `WaitForPlayAttack` 且 `Targeting != Self`，同时 `ExtraHitbox` 非空 | `PushError`；仍可施加（若 Targeting 合法），但 Tick 不开盒 |
| `Period > 0` 且 `ExtraHitbox` 空且 `TickDamage <= 0` | 允许（空 Tick，只耗 Duration） |
| `ExtraHitboxDuration <= 0` 但仍开盒 | 现网 `Max(0.01, 值)` |
| 范围 Targeting 且 `AreaRadius <= 0` | 现网 `PushError`，该次施加跳过 |

缺依赖时 `GD.PushError` 并跳过，不抛未处理异常。

---

## 样例资源

| 文件 | 职责 |
|------|------|
| `data/actors/attacks/player_skill_pulse_open_spec.tres` | 起手 0.3/0.1/0.1，一只盒 |
| `data/actors/effects/player_skill_pulse_effect.tres` | Duration=20.1，Period=1.0，ExtraHitboxDuration=0.1，TickDamage=0 |
| `data/actors/skills/player_skill_pulse.tres` | `skill.player_default.pulse`，PlayAttack + WaitForPlayAttack 的 ApplyEffect |

绑到 `Job.Ultimate`（V），战技槽仍留例 1 爆发，便于对照。`player_charge_burst.tres` 保留，不删。CD 由样例自定，须让手测能重复施放；不要求与 20.1 对齐。

---

## 范围外

- 打断、霸体、受击取消的完整状态机（只要求 Cancel 路径不施加推迟效果）
- `FirstTickDelay`、读条条、技能 UI / Duration 显示成 20s
- 钉在施放点的法术场、快照挂身 DoT 作为例 2 的主路径（`TickDamage` 快照仍保持可用）
- `GrantListener.WaitForPlayAttack`
- 续招 `Specs.Count > 1` 与 `WaitForPlayAttack` 的合成（每段收招都会施加，下一段 Replace 会卸掉；本轮样例只有 1 段）
- 脉冲盒发 `SkillAttack*`、给其它监听充能
- 自动化测试 / CI

---

## 文件清单（实现时）

| 文件 | 改动 |
|------|------|
| `scripts/data/ApplyEffectModule.cs` | 增加 `WaitForPlayAttack`；`OnActivate` 按开关决定立即施加或跳过 |
| `scripts/CombatComponent.cs` | 正常收招后施加推迟的 `ApplyEffect`；`HandleEffectTick` 在条件满足时 `OpenListenerHitbox` |
| `scripts/SkillInstance.cs` | 仅当收招施加需要额外标记时再加；优先用「扫描定义模块 + 本实例曾有过 PlayAttack」避免新字段 |
| 上文三份 `.tres` 与 `player_default_job.tres` | 样例与 Ultimate 槽 |

不改位移、属性、Hitbox 几何查询、多盒播放、续招表。

---

## 完成标准

1. 短招成功：起手打一下，0.5s 后可移动/普攻；之后每 1.0s 开 0.1s 盒，跟着施放者，进盒才受伤
2. 短招未收完就被 Replace 或死亡：没有后续脉冲
3. `Duration=20.1` 时第 20 窗能打满 0.1s；不另写宽限代码
4. 例 1 爆发、普攻连、战技续招资源行为不变（战技槽仍是爆发）
5. `dotnet build` 成功；不要求自动化测试

## 实现时建议加载的技能

- `godot-prompter:resource-pattern`
- `godot-prompter:ability-system`
- `godot-prompter:component-system`
- `godot-prompter:csharp-godot`
