# 技能定义：统一招式与效果

日期：2026-08-31  
项目：ProjectC（Godot 4.6 / C#）  
状态：已定稿

## 目标

把职业槽上的**普攻、战技、大招**收成同一套数据与同一套激活逻辑，覆盖：

- 与现有近战挥击同类的一段或多段伤害
- 范围 Buff、按固定间隔 Tick 的持续伤害
- 施放时先打一小段技能伤害，当帧给自己挂持续效果
- 持续期间监听普攻：额外判定 / 额外投射物、普攻充能、满能或到期爆发

判定真相仍是逻辑三轴 AABB。不引入通用多轨道时间轴；时机用招式阶段字段，持续用效果自己的寿命，协同用事件扇出。

## 约束

- 沿用 `2026-08-29-core-combat-job-design.md`：组件不自挂 `_PhysicsProcess` 做玩法；Player 先输入，Actor 再位移 → 判定 → 战斗
- 命中仍走 `HitboxComponent` / `HurtboxComponent` / `HurtboxRegistry`，不把 Godot 2D 物理当命中依据
- 不把 `AnimationPlayer` 当玩法时钟或判定窗真相
- 技能是 Resource 蓝图；运行时状态在实例与效果容器上，禁止改共享 `.tres`
- `ActorDefinition` 仍是 Actor 上唯一 Export 的模板；技能只从 `Definition.Job` 读取
- 保持节点名拼写 `Privot`
- 完成标准以「数据与运行时规则可实现、现有普攻可无损迁移」为准；图形化时间轴编辑器不做

## 对既有文档的修订

| 文档 | 旧约定 | 新约定 |
|------|--------|--------|
| `2026-08-29-core-combat-job-design.md` | `Job.Attack` 为 `AttackData`；`Skill` / `Ultimate` 为 `PackedScene`，本轮忽略 | `Attack` / `Skill` / `Ultimate` 均为 `SkillDefinition`；`Dodge` 仍为 `PackedScene`，本规格不实现 |
| `2026-08-24-attack-hit-detection-design.md` | 一份近战：`ActiveDuration` 从 0 开盒到结束；无前摇/后摇/多段盒 | 普攻是 `Kind = Basic` 的 `SkillDefinition`，招式窗口在 `AttackSpec`；现有 0.2s 开盒是 `Startup=0, Active=0.2, Recovery=0`、一只盒子的退化 |
| 职业规格「伤害不进 AttackData」 | 命中扣 `GetAttackPower()`，写在 `CombatComponent` | 默认伤害源仍是攻击方 `GetAttackPower()`，配置挂在 `AttackSpec.OnHit` 的默认条目上，公式本规格仍恒等 |

旧文档不逐行改写；技能与出招以本文为准。位移、属性、生命结算仍以 `2026-08-29` 为准。

---

## 架构

不采用「一条技能一条多轨道时间轴」。运行时只有三种东西：

1. **招式播放** — 推进 `elapsed`，按 `AttackSpec` 开/关盒
2. **效果容器** — 身上挂着的 Buff / DoT / 监听，自己扣寿命、到点 Tick
3. **事件扇出** — 普攻与技能伤害发不同事件；监听效果各自反应

一份 `SkillDefinition` 用**模块数组**组合上述能力。代码上普攻和战技可以同属 Ability 播放路径；逻辑上必须用 `AttackKind` 区分，禁止用「是不是某个 C# 类型」分支。

```text
ActorDefinition
  Job ──► JobDefinition
            Attack    SkillDefinition   Kind 必须 Basic（Player 必填；Enemy 允许 null）
            Skill     SkillDefinition   Kind 必须 Skill；允许 null
            Ultimate  SkillDefinition   Kind 必须 Skill；允许 null
            Dodge     PackedScene       本规格不实现
            Locomotion / Movement       不变
```

```text
激活成功
  SkillInstance（ConfigId + RuntimeId + Kind）挂在施放者上
    ├─ 当帧：授予模块 → 效果实例挂到各目标的效果表，并记 SourceRuntimeId
    └─ 若有 PlayAttack → 同一播放器开招（盒子另发 AttackId）
```

---

## 数据类型

### `AttackKind`

| 值 | 含义 | 发出的事件 |
|----|------|------------|
| `Basic` | 职业普攻槽打出来的 | `BasicAttackStarted` / `BasicAttackHit` |
| `Skill` | 战技 / 大招的 `PlayAttack`（含激活当帧那一下技能伤害） | `SkillAttackStarted` / `SkillAttackHit` |

事件载荷至少包含：`SkillInstance`（或 `RuntimeId` + `ConfigId` + `Kind`）、`AttackId`；`Hit` 另带 `HurtboxComponent`。

监听默认只订阅 `Basic*`。要吃技能伤害必须在该效果上显式勾选订阅 `Skill*`。

### `SkillDefinition`（`[GlobalClass] Resource`）

| 字段 | 类型 | 含义 |
|------|------|------|
| `ConfigId` | `string` | 蓝图身份。Replace 的唯一查找键。稳定字符串，不用 `.tres` 路径 |
| `Kind` | `AttackKind` | 本定义打出的招式事件通道；授予类模块不改 Kind |
| `Cost` | `int` | 激活消耗；本规格无资源池实现时，`Cost != 0` 则 `PushError` 且拒绝激活（普攻填 0） |
| `Cooldown` | `float` | 激活成功当帧起算的 CD（秒）。`0` 表示无此 CD |
| `Stacking` | 枚举 | 同 `ConfigId` 再施加：`Replace`（默认）/ `Independent` / `Reject` |
| `Targeting` | 见下 | 授予类 `ApplyEffect` 的选目标；`PlayAttack` 不走 Targeting，打的是 Hitbox 重叠 |
| `AreaRadius` | `float` | 仅 `Targeting` 为范围时使用；逻辑空间水平半径（`LogicX` + `LogicDepth`），高度用与近战相同的 `VirtualZ` 重叠规则 |
| `Modules` | 数组 | 模块列表，见下 |

`ConfigId` 约定：`skill.<job_key>.<slot_or_name>`，例如 `skill.player_default.attack`。缺省、空字符串、或项目内重复：加载/进场时 `PushError`。

`Job.Attack` 的 `Kind` 必须为 `Basic`。`Job.Skill` / `Ultimate` 非空时 `Kind` 必须为 `Skill`。不符则 `PushError`，该槽视为无效。

`Kind == Basic` **禁止**带「激活当帧给自己挂持续效果」的授予模块（`ApplyEffect` 以自身为施加对象且 `Duration > 0`，或任何 `GrantListener`）。违规则 `PushError`，该普攻定义视为无效。普攻打中目标的附加效果走 `AttackSpec.OnHit`。

`Stacking` 默认 `Replace`。`Independent` / `Reject` 字段先留在蓝图上，本规格实现只做 `Replace`；读到另外两种时 `PushError` 并按 `Replace` 处理，避免静默分叉。

### `Targeting`

| 值 | `ApplyEffect` 施加对象 |
|----|------------------------|
| `Self` | 仅施放者 |
| `EnemiesInRadius` | 范围内敌对 Actor（阵营与现有 Player vs Enemy 一致） |
| `AlliesInRadius` | 范围内同阵营（含自己） |
| `EveryoneInRadius` | 范围内所有 Actor（含自己） |

范围内：相对施放者逻辑坐标，水平距离 `<= AreaRadius`，且 Hurtbox 在 `VirtualZ` 上与施放者 Hurtbox 重叠（与近战同一套高度门）。`AreaRadius <= 0` 的范围 Targeting 视为无效，`PushError`，该次 `ApplyEffect` 跳过。

`PlayAttack` 忽略 `Targeting`。

### 模块

模块是可导出的 Resource 子类，数组顺序只决定**同一帧内的稳定次序**（先声明的先执行），默认**互不等待**。

| 模块 | 激活成功当帧 | 之后 |
|------|----------------|------|
| `PlayAttack` | 开始播这份 `AttackSpec` | 按招式时钟开/关盒，直到 Startup+Active+Recovery 结束 |
| `ApplyEffect` | 按 `Targeting` 对每个目标施加一份 `GameplayEffect` 运行时实例，父技能为当前 `SkillInstance` | 效果按自己的 `Duration` / `Period` 活 |
| `GrantListener` | 对**施放者自己**施加一份带普攻订阅的 `GameplayEffect` | 同上；默认只听 `Basic*` |

同一份定义可以同时有 `PlayAttack` 和授予模块。常见「先砸一下再挂协同」= 两者都有，授予当帧生效，不必等 `PlayAttack` 收招。

前摇被打断：授予已经挂上，只取消本实例尚未结束的 `PlayAttack`（关还开着的盒）。不退消耗、不自动卸授予（除非随后走 Replace 或效果自己到期）。

无 `PlayAttack`、只有授予：激活成功即只挂效果，没有技能伤害盒。

### `AttackSpec`（`[GlobalClass] Resource`）

由 `PlayAttack` 引用。现 `AttackData` 升格至此后删除 `AttackData` 类型。

| 字段 | 类型 | 含义 |
|------|------|------|
| `Startup` | `float` | 前摇（秒），此段不开盒。默认 `0` |
| `Active` | `float` | 默认判定段长度。默认 `0.2`（与现普攻一致） |
| `Recovery` | `float` | 后摇。默认 `0` |
| `CancelOpenAt` | `float` | 相对本次招式起点；`< 0` 表示本规格不开放取消。默认 `-1` |
| `Hitboxes` | 数组 | 至少一只。见下 |
| `OnHit` | 数组 | 该次 `PlayAttack` 的盒子命中时对**目标**施加；默认一条「伤害 = 攻击方 `GetAttackPower()`」 |

招式总长 = `Startup + Active + Recovery`。`CancelOpenAt >= 0` 时允许与 Active 重叠；本规格输入预缓冲可以读这个字段，但**不实现续招状态机**（字段先落地，消费方后接）。

**`HitboxEntry`**

| 字段 | 含义 |
|------|------|
| `Start` / `End` | 相对本次招式起点（秒）。`End > Start`。缺省时 `Start = Startup`、`End = Startup + Active`（整段默认判定窗） |
| `Offset` / `Size` | 与现 `AttackData` 相同，逻辑空间 |

多段伤害 = 多只 `HitboxEntry`，时间可重叠。每一只盒子激活时分配**新的** `AttackId`（与主刀、与其它段、与监听附加盒都隔离）。同一 `AttackId` 对同一 Hurtbox 只中一次，规则不变。

现有 `player_melee_default.tres`：`Startup=0, Active=0.2, Recovery=0`，一只盒，Offset/Size 与现在相同。

### `GameplayEffect`（`[GlobalClass] Resource`）

蓝图；运行时 Duplicate 成实例，挂在 `SkillInstance` 下。

| 字段 | 含义 |
|------|------|
| `Duration` | 秒；`<= 0` 表示瞬时：只跑 `OnApply` 然后立刻 `OnRemove`（不跑 `OnExpire`） |
| `Period` | 秒；`<= 0` 不 Tick。`> 0` 时 `OnApply` **不**造成周期伤害；第一次 `OnTick` 在施加后经过 `Period` |
| `Modifiers` | 本规格只要求结构能挂；数值结算仍走 Actor 上 `GetMaxHealth` / `GetAttackPower` 恒等，Modifier 暂不改公式 |
| `GrantedTags` | `StringName` 列表，施加时加上、卸掉时撤掉。用于可选互斥与门禁，不是 Replace 键 |
| `RemoveTags` | 施加时从目标上移除的标签（可选互斥） |
| `SubscribeBasic` | 默认 `true`：听普攻事件 |
| `SubscribeSkill` | 默认 `false` |
| `OnAttackStartedPayload` | 可空。收到已订阅的 `*Started` 时：开一只短命附加盒（引用一份 `AttackSpec` 或单只 `HitboxEntry`）和/或生成投射物场景 |
| `ChargeMax` | `<= 0` 表示不充能。`> 0` 时每次已订阅的 `*Hit` 使本实例 `Charge += 1` |
| `Burst` | 可空。`OnChargeFull` 或 `OnExpire` 时执行的一次爆发（范围伤害或一份 `PlayAttack`/`AttackSpec`） |

充能满：跑 `Burst`，然后 `Remove` 本效果（及若策略写「效果结束则结束父技能」则卸 `SkillInstance`）。**不**再走 `OnExpire`。

到期：跑 `OnExpire`（含 `Burst`），再 `OnRemove`。

投射物：载荷可引用 `PackedScene`；弹体如何位移、是否逻辑 AABB，**本规格不设计**，实现分期里附加判定优先做「在施放者身上开短命附加盒」。弹体场景字段允许先空。

### `SkillInstance`（运行时，非 Resource 资产）

| 字段 | 含义 |
|------|------|
| `ConfigId` | 拷自蓝图，Replace 键 |
| `RuntimeId` | 本次实例身份。由战斗侧分配，Actor 范围内 `uint` 从 1 递增，不复用到该 Actor 离开场景 |
| `Kind` | 拷自蓝图 |
| `SourceDefinition` | 蓝图引用（只读） |
| 孩子效果列表 | 本实例 `ApplyEffect` / `GrantListener` 产生的运行时效果 |
| 进行中的 `PlayAttack` | 可空 |

`RuntimeId` 禁止当叠加入口。日志、附加盒归属、驱散「这一份」用 `RuntimeId`。

`AttackId` 是第三套 Id：每一次开盒（主招每一段、监听附加盒）单独分配，全局或 Actor 范围递增均可，只要一次挥击内不重复。附加盒必须记录 `SourceRuntimeId`。

效果运行时实例挂在**目标 Actor** 的效果表上（给自己的协同挂在施放者；范围 DoT / Buff 挂在被选中的目标上），并记录 `SourceRuntimeId` / `SourceConfigId`。子效果**不用**自己的配置 Id 做 Replace。卸一份 `SkillInstance` = 卸掉场景内所有 `SourceRuntimeId` 等于该实例的效果（含打到别人身上的）。显示名仅供调试。

两份不同 `ConfigId` 的技能即使引用同一份 `GameplayEffect` 蓝图，也是两个 `SkillInstance`，监听叠加，互不 Replace。数值 Modifier 以后如何合并是结算层的事，本规格不改公式。

---

## 运行时

### 组件职责

| 对象 | 职责 |
|------|------|
| `CombatComponent`（可改名，但本规格仍由它调度出招） | `TryActivate(SkillDefinition)`；持有进行中的 Basic 招式锁与 Skill 实例表；物理帧推进 `PlayAttack` 与效果寿命 |
| 效果表 | 挂在 Combat 或独立子节点 `EffectHolder`；按实例归属，不自挂 `_PhysicsProcess` |
| `HitboxComponent` | 仍按 `AttackId` 开/关盒、查询、去重 |
| `PlayerInputComponent` | 普攻键 → `TryActivate(Job.Attack)`；战技/大招键后接，本规格不强制键位 |

调度顺序不变：输入 → 位移 → Hitbox 查询 → Combat 扣招式时间与效果时间。效果 Tick 与关盒在同一 `PhysicsTick` 内，**先查询命中，再扣时间/关盒**（延续命中规格：避免时长短于一帧时零命中）。

### `TryActivate`

1. `def` 空或校验失败：return  
2. `Kind == Basic` 且该 Actor 已有进行中的 Basic `PlayAttack`：return（招式锁，不是 `Cooldown` 字段）  
3. `Cooldown` 未转好：return  
4. `Cost != 0` 且无资源池：`PushError`，return  
5. `Stacking == Replace` 且已有同 `ConfigId` 的 `SkillInstance`：走 Replace（见下）  
6. `new SkillInstance`（新 `RuntimeId`）  
7. **当帧**按模块数组顺序：授予模块施加；`PlayAttack` 开始播  
8. 记录该 `ConfigId` 的 CD 起点为当前时间  

普攻每一刀都是新的短寿命 `SkillInstance`，寿命 = 本次 `PlayAttack` 总长；打完丢弃。不按 `ConfigId` 对「上一刀」做 Replace。

`Kind == Skill` 的实例寿命 = `max(本次 PlayAttack 剩余, 孩子效果剩余)`；没有授予且没有 PlayAttack 的定义视为无效（`PushError`，不激活）。

### 招式播放（Basic 与 Skill 共用）

`elapsed` 从 0 加物理 delta。每只 `HitboxEntry`：跨过 `Start` 则 `Activate(新AttackId, Offset, Size)`，跨过 `End` 则关该 `AttackId` 对应盒。实现上若 Hitbox 组件同时只支持一只盒，则多段必须能排队或扩展为可多盒；**本规格要求多段在数据上合法**，第一期实现若仍单盒，只允许 `Hitboxes.Count == 1`，多于一只 `PushError` 并只播第一只。

`Kind` 决定发出 `Basic*` 还是 `Skill*`。技能的 `PlayAttack` **不得**发 `Basic*`，否则开头小伤害会误触发协同与充能。

### 效果寿命

每物理帧：`elapsed += delta`。`Period > 0` 且距上次 Tick（或施加时刻）`>= Period` 则 `OnTick`。`elapsed >= Duration` 则 `OnExpire` → `OnRemove` → 若该实例已无孩子且无进行中 PlayAttack，销毁 `SkillInstance`。

### 事件扇出

`BasicAttackStarted` / `Hit`（及显式订阅了的 `Skill*`）发生时，遍历**所有**活着的、已订阅的效果实例，按「施加时间升序，同一帧再按父 `RuntimeId` 升序」执行。跨 `ConfigId` 默认叠加：额外弹 + 充能 + 附加盒可以同时存在。

每个监听者开出的附加盒使用新 `AttackId`，不得与主刀或其它监听者共用，否则会被「同一刀同一 Hurtbox 只中一次」吃掉。

### Replace

同 `ConfigId` 再激活（仅 `Kind == Skill` 的长寿命实例；普攻不走此路径）：

```text
找到旧 SkillInstance
  → 取消旧实例未结束的 PlayAttack
  → 场景内所有 SourceRuntimeId == 旧实例 的效果 OnRemove
    （施放者身上的监听，以及已打到别人身上的 Buff/DoT；退订阅、撤标签、关该来源仍开着的附加盒）
  → 实例 OnRemove
  → 不触发 OnExpire（重放不会因此爆发）
new SkillInstance，新 RuntimeId，同一 ConfigId
  → 按当前蓝图当帧再跑模块
```

「刷新」就是卸旧上新：时长、快照、Charge、附加盒规格全部来自**这一次**蓝图。需要保留 Charge 的效果可覆写 `OnReapply`，那是例外，不是默认。

已飞出的投射物不因 Replace 强制回收（投射物本规格第一期不做）。

### 生命周期回调（效果实例）

| 回调 | 何时 | 典型用途 |
|------|------|----------|
| `OnApply` | 新实例挂上 | 加标签、订阅、快照 |
| `OnTick` | 每 `Period` | DoT |
| `OnRemove` | 任何卸掉：到期、Replace、驱散、持有者死亡 | 退订阅、撤标签、关附加盒 |
| `OnExpire` | 仅 `Duration` 走完 | 到期 `Burst` |
| `OnChargeFull` | `Charge >= ChargeMax` | 满能 `Burst`，再 `Remove`，不走 `OnExpire` |

死亡：Actor `Died` 时卸掉该 Actor 上全部 `SkillInstance`（走 `OnRemove`，不走 `OnExpire`）。Enemy `QueueFree` 已有；Player 死亡后不再激活。

---

## 配置样例（语义，非最终 `.tres` 文本）

**普攻（迁移后）**

```text
ConfigId = skill.player_default.attack
Kind = Basic
Cost = 0, Cooldown = 0, Targeting = Self
Modules = [ PlayAttack(player_melee AttackSpec) ]
```

**放一下伤害 + 普攻额外短命盒**

```text
ConfigId = skill.player_default.extra_blow
Kind = Skill
Modules = [
  PlayAttack(小伤害 AttackSpec),
  GrantListener(Duration=6, SubscribeBasic=true, OnAttackStarted=附加盒)
]
```

**普攻充能，满或到期爆发**

```text
GrantListener(Duration=6, ChargeMax=5, Burst=范围伤害或 AttackSpec)
OnAttackHit → Charge += 1
OnChargeFull / OnExpire → Burst
Replace 时 OnRemove，Charge 作废，不 Burst
```

**范围 Buff / 周期伤害**

```text
ApplyEffect(Targeting=AlliesInRadius 或 EnemiesInRadius, Duration=10, Modifiers 或 Period=1 + OnTick 伤害)
无 PlayAttack 亦可
```

---

## 错误处理

| 情况 | 行为 |
|------|------|
| `ConfigId` 空或重复 | `PushError`；该定义不能激活 |
| `Job.Attack` 的 Kind 不是 Basic | `PushError`；普攻键无效 |
| Skill/Ultimate 的 Kind 不是 Skill | `PushError`；该槽无效 |
| Basic 带自身持续授予 / `GrantListener` | `PushError`；该普攻无效 |
| Combat 存在且 Player 的 `Job.Attack` 为空 | 与职业规格相同：`PushError` |
| `Hitboxes` 为空 | `PushError`；该 `PlayAttack` 跳过 |
| 第一期单盒实现遇到多只 Hitbox | `PushError`；只使用第一只 |
| `Cost != 0` | `PushError`；拒绝激活（本规格无资源池） |
| `Stacking` 非 Replace | `PushError`；仍按 Replace |
| 范围 Targeting 且 `AreaRadius <= 0` | `PushError`；该 `ApplyEffect` 跳过 |
| 无 PlayAttack 且无授予模块 | `PushError`；拒绝激活 |
| 目标无 Health | 命中仍可打日志；不扣血 |
| `GetAttackPower() <= 0` | `TakeDamage` 直接 return |

缺依赖时 `GD.PushError` 并跳过，不抛未处理异常。

---

## 文件清单（实现时）

### 新增（预期）

| 文件 | 职责 |
|------|------|
| `scripts/data/SkillDefinition.cs` | 蓝图：ConfigId、Kind、模块、CD |
| `scripts/data/AttackSpec.cs` | 招式窗口与 Hitbox 列表（替代 AttackData） |
| `scripts/data/SkillModule.cs` 及子类 | `PlayAttack` / `ApplyEffect` / `GrantListener` |
| `scripts/data/GameplayEffect.cs` | 效果蓝图 |
| `scripts/SkillInstance.cs` | 运行时实例（若放在 Combat 内嵌类亦可） |
| `scripts/EffectHolder.cs` | 寿命、Tick、扇出、Replace 级联（或并入 Combat） |
| `data/actors/skills/player_default_attack.tres` | 迁移后的普攻定义 |

### 修改（预期）

| 文件 | 改动 |
|------|------|
| `scripts/data/JobDefinition.cs` | `Attack` / `Skill` / `Ultimate` 改为 `SkillDefinition` |
| `scripts/CombatComponent.cs` | `TryActivate`；按 Kind 发事件；推进实例 |
| `scripts/PlayerInputComponent.cs` | 普攻走 `TryActivate(Job.Attack)` |
| `data/actors/jobs/player_default_job.tres` | 绑新普攻定义 |
| 删除 `AttackData.cs` 与旧 `player_melee_default.tres` 对 `AttackData` 的依赖（可改 script 为 AttackSpec 或新文件替换） |

### 不改

位移、属性、Health、Hurtbox 几何、Map 坐标、存档、Dodge 槽。

---

## 范围外

- 自定义时间轴 / Animation 轨道编辑器
- 闪避、霸体与受击打断的完整状态机（`GrantedTags` 可先存，Combat 尚未读「不可打断」）
- 资源池、真正消耗 Cost
- `Independent` / `Reject` 叠法的实现
- Modifier 改 `GetAttackPower` / `GetMaxHealth` 公式
- 投射物飞行与弹体 Hitbox
- 续招状态机与预输入消费（仅保留 `CancelOpenAt` 字段）
- 技能 UI、CD 条、充能条
- 职业运行时热切换
- 自动化测试 / CI
- 将 `Privot` 更正为 `Pivot`

---

## 实现分期（供后续计划拆分，不改变本文语义）

1. **统一普攻**：`AttackData` → `SkillDefinition(Kind=Basic)` + 单盒 `AttackSpec`；`TryActivate`；行为与现 J 键挥击、扣血一致。  
2. **技能 PlayAttack + 当帧授予骨架**：`Kind=Skill` 实例、双 Id、EffectHolder 寿命、`ApplyEffect`（Self 与半径选目标）、Replace。  
3. **监听扇出**：`GrantListener`、独立 `AttackId` 附加盒、Charge / Burst、`OnExpire` 与 Replace 不爆发。

每一期都必须保持：技能 `PlayAttack` 不发 `Basic*` 事件。

---

## 完成标准（全部期做完后）

1. Player 普攻手感、盒子、扣血与迁移前一致；`Job.Attack` 为 `Kind=Basic` 的 `SkillDefinition`  
2. 一份带 `PlayAttack` + `GrantListener` 的战技：激活当帧挂监听，开头技能伤害不给自己充能、不开额外协同盒；之后普攻触发附加盒且 `AttackId` 与主刀不同  
3. 同 `ConfigId` 再放：旧实例卸掉且不跑到期爆发；新实例新 `RuntimeId`、新 Duration  
4. 两份不同 `ConfigId` 的监听可同时存在，一次普攻两者都触发  
5. `dotnet build` 成功；不要求自动化测试

---

## 实现时建议加载的技能

- `godot-prompter:resource-pattern` — `SkillDefinition` / `AttackSpec` / `GameplayEffect`
- `godot-prompter:ability-system` — 效果寿命、标签、授予与激活门禁
- `godot-prompter:component-system` — EffectHolder / Combat 调度，不自挂物理帧
- `godot-prompter:event-bus` — 仅当信号跨树；同 Actor 内优先组件信号
- `godot-prompter:godot-testing` — 若补测试再加载；本规格不要求
