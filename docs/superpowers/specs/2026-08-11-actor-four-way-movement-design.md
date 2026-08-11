# Actor 贴地四向移动设计

日期：2026-08-11  
项目：ProjectC（Godot 4.6 / C#）  
状态：已定稿

## 目标

实现 `Actor` 的贴地四向（含斜向归一化）移动：`MovementComponent` 消费 `ActorMovementConfig.BaseMoveSpeed`，通过 `TransformComponent` 修改逻辑 `LogicX` / `LogicDepth`；表现层仍由 `TransformComponent` 将逻辑坐标投影到 `Actor.GlobalPosition`，并将 `VirtualZ` 投影到 `Privot`。由 `Actor` / `Player` 统一调度各组件的 `PhysicsTick`，保证同帧内「输入 → 移动」顺序。

## 约束

- 表现写回保持既有设计：`TransformComponent.UpdateVisualPosition` 负责写 `Actor.GlobalPosition` 与 `Privot.Position`；`Actor` 不直接改坐标做表现同步
- 本轮只做贴地走位；`Jump()` 保留空实现，不改 `VirtualZ`、不做重力
- 瞬时速度：有输入时 `velocity = normalize(dir) * BaseMoveSpeed`，松手立刻停；不加加速度 / 摩擦力字段
- 允许斜向；`GetVector` / 缓存方向非零时归一化，对角线不快于单轴
- 输入映射：方向 X → `LogicX`，Y → `LogicDepth`；速度单位为逻辑单位/秒
- 组件通信继续兄弟直连（Movement → Transform）；调度顺序由 Actor 编排，不依赖场景子节点顺序
- 不做可行走区域钳制、AI 驱动 Enemy、自动化测试；完成标准以 C# 编译通过 + 手动跑图为准
- 保持节点名拼写 `Privot`

## 架构

```text
PlayerInputComponent.PhysicsTick(delta)
  → MovementComponent.SetMoveInput(dir)   // 含零向量
  → MovementComponent.Jump()              // 本轮空实现

MovementComponent.PhysicsTick(delta)
  → 读 ActorMovementConfig.BaseMoveSpeed
  → 读 TransformComponent.GetLogicX / GetLogicDepth
  → logic += normalize(dir) * speed * delta
  → TransformComponent.SetLogicX / SetLogicDepth

TransformComponent（既有）
  → Set* → UpdateVisualPosition
  → Actor.GlobalPosition = 贴地投影(LogicX, LogicDepth)
  → Privot.Position = VirtualZ 屏幕偏移（本轮 VirtualZ 不变）
```

```text
Actor._PhysicsProcess(delta)
  └── MovementComponent.PhysicsTick(delta)

Player._PhysicsProcess(delta)
  ├── PlayerInputComponent.PhysicsTick(delta)   // 先输入
  └── base._PhysicsProcess(delta)               // 再 Movement
```

| 类型 | 本轮职责 | 禁止 |
|------|----------|------|
| `PlayerInputComponent` | `PhysicsTick` 读 Input Map 并转发意图 | 改坐标；自挂 `_PhysicsProcess` 做同职责逻辑 |
| `MovementComponent` | 缓存输入；贴地积分；写逻辑 XY | 直接改 `Node2D.Position`；本轮改 `VirtualZ`；自挂物理帧更新 |
| `TransformComponent` | 逻辑态权威 + 表现写回；补齐 `GetLogicX` | 读 Input、解释移速 |
| `Actor` | 缓存组件；调度 `MovementComponent.PhysicsTick` | 解释输入、算移速、写世界坐标 |
| `Player` | 缓存 Input；先 Input tick 再 `base` | 移动物理、表现投影 |
| `ActorMovementConfig` | 继续使用 `BaseMoveSpeed` | 本轮新增加减速字段 |

## API 与帧循环

### `TransformComponent`

| API | 含义 |
|-----|------|
| `float GetLogicX()` | **新增**：返回当前 `m_LogicX` |
| `float GetLogicDepth()` | 已有 |
| `SetLogicX` / `SetLogicDepth` / `UpdateVisualPosition` | 行为不变 |

本轮不新增 `AddLogic*`；Movement 侧读-算-写即可。

### `MovementComponent`

| 成员 | 行为 |
|------|------|
| `_Ready` | 现有：校验并缓存 `MovementConfig`；新增：解析兄弟 `../TransformComponent`，缺失则 `PushError` |
| `SetMoveInput(Vector2 direction)` | 缓存意图；非零则 `Normalized()`，零向量保持 `Zero` |
| `Jump()` | 空实现（保留桩，供后续跳跃） |
| `PhysicsTick(double delta)` | 若 config / transform 无效则 return；若输入为零则 return；否则按 `BaseMoveSpeed * delta` 积分并 `SetLogicX` / `SetLogicDepth` |
| `_Process` / `_PhysicsProcess` | 移除空实现或不在其中做移动逻辑，避免与 Actor 调度双跑 |

### `PlayerInputComponent`

| 成员 | 行为 |
|------|------|
| `_Ready` | 不变：解析兄弟 `MovementComponent` |
| `PhysicsTick(double delta)` | 由原 `_PhysicsProcess` 迁入：`SetMoveInput(GetMoveVector())`；若 `IsJumpJustPressed` 则 `Jump()` |
| `_PhysicsProcess` | 删除（改由 Actor/Player 调用 `PhysicsTick`） |

### `Actor` / `Player` 调度

| 类型 | 行为 |
|------|------|
| `Actor._Ready` | 缓存 `TransformComponent`、`MovementComponent`（缺失则 `PushError`） |
| `Actor._PhysicsProcess` | 若 `MovementComponent` 有效则 `PhysicsTick(delta)` |
| `Player._Ready` | `base._Ready` 后缓存 `PlayerInputComponent`（缺失则 `PushError`） |
| `Player._PhysicsProcess` | 若 Input 有效则先 `PhysicsTick`；再 `base._PhysicsProcess(delta)` |

`TransformComponent` 本轮不进入 tick 队列：`SetLogic*` 时已刷新表现。

## 错误处理

| 情况 | 行为 |
|------|------|
| Movement 缺 `MovementConfig` / `TransformComponent` | `_Ready` `PushError`；`PhysicsTick` 直接 return |
| Player 缺 `PlayerInputComponent` | `PushError`；跳过 Input tick，仍跑 Movement |
| Actor 缺 `MovementComponent` | `PushError`；跳过 Movement tick |
| 输入为零 | Movement 不改逻辑坐标 |
| 无 `MapContext` Origin | 保持 Transform 现有：`PushError`，跳过写世界坐标 |

## 文件清单

### 修改

| 文件 | 改动 |
|------|------|
| `scripts/TransformComponent.cs` | 新增 `GetLogicX()` |
| `scripts/MovementComponent.cs` | 解析 Transform；实现 `SetMoveInput` 缓存与 `PhysicsTick` 积分；移除自驱更新 |
| `scripts/PlayerInputComponent.cs` | `_PhysicsProcess` → `PhysicsTick` |
| `scripts/Actor.cs` | 调度 `MovementComponent.PhysicsTick` |
| `scripts/Player.cs` | 先 Input `PhysicsTick`，再 `base` |
| `prefabs/Player.tscn` | 若根脚本仍为 `Actor.cs`，改为绑定 `Player.cs`（确保 Player 调度生效） |

### 不改（本轮）

| 文件 | 说明 |
|------|------|
| `scripts/data/ActorMovementConfig.cs` | 字段足够 |
| `prefabs/Enemy.tscn` | 已有 Movement；无 Input，不会自行移动 |
| `MapCoordinates` / `MapContext` / `MapOrigin` | 投影与原点契约不变 |

## 范围外

- 跳跃、重力、`VirtualZ` 变化与空中移速缩放实装
- 加减速、滑步摩擦
- 可行走区域多边形/矩形钳制
- Enemy AI / 非玩家 `SetMoveInput` 接线
- 将 `Privot` 更正为 `Pivot`
- 自动化测试 / CI 验收脚本

## 完成标准

1. 按方向键，Player 沿逻辑 XY 以 `BaseMoveSpeed` 移动；斜向归一化后等速
2. 松手立刻停；根节点贴地投影正确；走位不改变 `Privot` 的 VirtualZ 偏移（保持进场高度）
3. 同帧顺序固定：Input `PhysicsTick` → Movement `PhysicsTick`（由 `Player` / `Actor` 保证）
4. Enemy 具备相同 Movement 路径但不自行移动
5. 工程 C# 编译通过；不要求自动化测试
