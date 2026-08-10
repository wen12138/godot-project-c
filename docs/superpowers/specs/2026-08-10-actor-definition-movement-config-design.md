# Actor 定义与移动配置设计

日期：2026-08-10  
项目：ProjectC（Godot 4.6 / C#）  
状态：已定稿

## 目标

为 Player / Enemy 共用的 `Actor` 增加可复用的静态定义数据，优先覆盖移动相关字段，供后续 `MovementComponent` 实现真实位移与跳跃时消费。本轮交付 Resource 类型、示例 `.tres`、场景 Export 绑定，以及 `MovementComponent` 对配置的只读缓存接线；不实现移动物理，不做自动测试（以编译通过为准）。

## 约束

- Player 与 Enemy 共用同一 `Actor` + `MovementComponent`；差异靠场景组成与不同 `ActorDefinition`，不引入 `PlayerActor` / `EnemyActor` 子类
- 静态调参用 Godot `Resource`（`.tres`），与存档层（`SaveGameDto` / `SaveService` / `user://` JSON）分离
- `Actor` 根节点 Export `ActorDefinition`；`MovementComponent` 向父节点拉取 `Definition.Movement`
- 配置只读共享，本轮不对定义数据 `Duplicate()`
- 不做 `ActorCatalog`、按 Id 运行时查表、存档读写 Definition、Buff 改数值
- 不做自动化验收 / 单元测试；实现后以 C# 编译通过为完成标准

## 架构

采用「组合引用」：移动手感独立成行，Actor 定义行再引用它，便于多种角色共用同一套移动参数。

```text
scripts/data/
  ActorMovementConfig.cs   // 移动手感行
  ActorDefinition.cs       // Actor 表的一行（Id + Movement 引用）

data/actors/
  movement/
    player_default_move.tres
    enemy_default_move.tres
  player_default.tres
  enemy_default.tres

prefabs/
  Player.tscn  → Definition = player_default.tres
  Enemy.tscn   → Definition = enemy_default.tres
```

```text
ActorDefinition (.tres)
  Id + Movement ──ref──► ActorMovementConfig (.tres)
                              │
Actor [Export] Definition     │
  │                           │
  └─ MovementComponent._Ready ┘ 缓存只读引用
```

| 类型 | 职责 | 禁止 |
|------|------|------|
| `ActorMovementConfig` | 存放基础移速 / 跳跃力 / 浮空移速缩放 | 持有 Node、写物理逻辑 |
| `ActorDefinition` | 稳定 Id + 对 Movement 的引用；日后可扩非移动字段 | 存档序列化、运行时可变状态 |
| `Actor` | Export 并暴露 `Definition` | 解释速度、转发输入 |
| `MovementComponent` | `_Ready` 校验并缓存 `ActorMovementConfig` | 本轮实现位移 / 跳跃物理 |

## 数据类型

### `ActorMovementConfig`（`[GlobalClass] Resource`）

| 字段 | 类型 | 默认值 | 含义 |
|------|------|--------|------|
| `BaseMoveSpeed` | `float` | `200` | 逻辑 XY（`LogicX` / `LogicDepth`）基础移动速度 |
| `BaseJumpForce` | `float` | `400` | 基础跳跃力（影响跳跃高度） |
| `BaseAerialMoveSpeedScale` | `float` | `0.7` | 浮空且可操控时与 `BaseMoveSpeed` 相乘得到浮空移速 |

后续移动公式（本轮只文档约定，不实现）：

`aerialSpeed = BaseMoveSpeed * BaseAerialMoveSpeedScale`

### `ActorDefinition`（`[GlobalClass] Resource`）

| 字段 | 类型 | 含义 |
|------|------|------|
| `Id` | `string` | 稳定键，如 `"player_default"` / `"enemy_default"`；与文件名对齐 |
| `Movement` | `ActorMovementConfig` | 移动配置引用；可被多个 Definition 共用 |

## 场景绑定与接线

```text
Player / Enemy (Actor)
├── [Export] Definition: ActorDefinition
├── TransformComponent
├── MovementComponent
├── (仅 Player) PlayerInputComponent
└── RenderPrivot
```

1. `Actor` 增加 `[Export] public ActorDefinition Definition { get; set; }`（或等价 Export 属性/字段，与项目现有风格一致）。
2. `MovementComponent._Ready`：`GetParent<Actor>()`，若 `Definition == null` 或 `Definition.Movement == null`，则 `GD.PushError`（含节点路径）并中止缓存；不静默回退默认数值。
3. 校验通过后缓存 `ActorMovementConfig` 只读引用；可提供只读属性（如 `MovementConfig`）供后续逻辑与调试读取。
4. `PlayerInputComponent` 与输入桩 API（`SetMoveInput` / `Jump`）保持不变；输入仍兄弟直连 Movement，配置由 Movement 向父 Actor 拉取。

## 与存档框架的边界

- `ActorDefinition` / `ActorMovementConfig` 是静态设计数据，不写入 `SaveGameDto`
- 日后若存档需要区分角色模板，只持久化 `Id`（或场景引用），不序列化整份 Resource
- `scripts/data/` 可同时容纳存档类型与静态 Resource 类型；资产文件放在 `res://data/actors/`

## 范围外

- 真实位移、重力、跳跃物理
- `ActorCatalog` Autoload 或按 Id 字典查表
- 存档读写、版本迁移涉及 Actor 定义
- 运行时修改基础数值（Buff）及 `Duplicate()`
- 血量、攻击等非移动字段（预留扩 `ActorDefinition`）
- 自动化测试、CI 验收脚本

## 完成标准

- 新增类型可被编辑器识别（`[GlobalClass]`），并能创建对应 `.tres`
- Player / Enemy prefab 已绑定示例 `ActorDefinition`
- 工程 C# 编译通过
- 不要求自动测试或运行时自动验收
