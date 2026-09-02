# 短招成功后的周期开盒

日期：2026-09-02  
项目：ProjectC（Godot 4.6 / C#）  
状态：已定稿

## 目标

让一份 `Kind=Skill` 的技能可以：**短 `PlayAttack` 打出起手伤害并占用出招；`ApplyEffect` 可在招式时钟的指定时刻才施加。** 典型用法是进入 Active 才挂持续效果。效果挂上之后施放者在短招收完即可移动、普攻、放别的技能；盒子跟着人走，每跳当场扫重叠。

覆盖的样例：前摇 0.3s、判定 0.1s、后摇 0.1s；**进入 Active（t=0.3）时**挂效果，之后每 1.0s 开一次 0.1s 盒子。效果 `Duration=20.1`，保证最后一窗完整；UI 以后再显示成整 20s，本规格不做 UI。

## 约束

- 沿用 `2026-08-31-skill-definition-design.md`：技能是 Resource 蓝图；运行时不写回共享 `.tres`；判定仍是逻辑三轴 AABB；组件不自挂 `_PhysicsProcess` 做玩法
- 命中仍走 `HitboxComponent` / `HurtboxComponent`，伤害公式仍是攻击方 `GetAttackPower()`
- 不把 20s 级持续做成一次 `PlayAttack`（会锁死 `IsPlayOccupied`）
- 不新增 `GameplayEffect` 的周期延迟字段（不要 `FirstTickDelay`）；推迟时刻只放在 `ApplyEffectModule` 上
- 不做打断状态机；只保证「招式时钟还没跨过该模块的施加时刻就被取消 → 周期效果不启动」
- 完成标准：C# 编译通过 + 手动跑图；不做自动化测试 / CI
- 保持节点名拼写 `Privot`

## 对既有文档的修订

| 文档 | 旧约定 | 新约定 |
|------|--------|--------|
| `2026-08-31-skill-definition-design.md` 模块表 | `ApplyEffect` 激活成功当帧按 Targeting 施加 | 默认 `Cue=PlayStartupStart`：有 `PlayAttack` 时等招式时钟跨过 0（前摇开始）；无 `PlayAttack` 时退化为激活当帧。其它 Cue 等到 `elapsed` 跨过解析时刻 |
| 同上效果寿命 | 第一次 `OnTick` 在施加后经过 `Period` | 仍是。周期时钟从效果真正挂上算起，不是从技能激活算起 |
| 同上 `ExtraHitbox` | 仅监听 `*Started` 时开短命盒 | 保留。`OnTick` 若 `ExtraHitbox` 非空且效果挂在施放者自己身上，也走同一条短命盒路径 |

旧文档不逐行改写；推迟施加与 Tick 开盒以本文为准。多段 HitBox 播放、续招、Replace、监听扇出仍以既有规格为准。

---

## 架构

```text
激活成功
  PlayAttack 开始（0.5s 占用）
  所有 Cue 都不在模块 OnActivate 里直接施加（有招式时钟的技能）
  BeginPlayAttack 后 previous=-1, elapsed=0 扫一次
    → PlayStartupStart / ApplyAt==0 当帧挂上（前摇开始）

PlayAttack 推进 elapsed
  previous < 解析时刻 <= elapsed  → 按模块顺序施加一次
  取消（Replace / 死亡 / 以后的打断）发生在跨过该时刻之前
  → 该模块不施加
```

「进入 Active 才挂」= `Cue=PlayActiveStart`，解析时刻 = 本段 `AttackSpec.Startup`。前摇被打断则没有持续伤害；**已经跨过 Active 起点之后**再取消短招，效果已经挂上（Replace 仍会卸掉）。

周期盒子开在**施放者**的 `HitboxComponent` 上（与现有 `GrantListener` 附加盒相同），每物理帧跟 `TransformComponent` 走。`IsPlayOccupied` 只在短招期间为真。

```text
SkillDefinition (Kind=Skill)
  Modules
    PlayAttack     Specs[0]：Startup=0.3 Active=0.1 Recovery=0.1，一只盒
    ApplyEffect    Cue=PlayActiveStart
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

### `ApplyEffectCue`

与 `ApplyEffectModule` 放在一起即可，不必新 Resource 类型。

| 值 | 解析时刻（相对本次 PlayAttack elapsed=0） |
|----|------------------------------------------|
| `PlayStartupStart` | `0`（前摇开始）。默认。无 `PlayAttack` 时退化为激活当帧（现网光环） |
| `PlayActiveStart` | 本段 `AttackSpec.Startup`（Active 开始） |
| `PlayRecoveryStart` | `Startup + Active`（后摇开始） |
| `PlayComplete` | `TotalDuration`（短招正常结束） |
| `PlayElapsed` | 使用模块上的 `ApplyAt`（相对招式起点的秒） |

相位四项跟着 `AttackSpec` 走：`PlayStartupStart` / `PlayActiveStart` / `PlayRecoveryStart` / `PlayComplete` 对应 Startup → Active → Recovery → 结束。任意提前量用 `PlayElapsed`：例如结束前 0.05s → `ApplyAt = TotalDuration - 0.05`。

不要再使用 `OnActivate` 这个名字，避免和 `AttackSpec.Active` 混淆。`PlayStartupStart` 也不是「技能键按下的抽象激活」，而是招式时钟上前摇的起点。

### `ApplyEffectModule`

| 字段 | 类型 | 含义 |
|------|------|------|
| `Effect` | `GameplayEffect` | 已有 |
| `Cue` | `ApplyEffectCue` | 默认 `PlayStartupStart` |
| `ApplyAt` | `float` | 仅 `Cue=PlayElapsed` 时有效。默认 `-1` |

不新增模块类型。`GrantListener` 本轮不增加 Cue，仍当帧施加。

### `GameplayEffect`

不新增字段。例 2 用已有：

| 字段 | 样例 | 含义 |
|------|------|------|
| `Duration` | `20.1` | 相对**施加时刻**。策划把值填到盖住最后一窗（最后一跳时刻 + `ExtraHitboxDuration`）。本轮不做到期宽限 |
| `Period` | `1.0` | `<=0` 不 Tick。`>0` 时 `OnApply` 不打周期伤；第一次 `OnTick` 在施加后经过 `Period`，之后每 `Period` 一次 |
| `ExtraHitbox` | 一只 `HitboxEntry` | Tick 开盒的几何。`Offset`/`Size` 与起手盒可以相同；`Start`/`End` 忽略（寿命用 `ExtraHitboxDuration`） |
| `ExtraHitboxDuration` | `0.1` | 脉冲盒打开时长。实现上仍 `Max(0.01, 值)` |
| `TickDamage` | `0` | 例 2 为 0。`>0` 时仍对 `effect.Target` 直接扣血，与开盒独立 |

### 起手表

`AttackSpec`：`Startup=0.3`，`Active=0.1`，`Recovery=0.1`，`FollowUpWindow=0`，一只 `HitboxEntry`（可用默认窗，不必填 `Start`/`End`）。

`SkillDefinition`：`Kind=Skill`，`Targeting=Self`，`Specs` 长度必须为 1（本轮样例）。

---

## 运行时

### 解析时刻

用**正在播的那一段** `AttackSpec`：

```text
PlayStartupStart   → 0
PlayActiveStart    → Startup
PlayRecoveryStart  → Startup + Active
PlayComplete       → TotalDuration
PlayElapsed        → ApplyAt
                     ApplyAt < 0 → PushError，该模块跳过
                     ApplyAt > TotalDuration → 钳到 TotalDuration（等价 PlayComplete）
```

### 激活

`TryActivate` 仍当帧跑全部模块 `OnActivate`。`ApplyEffectModule`：

- 定义**没有** `PlayAttack`：仅 `PlayStartupStart` 立即 `ApplyModuleEffect`（无前摇可等）；其它 Cue `PushError` 并跳过
- 定义**有** `PlayAttack`：本帧一律不施加，等招式时钟

`BeginPlayAttack` 失败因而从未挂上招式时，elapsed 不会跨过任何时刻，效果自然不挂。

CD 仍在激活成功当帧写入。

`BeginPlayAttackFromSpec` 结束时立刻用 `previous=-1, elapsed=0` 扫一次（`PlayStartupStart` 与 `ApplyAt==0` 当帧挂上，即前摇开始）。

### 招式推进

`TickPlayAttacks` 在 `elapsed += dt`、开/关盒之后：

1. 若这次不是 `CancelPlayAttack`：按定义 `Modules` 数组顺序，对尚未施加的 `ApplyEffectModule`，若 `previous < 解析时刻 && elapsed >= 解析时刻`，则 `ApplyModuleEffect`，并记下该模块下标以免重复
2. `elapsed >= Total` 时：先做第 1 步（`PlayComplete` / 钳到 Total 的 `PlayElapsed` 要在置空前跨过），再关盒、续招窗、`PlayAttack = null`
3. 再判断 `InstanceStillAlive`

`PlayAttackState` 增加 `HashSet<int>`（或等价）记录已施加的模块下标。不要依赖「是否收招」单一布尔，同一招可以有多个不同 Cue 的 `ApplyEffect`。

### 取消

`CancelPlayAttack` **不再扫**未跨过的推迟模块。已经跨过时刻挂上的效果按现网留着，直到 Replace / 到期 / 死亡。短招期间已经开出的起手盒按现网关盒。

### `OnTick`

`HandleEffectTick`：

1. `TickDamage > 0` 且 `effect.Target.Health` 有效：对该目标 `TakeDamage`（现网）
2. `ExtraHitbox != null` 且 `effect.Target ==` 本 `CombatComponent` 的 Actor：调用现有 `OpenListenerHitbox`（新 `AttackId`，`FromListener=true`，不发 `SkillAttackStarted` / `SkillAttackHit`）
3. 否则有 `ExtraHitbox` 但目标不是自己：不开盒

盒的查询与关闭顺序不变：Actor 先 Hitbox 查询，再 Combat 扣时间。`TickListenerBoxes` 仍按 `Remaining` 关附加盒。

### 到期与 Replace

- `elapsed >= Duration`：现网 `OnExpire` → `OnRemove`。脉冲盒按 `ExtraHitboxDuration` 自己倒数。策划用 `Duration=20.1` 盖住最后一窗。
- Replace / 死亡：现网 `CloseListenerBoxesForSource`，立刻关掉该来源还开着的附加盒。
- 脉冲期间再放同一 `ConfigId`：若 CD 已转好且当前无 `PlayAttack` 占用，走 Replace，卸旧效果；新周期仍要等新的短招跨过新的 Cue 时刻。

### 占用

短招 0.5s 内 `IsPlayOccupied==true`。收招后占用结束；开着的脉冲盒不占用。样例在 t=0.3 已挂效果，t=0.3–0.5 仍占用，但周期时钟已经在走。

---

## 时间轴（样例，`Cue=PlayActiveStart`，短招跨过 t=0.3）

相对技能激活 t=0；效果在 t=0.3 挂上。

| 世界时间 | 事件 |
|----------|------|
| 0.00–0.30 | 前摇，占用；效果未挂 |
| 0.30 | 进入 Active，施加效果 |
| 0.30–0.40 | 起手盒，占用 |
| 0.40–0.50 | 后摇，占用 |
| 0.50 | 短招结束，占用解除 |
| 1.30–1.40 | 第 1 次脉冲盒 |
| 2.30–2.40 | 第 2 次 |
| … | 每 1.0s 一次 |
| 20.30–20.40 | 第 20 次脉冲盒（效果 elapsed=20.0 开盒） |
| 20.40 | 效果 elapsed=20.1，到期 |

短招在 **0.30 之前**被取消：无脉冲。0.30 之后取消短招：周期已经在跑（除非这次取消走 Replace 卸效果）。

---

## 错误处理

| 情况 | 行为 |
|------|------|
| 定义没有 `PlayAttack` 且 `Cue` 不是 `PlayStartupStart` | 激活时 `PushError`；该模块跳过 |
| `Cue=PlayElapsed` 且 `ApplyAt < 0` | `PushError`；该模块跳过 |
| `Cue` 推迟施加且 `Effect` 为空 | 与现网空效果一样跳过 |
| 推迟施加且 `Targeting != Self`，同时 `ExtraHitbox` 非空 | `PushError`；仍可施加（若 Targeting 合法），但 Tick 不开盒 |
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
| `data/actors/skills/player_skill_pulse.tres` | `skill.player_default.pulse`，PlayAttack + `Cue=PlayActiveStart` 的 ApplyEffect |

绑到 `Job.Ultimate`（V），战技槽仍留例 1 爆发。`player_charge_burst.tres` 保留，不删。CD 由样例自定，须让手测能重复施放；不要求与 20.1 对齐。

---

## 范围外

- 打断、霸体、受击取消的完整状态机（只要求 Cancel 路径不施加尚未跨过的推迟效果）
- `FirstTickDelay`、读条条、技能 UI / Duration 显示成 20s
- 钉在施放点的法术场、快照挂身 DoT 作为例 2 的主路径（`TickDamage` 快照仍保持可用）
- `GrantListener` 的 Cue
- 续招 `Specs.Count > 1` 与推迟 `ApplyEffect` 的合成（每段用该段 Spec 解析相位；下一段 Replace 会卸掉上一段效果；本轮样例只有 1 段）
- 脉冲盒发 `SkillAttack*`、给其它监听充能
- 自动化测试 / CI

---

## 文件清单（实现时）

| 文件 | 改动 |
|------|------|
| `scripts/data/ApplyEffectModule.cs` | `ApplyEffectCue`、`Cue`、`ApplyAt`；有 `PlayAttack` 时 `OnActivate` 不直接施加 |
| `scripts/CombatComponent.cs` | 解析时刻、跨过则施加；`HandleEffectTick` 在条件满足时 `OpenListenerHitbox` |
| `scripts/SkillInstance.cs` | `PlayAttackState` 记录已施加的模块下标 |
| 上文三份 `.tres` 与 `player_default_job.tres` | 样例与 Ultimate 槽 |

不改位移、属性、Hitbox 几何查询、多盒播放、续招表。

---

## 完成标准

1. `Cue=PlayActiveStart`：前摇结束才挂效果；之后每 1.0s 开 0.1s 盒，跟着施放者，进盒才受伤；0.5s 后可移动/普攻
2. 短招在 Active 起点之前被 Replace 或死亡：没有后续脉冲
3. `Duration=20.1` 时第 20 窗能打满 0.1s；不另写宽限代码
4. 例 1 爆发、普攻连、战技续招资源行为不变（战技槽仍是爆发）
5. `dotnet build` 成功；不要求自动化测试

## 实现时建议加载的技能

- `godot-prompter:resource-pattern`
- `godot-prompter:ability-system`
- `godot-prompter:component-system`
- `godot-prompter:csharp-godot`
