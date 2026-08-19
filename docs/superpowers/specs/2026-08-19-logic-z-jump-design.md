# 逻辑 Z 轴跳跃设计

日期：2026-08-19  
项目：ProjectC（Godot 4.6 / C#）  
状态：已定稿

## 目标

在既有贴地四向移动之上，实现完整手感的逻辑 Z 轴跳跃：`MovementComponent` 消费 `BaseJumpForce` / `BaseGravity` / `BaseAerialMoveSpeedScale`，通过竖直速度积分更新 `VirtualZ`；表现仍由 `TransformComponent` 将高度投影到 `Privot`，Actor 根与 Shadow 保持贴地。仅贴地可起跳；固定冲量，无土狼/缓冲/可变跳高。

## 约束

- 延续既有管线：Input → Movement 写逻辑坐标 → Transform 写表现；不直接改 `Node2D.Position` 做移动
- 固定起跳冲量：`Jump()` 贴地时设 `m_VerticalVelocity = BaseJumpForce`；松键不打断弧线
- 重力与跳跃参数放在 `ActorMovementConfig`（新增 `BaseGravity`），各角色可调
- 贴地判定：`VirtualZ <= 0` 且 `m_VerticalVelocity <= 0`；空中再按跳忽略
- 水平仍瞬时速度：有输入才积分，松手立刻停；空中速度为 `BaseMoveSpeed * BaseAerialMoveSpeedScale`
- `PhysicsTick` 必须每帧跑竖直积分（输入为零也要重力落地）；调度顺序不变（Player 先 Input，再 Actor Movement）
- 不做土狼、跳跃缓冲、可变跳高、二段跳、动画/影子缩放、可行走区钳制、Enemy AI 跳跃、自动化测试
- 完成标准：C# 编译通过 + 手动跑图；保持节点名拼写 `Privot`

## 架构

采用方案 1：竖直速度态内嵌在 `MovementComponent`；`TransformComponent` 仍为逻辑位置权威并补齐 `GetVirtualZ()`。

```text
PlayerInputComponent.PhysicsTick
  → SetMoveInput(dir)          // 含零向量
  → Jump()                     // 仅贴地时生效

MovementComponent.PhysicsTick(delta)
  1. 非贴地（含刚起跳 velocity > 0）：velocity -= BaseGravity * delta
  2. VirtualZ += velocity * delta
  3. VirtualZ <= 0 → VirtualZ = 0，velocity = 0
  4. 水平：speed = 贴地 ? BaseMoveSpeed : BaseMoveSpeed * BaseAerialMoveSpeedScale
     （有输入才积分 LogicX / LogicDepth）
  5. SetLogicX / SetLogicDepth / SetVirtualZ → Transform 写表现

贴地判定：VirtualZ <= 0 且 m_VerticalVelocity <= 0
```

```text
Player._PhysicsProcess
  ├── PlayerInputComponent.PhysicsTick   // 先：可能 Jump() 写入竖直速度
  └── Actor._PhysicsProcess
        └── MovementComponent.PhysicsTick  // 后：重力 + 积分 + 水平
```

| 类型 | 本轮职责 | 禁止 |
|------|----------|------|
| `MovementComponent` | `m_VerticalVelocity`；`Jump()`；重力/落地；空中移速缩放；写逻辑 XYZ | 直接改世界坐标；自挂 `_PhysicsProcess` |
| `TransformComponent` | 新增 `GetVirtualZ()`；既有 `SetVirtualZ` 写 `Privot` | 读输入、管重力 |
| `ActorMovementConfig` | 新增 `BaseGravity` | 持有运行时速度态 |
| `PlayerInputComponent` / `Actor` / `Player` | 保持现有调度与 `Jump()` 调用 | 本轮改调度顺序 |

## API 与配置

### `ActorMovementConfig`

| 字段 | 类型 | 默认（代码） | 含义 |
|------|------|--------------|------|
| `BaseMoveSpeed` | `float` | `200` | 贴地逻辑 XY 速度 |
| `BaseJumpForce` | `float` | `400` | 起跳瞬间竖直速度（逻辑高度/秒） |
| `BaseAerialMoveSpeedScale` | `float` | `0.7` | 浮空水平移速缩放 |
| `BaseGravity` | `float` | `980` | 逻辑 Z 重力加速度（高度/秒²） |

`.tres`：`player_default_move.tres` / `enemy_default_move.tres` 均写入 `BaseGravity = 980`（Player/Enemy 既有 JumpForce / AerialScale 不变）。

空中水平公式（既有约定，本轮实装）：

`aerialSpeed = BaseMoveSpeed * BaseAerialMoveSpeedScale`

### `TransformComponent`

| API | 行为 |
|-----|------|
| `float GetVirtualZ()` | **新增**：返回当前 `m_VirtualZ` |
| `SetVirtualZ` / `SetLogicX` / `SetLogicDepth` | 不变 |

### `MovementComponent`

| 成员 | 行为 |
|------|------|
| `m_VerticalVelocity` | 运行时竖直速度；贴地且未起跳时为 `0` |
| `Jump()` | 贴地时：`m_VerticalVelocity = BaseJumpForce`；否则忽略 |
| `PhysicsTick(double delta)` | 见下方子步骤；config / transform 无效则 return |
| `SetMoveInput` | 不变（非零归一化） |

`PhysicsTick` 竖直子步骤：

1. 若非贴地（`!(VirtualZ <= 0 && m_VerticalVelocity <= 0)`，因此刚起跳时 `velocity > 0` 即便 `VirtualZ == 0` 也会积分）：`m_VerticalVelocity -= BaseGravity * delta`，再令 `VirtualZ += m_VerticalVelocity * delta`
2. 若 `VirtualZ <= 0`：钳为 `0`，`m_VerticalVelocity = 0`
3. 水平：按是否贴地选速度；`m_MoveInput != Zero` 时积分 `LogicX` / `LogicDepth`
4. 写回 `TransformComponent`（实现时尽量减少同帧多次 `UpdateVisualPosition`，在现有 `Set*` API 下能少则少）

## 错误处理

| 情况 | 行为 |
|------|------|
| Movement 缺 config / Transform | `_Ready` 已有 `PushError`；`PhysicsTick` return |
| 空中连按跳跃 | `Jump()` 忽略 |
| 输入为零 | 不改逻辑 XY；竖直仍积分直至落地 |
| 无 `MapContext` Origin | 保持 Transform 现有：`PushError`，跳过写世界坐标 |

## 文件清单

### 修改

| 文件 | 改动 |
|------|------|
| `scripts/data/ActorMovementConfig.cs` | 新增 `BaseGravity`（默认 `980`） |
| `data/actors/movement/player_default_move.tres` | `BaseGravity = 980` |
| `data/actors/movement/enemy_default_move.tres` | `BaseGravity = 980` |
| `scripts/TransformComponent.cs` | 新增 `GetVirtualZ()` |
| `scripts/MovementComponent.cs` | 竖直速度；实现 `Jump()`；重力/落地/空中移速 |

### 不改（本轮）

| 文件 | 说明 |
|------|------|
| `PlayerInputComponent` / `Actor` / `Player` | 调度与跳跃调用已接好 |
| `MapCoordinates` / `MapContext` / `MapOrigin` | 投影与原点契约不变 |
| `prefabs/*.tscn` | 节点树无需改动 |

## 范围外

- 土狼时间、跳跃缓冲、可变跳高、二段跳
- 加减速、空中惯性
- 可行走区域钳制；非零落地平台高度（始终落回 `VirtualZ = 0`）
- 跳跃动画、影子随高度缩放
- Enemy AI 主动调用 `Jump()`
- 自动化测试 / CI 验收脚本

## 完成标准

1. 贴地按跳：`Privot` 上抛再落回；Shadow 与 Actor 根保持贴地
2. 空中再按跳无效；落地后可再跳
3. 空中四向移速约为地面 × `BaseAerialMoveSpeedScale`；松手立刻停
4. 工程 C# 编译通过；不要求自动化测试
