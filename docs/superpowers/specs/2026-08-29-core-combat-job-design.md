# 核心战斗：属性、职业与位移装配设计

日期：2026-08-29  
项目：ProjectC（Godot 4.6 / C#）  
状态：已定稿

## 目标

在已有逻辑坐标移动、跳跃与近战 AABB 命中之上，落地核心战斗的数据骨架：**属性**（基础生命 / 基础攻击）与 **职业**（位移实现 + 技能槽）。Actor 进场时必须已持有 `ActorDefinition`；由其 `Job` 实例化位移模块，Player 再把 `MovementComponent` 注入输入。命中改为按攻击方结算后的攻击力扣目标生命。本轮做出地面位移预制体迁移、属性扣血，以及职业表上的普攻引用迁移。

## 约束

- 判定真相仍在逻辑三轴 AABB；不把 Godot 2D 物理当命中依据
- 位移物理仍由树上的 `MovementComponent`（或其子类）每帧执行；职业 Resource **不**积分坐标
- 走与跳不拆成两个会写 `TransformComponent` 的预制体；换「完全不同的移动实现」时换整个 `Job.Locomotion`
- `ActorDefinition` 仍是 Actor 上唯一 Export 的模板；职业是它的字段，不在 Actor 上再 Export 一份 Job
- `ActorMovementConfig` 从 `ActorDefinition.Movement` 迁到 `Job.Movement`
- 属性 `.tres` 只存基础值；`MaxHealth` / `AttackPower` 是结算结果。本轮无装备、无 Buff，结算为恒等
- 可变生命在 `HealthComponent`；共享 Resource 进场 `Duplicate()`，禁止改原始 `.tres` 上的当前生命
- 组件不自挂 `_PhysicsProcess` 做玩法；调度仍是 Player 先输入，再 Actor 位移 → 判定 → 战斗
- 完成标准：C# 编译通过 + 手动跑图；不做自动化测试 / CI
- 保持节点名拼写 `Privot`

## 对既有文档的修订

| 文档 | 旧约定 | 新约定 |
|------|--------|--------|
| `2026-08-10-actor-definition-movement-config-design.md` | `ActorDefinition` = `Id` + `Movement`；`MovementComponent` 读 `Definition.Movement` | `ActorDefinition` = `Id` + `Attributes` + `Job`；移动配置在 `Job.Movement`；位移节点由 `Job.Locomotion` 实例化 |
| `2026-08-19-logic-z-jump-design.md` | Player/Enemy 场景静态挂 `MovementComponent`；输入在 `_Ready` 找兄弟 | 场景不挂位移；`Actor._Ready` 生成后再注入 `PlayerInputComponent` |
| `2026-08-24-attack-hit-detection-design.md` | `ActorDefinition` 不加战斗字段；`CombatComponent.Attack` 场景 Export；命中只打日志 | 普攻配置改到 `Job.Attack`；命中扣 `HealthComponent` |
| 四向移动 / 跳跃中的「兄弟直连 Movement」 | `PlayerInput` `_Ready` 用 `../MovementComponent` | 改为 Player 注入；查找按类型，不写死节点名 |

旧文档不逐行改写；以本文为装配与数据的现行约定。

---

## 架构

### 数据

```text
ActorDefinition
  Id
  Attributes ──► CombatAttributes    // BaseHealth, BaseAttack
  Job        ──► JobDefinition
                   Locomotion        // PackedScene，根 = MovementComponent 子类
                   Movement          // ActorMovementConfig
                   Attack            // AttackData；Enemy 可为 null
                   Dodge / Skill / Ultimate  // PackedScene，本轮允许 null，不实例化
```

同一份 `JobDefinition` / `CombatAttributes` / `ActorMovementConfig` 可被多个 `ActorDefinition` 引用。

### 运行时

```text
编辑器场景（无 MovementComponent）
  Actor
  ├── TransformComponent
  ├── HealthComponent          // 新增，Player 与 Enemy 必有
  ├── HurtboxComponent
  ├── Hitbox / Combat          // 仅能出招的角色
  ├── PlayerInputComponent     // 仅 Player
  ├── Shadow
  └── Privot

Actor._Ready 之后
  └── <Locomotion 实例>        // 直接子节点，类型为 MovementComponent
```

```text
结算（本轮恒等，入口先留对）
  MaxHealth    = RuntimeAttributes.BaseHealth
  AttackPower  = RuntimeAttributes.BaseAttack

之后装备 / Buff 只改结算，不把最终值写回 CombatAttributes 蓝图字段
```

---

## 数据类型

### `CombatAttributes`（`[GlobalClass] Resource`）

| 字段 | 类型 | 默认（代码） | 含义 |
|------|------|--------------|------|
| `BaseHealth` | `int` | `100` | 基础生命值 |
| `BaseAttack` | `int` | `10` | 基础攻击力 |

禁止在此 Resource 上存放 `MaxHealth`、`AttackPower`、当前生命。

示例 `.tres`：

| 文件 | BaseHealth | BaseAttack |
|------|------------|------------|
| `data/actors/attributes/player_default_attr.tres` | `100` | `10` |
| `data/actors/attributes/enemy_default_attr.tres` | `30` | `5` |

Enemy 本轮不出招，`BaseAttack` 仍填写，供日后 Enemy 职业出招使用。

### `JobDefinition`（`[GlobalClass] Resource`）

| 字段 | 类型 | 本轮 |
|------|------|------|
| `Locomotion` | `PackedScene` | 必填；根节点必须是 `MovementComponent`（或其子类） |
| `Movement` | `ActorMovementConfig` | 必填；原 `ActorDefinition.Movement` 迁入 |
| `Attack` | `AttackData` | Player 必填；Enemy 允许 `null` |
| `Dodge` | `PackedScene` | 允许 `null`，不实例化 |
| `Skill` | `PackedScene` | 允许 `null`，不实例化 |
| `Ultimate` | `PackedScene` | 允许 `null`，不实例化 |

示例：

- `data/actors/jobs/player_default_job.tres`：地面位移场景 + `player_default_move.tres` + `player_melee_default.tres`
- `data/actors/jobs/enemy_default_job.tres`：同一地面位移场景 + `enemy_default_move.tres` + `Attack = null`

Player 与 Enemy **共用** `prefabs/locomotion/ground_locomotion.tscn`，差异只在 `Movement` 数值。

### `ActorDefinition`

| 字段 | 类型 | 含义 |
|------|------|------|
| `Id` | `string` | 不变 |
| `Attributes` | `CombatAttributes` | 属性蓝图 |
| `Job` | `JobDefinition` | 职业蓝图 |

**删除** `Movement`。`player_default.tres` / `enemy_default.tres` 改为引用 Attributes + Job。

### `ActorMovementConfig` / `AttackData`

字段与现有实现不变。读取路径改为 `Definition.Job.Movement` / `Definition.Job.Attack`。

---

## 结算与生命

### 查询入口（Actor）

Actor 在 `_Ready` 开头对 `Definition.Attributes` 做 `Duplicate()`，缓存为运行时副本（空则 `PushError`，结算视为 0）。

| API | 本轮行为 |
|-----|----------|
| `int GetMaxHealth()` | 返回运行时 `BaseHealth`；Attributes 无效则 `0` |
| `int GetAttackPower()` | 返回运行时 `BaseAttack`；Attributes 无效则 `0` |

`HealthComponent` 与 `CombatComponent` **只读这两个 API**，不读 `BaseHealth` / 不自己缓存一份会过期的最大生命。日后结算改公式时只改 Actor（或日后抽 StatSet），组件不用跟着改。

### `HealthComponent`（`Node`，Actor 直下）

| 成员 | 行为 |
|------|------|
| `int CurrentHealth` | 当前生命；只通过 API 修改 |
| `void InitializeFromActor()` | `CurrentHealth = actor.GetMaxHealth()`；若最大生命 `<= 0` 则当前为 `0` |
| `void TakeDamage(int amount)` | `amount <= 0` 或已死亡：return。扣到 `max(0, current - amount)`，发 `HealthChanged`；刚降到 0 时发一次 `Died` |
| `void Heal(int amount)` | `amount <= 0` 或已死亡：return。加到 `min(GetMaxHealth(), current + amount)`，发 `HealthChanged` |
| `bool IsDead` | `CurrentHealth <= 0` |
| 信号 `HealthChanged(int oldValue, int newValue)` | 当前生命变化 |
| 信号 `Died` | 仅第一次进入 0 |

**不要**在 `HealthComponent._Ready` 里读属性：子节点 `_Ready` 早于 `Actor._Ready` 的 Duplicate。由 `Actor._Ready` 在 Duplicate 与缓存组件之后调用 `InitializeFromActor()`。

### 死亡

| 角色 | `Died` |
|------|--------|
| Enemy | `QueueFree()`（Hurtbox `_ExitTree` 已注销注册表） |
| Player | 只打日志；不 `QueueFree`；`TakeDamage` 在已死亡时直接 return |

Actor 在 `_Ready` 连接 `Health.Died`：父节点是 `Player` 则日志，否则 `QueueFree`。用 `is Player` 区分，不引入新阵营字段。

### 命中结算

`CombatComponent` 不再 Export `AttackData`。`_Ready` 从父 `Actor.Definition.Job.Attack` 读取；为 `null` 则 `PushError`（仅当该 Actor 挂了 Combat 却没有普攻配置）。`TryStartAttack` / 开盒逻辑不变。

`OnHit`：

1. 仍打印现有命中日志
2. 取攻击方父 Actor 的 `GetAttackPower()`
3. 取 `hurtbox.GetOwnerActor()` 上的 `HealthComponent`；缺则跳过扣血（不抛未处理异常）
4. `TakeDamage(attackPower)`

伤害数字本轮等于攻击力，不读 `AttackData` 伤害字段（该 Resource 仍只有时长与盒子）。

---

## 运行时装配

### 位移预制体

`prefabs/locomotion/ground_locomotion.tscn`：

- 根节点类型 `Node`，脚本为现有 `MovementComponent`
- 根即位移组件，**不要**再包一层容器（避免 `../TransformComponent` 与按类型取根失败）
- 本轮不引入 `FlyMovementComponent`；日后换实现 = 新脚本继承 `MovementComponent` + 新 `.tscn` + 换 `Job.Locomotion`

`Player.tscn` / `Enemy.tscn` **删除**静态 `MovementComponent` 节点。

### `Actor._Ready` 顺序（写死）

1. 校验 `Definition`、`Definition.Attributes`、`Definition.Job`、`Job.Locomotion`、`Job.Movement`；失败则 `PushError` 并跳过后续位移生成（仍尝试缓存其它已有子节点）
2. `m_RuntimeAttributes = Definition.Attributes.Duplicate() as CombatAttributes`
3. 若直接子节点里**已经**有 `MovementComponent`：`PushError`（预制体未删静态位移），**不再**实例化第二份
4. 否则 `var locomotion = Job.Locomotion.Instantiate()`；若根不是 `MovementComponent`：`PushError`，`QueueFree` 实例，跳过 AddChild
5. 仅当第 4 步得到合法实例时 `AddChild(locomotion)`（直接子节点）
6. 按**类型**在直接子节点中缓存：`TransformComponent`、`MovementComponent`、`HealthComponent`、`HurtboxComponent`；`CombatComponent` / `HitboxComponent` 仍可选
7. `HealthComponent.InitializeFromActor()`；缺 Health：`PushError`
8. 连接 `Health.Died`

缺 `TransformComponent` / `HurtboxComponent`：保持现有 `PushError` 策略。

公开只读：`Movement`、`Health`、`Combat`（可空），供 `Player` 注入。

### `MovementComponent._Ready`

- 配置改为 `GetParent<Actor>().Definition.Job.Movement`（在实例化时 Definition 已在节点上）
- `TransformComponent`：向父 Actor 的直接子节点按类型查找，**禁止**写死 `../TransformComponent`（根节点名将是场景名，不再保证叫 `MovementComponent`）
- `SetMoveInput` / `Jump` / `PhysicsTick` 行为不变（地面走跳 + 重力）

### `Player._Ready`

```text
base._Ready()
绑定 PlayerInput.Bind(Movement, Combat)
```

`PlayerInputComponent`：删除 `_Ready` 内对 Movement / Combat 的路径查找；新增 `Bind(MovementComponent movement, CombatComponent combat)`。未注入时 `PhysicsTick` 直接 return。`Jump()` / `SetMoveInput` / `TryStartAttack` 调用对象改为注入引用。

每帧顺序不变：

```text
Player._PhysicsProcess
  → PlayerInput.PhysicsTick
  → Actor._PhysicsProcess
        Movement.PhysicsTick
        Hitbox.PhysicsTick
        Combat.PhysicsTick
        Hurtbox.RedrawDebug
```

`Actor._PhysicsProcess` 对本次生成的 `Movement` 使用缓存引用，不再 `GetNode("MovementComponent")`。

### 运行时再生成角色

生成器必须在 `AddChild` / 进树**之前**设置 `Actor.Definition`。进树后改 Definition **不**本轮热切换职业。

---

## 错误处理

| 情况 | 行为 |
|------|------|
| `Definition` / `Job` / `Locomotion` / `Job.Movement` / `Attributes` 为空 | `PushError`（含节点路径）；不静默套默认职业或默认移速 |
| Locomotion 根不是 `MovementComponent` | `PushError`；不挂上；输入 Bind 到 null，无法移动 |
| Player 场景仍残留静态 Movement 且职业又生成一份 | **禁止**；预制体必须删掉静态节点。实现时不在代码里合并两份 |
| Combat 存在但 `Job.Attack` 为 null | `PushError`；`TryStartAttack` return |
| 目标无 Health | 命中日志仍打；不扣血 |
| `GetAttackPower() <= 0` | `TakeDamage` 直接 return，相当于未造成伤害 |
| Player 已死亡后再命中 | 不扣、不重复 `Died` |
| `Dodge` / `Skill` / `Ultimate` 非 null | 本轮忽略，不实例化、不报错 |

缺依赖时 `GD.PushError` 并跳过，不抛未处理异常。

---

## 文件清单

### 新增

| 文件 | 职责 |
|------|------|
| `scripts/data/CombatAttributes.cs` | 基础生命 / 基础攻击 |
| `scripts/data/JobDefinition.cs` | 职业槽与位移场景 |
| `scripts/HealthComponent.cs` | 当前生命、受伤、死亡信号 |
| `prefabs/locomotion/ground_locomotion.tscn` | 地面 `MovementComponent` 预制体 |
| `data/actors/attributes/player_default_attr.tres` | Player 属性 |
| `data/actors/attributes/enemy_default_attr.tres` | Enemy 属性 |
| `data/actors/jobs/player_default_job.tres` | Player 默认职业 |
| `data/actors/jobs/enemy_default_job.tres` | Enemy 默认职业 |

### 修改

| 文件 | 改动 |
|------|------|
| `scripts/data/ActorDefinition.cs` | 删 `Movement`；加 `Attributes`、`Job` |
| `data/actors/player_default.tres` | 改绑 Attributes + Job |
| `data/actors/enemy_default.tres` | 同上 |
| `scripts/Actor.cs` | Duplicate 属性；实例化 Locomotion；按类型缓存；初始化 Health；`Died`；公开查询与组件 getter |
| `scripts/Player.cs` | `base._Ready` 后 `Bind` |
| `scripts/PlayerInputComponent.cs` | `Bind`；去掉 `_Ready` 路径查找 |
| `scripts/MovementComponent.cs` | 读 `Job.Movement`；按类型找 Transform |
| `scripts/CombatComponent.cs` | 读 `Job.Attack`；命中扣血 |
| `prefabs/Player.tscn` | 去掉 Movement；挂 Health；去掉 Combat 上的 Attack Export 绑定 |
| `prefabs/Enemy.tscn` | 去掉 Movement；挂 Health |

### 不改（本轮）

| 文件 | 说明 |
|------|------|
| `ActorMovementConfig.cs` / 两份 `*_move.tres` | 仅引用方改变 |
| `AttackData.cs` / `player_melee_default.tres` | 不增加伤害字段 |
| `TransformComponent` / `Hitbox` / `Hurtbox` / `LogicAabb` | 判定几何不变 |
| 存档框架 | 不写当前 HP |

---

## 范围外

- 闪避无敌、战技、大招的实例化与逻辑
- 飞行等第二套位移实现（只要求 Locomotion 槽能换场景）
- `StatModifier` / 装备 / Buff 公式（只保留 `GetMaxHealth` / `GetAttackPower` 恒等）
- 防御、暴击、击退、硬直、顿帧、死亡动画、HP UI
- 职业运行时热切换
- 把当前生命写入存档
- 自动化测试
- 将 `Privot` 更正为 `Pivot`

---

## 完成标准

1. Player / Enemy 场景无静态 `MovementComponent`；进场后能按职业生成地面位移，手感与现有走跳一致
2. 改 `Job.Movement` 的移速 / 跳跃力仍生效；缺职业或位移场景时有 `PushError` 且不套默认实现
3. Player 按 **J** 命中 Enemy：除命中日志外，Enemy 生命按 Player `AttackPower`（本轮 = 10）减少；约三次命中后 Enemy 从场景移除
4. Player 不会因自己的攻击扣血；Enemy 无 Combat 时不能出招
5. `dotnet build` 对应工程成功；不要求自动化测试

---

## 实现时建议加载的技能

- `godot-prompter:resource-pattern` — `CombatAttributes` / `JobDefinition`
- `godot-prompter:component-system` — `HealthComponent`、注入而非兄弟路径
- `godot-prompter:ability-system` — 属性基础值与最终值分层（本轮不做 modifier 流水线）
- `godot-prompter:player-controller` — 位移仍在 `_PhysicsProcess` 等价的 `PhysicsTick` 中
- `godot-prompter:scene-organization` — 位移拆成可实例化子场景
