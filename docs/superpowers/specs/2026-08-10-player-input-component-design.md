# 玩家输入组件设计

日期：2026-08-10  
项目：ProjectC（Godot 4.6 / C#）  
状态：已定稿

## 目标

将玩家游戏输入从移动逻辑中拆出：由 `PlayerInputComponent` 读取 Input Map，再调用 `MovementComponent` 的操控入口。同时用静态常量类统一 Input Action 名称，消除魔法字符串。本轮只搭接线与桩 API，不实现真实移动/跳跃物理。

## 约束

- 组件通信采用兄弟节点直连（与现有 `Actor` / `MovementComponent` 一致）
- 已存在 Input Map：`move_up` / `move_down` / `move_left` / `move_right` / `jump`
- 不接入手柄轴、输入缓冲、重绑定 UI
- 不实现 `MovementComponent` 的真实位移与跳跃物理
- 不改动 `Actor` 的职责边界（输入不经过 Actor 转发）

## 架构

```text
scripts/
  InputActions.cs              // 静态常量：Input Action 名
  PlayerInputComponent.cs      // 读输入 → 调 Movement
  MovementComponent.cs         // 新增桩方法 SetMoveInput / Jump
prefabs/
  Player.tscn                  // 增加 PlayerInputComponent 节点
```

```text
键盘/手柄
   ↓ Input Map
PlayerInputComponent (_PhysicsProcess)
   ↓ SetMoveInput(Vector2) / Jump()
MovementComponent（本轮空实现）
```

| 类型 | 职责 | 禁止 |
|------|------|------|
| `InputActions` | 集中存放与 Input Map 一致的 `const string` | 持有 Node、读输入副作用 |
| `PlayerInputComponent` | 轮询输入并转发意图给 Movement | 改 Transform、写物理位移 |
| `MovementComponent` | 接收移动/跳跃意图（桩） | 本轮不读 Input |

## 场景结构

`Player.tscn` 根节点下新增兄弟节点：

```text
Player (Actor)
├── TransformComponent
├── MovementComponent
├── PlayerInputComponent   ← 新增
└── RenderPrivot
    └── …
```

`PlayerInputComponent` 在 `_Ready` 中通过相对路径获取兄弟：

```csharp
m_Movement = GetNode<MovementComponent>("../MovementComponent");
```

## API 表面

### `InputActions`（`public static class`）

| 常量 | 值（须与 project.godot `[input]` 一致） |
|------|----------------------------------------|
| `MoveUp` | `"move_up"` |
| `MoveDown` | `"move_down"` |
| `MoveLeft` | `"move_left"` |
| `MoveRight` | `"move_right"` |
| `Jump` | `"jump"` |

另提供静态辅助：

- `Vector2 GetMoveVector()` — 内部调用 `Input.GetVector(MoveLeft, MoveRight, MoveUp, MoveDown)`
- `bool IsJumpJustPressed()` — 内部调用 `Input.IsActionJustPressed(Jump)`

`PlayerInputComponent` 必须通过上述辅助读取，禁止再直接拼装 action 名。

### `PlayerInputComponent`（`Node`）

- `_Ready`：解析 `MovementComponent` 兄弟引用；缺失时 `GD.PushError` 并跳过后续转发（置空引用即可）
- `_PhysicsProcess`：
  - 每帧：`SetMoveInput(InputActions.GetMoveVector())`（含零向量，便于停止）
  - 当帧：若 `InputActions.IsJumpJustPressed()` 则 `Jump()`

### `MovementComponent`（桩方法）

- `void SetMoveInput(Vector2 direction)` — 空实现
- `void Jump()` — 空实现

## 约定

- 读输入在 `_PhysicsProcess`，与后续物理移动同频
- 水平/深度轴：`GetVector` 的 X = 左右，Y = 上下（对应 `move_up`/`move_down`）；后续 Movement 自行解释到逻辑坐标
- 所有 Input Action 字符串只通过 `InputActions` 引用，禁止在组件内写字面量
- 类名文件：`PlayerInputComponent.cs`（用户提到的拼写 `PlayerInputCompoent` 视为笔误，以 Component 为准）

## 范围外

- 真实移动速度、加速度、跳跃高度与重力
- AI / 非玩家实体复用输入组件
- 通过 Actor 注入依赖
- 手柄单独轴映射、触控虚拟摇杆
- 自动化测试（本轮由手动在编辑器确认节点与脚本绑定即可）

## 验收标准

1. `InputActions` 常量与现有 Input Map 名称一一对应
2. `Player.tscn` 存在绑定了 `PlayerInputComponent.cs` 的节点
3. 运行时该组件能解析到 `MovementComponent`，并在按键时调用桩方法（可用临时日志或断点验证；合入前可不留日志）
4. 业务代码中不再出现 `"move_up"` 等 Input Action 魔法字符串（本轮新增代码范围内）
