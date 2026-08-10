# 地图原点与逻辑坐标变换设计

日期：2026-08-10  
项目：ProjectC（Godot 4.6 / C#）  
状态：已定稿

## 目标

为 Beat ’em up 伪 3D 走位建立统一的「逻辑坐标 ↔ 世界坐标」映射：用场景中的 `MapOrigin` 标定逻辑 `(0, 0)`，以逻辑坐标为运行时权威，正确初始化已摆在场景中的 Actor，并为后续运行时生成提供同一套 API。同时明确：`Actor` 根节点只表示贴地走位；`VirtualZ`（跳跃高度）只作用在离地枢轴 `Privot` 上。

## 约束

- 运行时权威始终是逻辑坐标（`LogicX` / `LogicDepth` / `VirtualZ`）；`Node2D` 位置是投影结果
- 投影常量只定义一处（`MapCoordinates`），`TransformComponent` 不得再各自维护一套
- Actor 预制体不写死 `../../Map/MapOrigin` 路径；通过 Autoload `MapContext` 取当前关卡原点
- 换算使用 `MapOrigin.GlobalPosition`，以便整图平移后仍正确
- 反算世界→逻辑时约定贴地（`VirtualZ = 0`）；高度与深度在屏幕 Y 上混叠，不尝试从单一屏幕 Y 反解高度
- 保持项目既有节点名拼写 `Privot`（由原 `RenderPrivot` 更名），本轮不改名为英文 `Pivot`
- 本轮不做可行走区域钳制、HurtBox、真实移动物理、存档读写逻辑坐标

## 架构

```text
Main
├── Map
│   ├── MapOrigin (Marker2D + MapOrigin.cs)  ← 逻辑 (0,0) 世界落点；进出树注册/清理
│   └── Background / Mid / Foreground …
└── Actors
    └── Player / Enemy …
```

```text
MapOrigin._EnterTree → MapContext.RegisterOrigin
MapOrigin._ExitTree  → MapContext.ClearOrigin

TransformComponent
  ├─ 读 MapContext.Origin.GlobalPosition
  ├─ MapCoordinates：Logic ↔ World
  ├─ 写 Actor.GlobalPosition（仅 LogicX + LogicDepth）
  └─ 写 Privot.Position（仅 VirtualZ 屏幕偏移）
```

| 类型 | 职责 | 禁止 |
|------|------|------|
| `MapCoordinates` | 静态换算与投影常量 | 持有 Node、读写场景树 |
| `MapContext` | Autoload：当前关卡原点注册表 | 做投影公式、改 Actor 位置 |
| `MapOrigin` | 标定原点并自注册/自清理 | 游戏玩法逻辑 |
| `TransformComponent` | 存逻辑位姿并驱动根 + `Privot` | 直接读 Input、写移动物理 |

## 换算公式与 API

### 投影常量（唯一来源：`MapCoordinates`）

| 常量 | 值 | 含义 |
|------|-----|------|
| `DepthToScreenY` | `0.5f` | 逻辑深度 → 屏幕 Y |
| `HeightToScreenY` | `1.0f` | 虚拟高度 → 屏幕 Y（向上为负向偏移） |

### 公式

相对 `origin = MapOrigin.GlobalPosition`：

```text
地面世界坐标（忽略 VirtualZ）：
  WorldGround = Origin + (LogicX, LogicDepth * DepthToScreenY)

离地枢轴本地偏移：
  PrivotLocal = (0, -VirtualZ * HeightToScreenY)

贴地反算（VirtualZ 视为 0）：
  local = World - Origin
  LogicX     = local.X
  LogicDepth = local.Y / DepthToScreenY
```

### `MapCoordinates`（静态类）

| API | 含义 |
|-----|------|
| `Vector2 LogicToWorld(Vector2 origin, float logicX, float logicDepth, float virtualZ = 0)` | 完整逻辑 → 世界（含可选 VirtualZ；**根节点调用时 virtualZ 必须为 0**） |
| `void WorldToLogicGround(Vector2 origin, Vector2 world, out float logicX, out float logicDepth)` | 世界 → 贴地逻辑 |
| `Vector2 VirtualZScreenOffset(float virtualZ)` | 返回 `(0, -virtualZ * HeightToScreenY)`，供 `Privot` 使用 |

### `MapContext`（Autoload）

| API | 含义 |
|-----|------|
| `void RegisterOrigin(Node2D origin)` | 进关注册；`origin == null` 则 `PushError` |
| `void ClearOrigin()` | 离关清理 |
| `Node2D Origin { get; }` | 当前原点；未注册时访问应 `PushError` 并视为无效 |
| `bool HasOrigin` | 是否已注册 |

在 `project.godot` `[autoload]` 中注册，与现有 `SaveService` 并列。

### `MapOrigin`（挂在 `Marker2D` / `Node2D`）

- `_EnterTree`：`MapContext.RegisterOrigin(this)`
- `_ExitTree`：若当前注册仍是自身则 `MapContext.ClearOrigin()`

## Actor 场景约定

```text
Actor (Node2D)
├── TransformComponent
├── MovementComponent
├── …（如 PlayerInputComponent）
├── Shadow                 ← 贴地；相对 Actor 保持地面位，不吃 VirtualZ
└── Privot                 ← 离地枢轴（由 RenderPrivot 更名）
    ├── Render             ← 保留既有美术锚点（如 position.y = -75）
    ├── ForwardArrow
    └── （日后 HurtBox 等与表现/受击同高的节点）
```

| 目标 | 逻辑量 | 写入目标 |
|------|--------|----------|
| 地面位姿（走位、Y-sort、贴地阴影） | `LogicX` + `LogicDepth` | `Actor.GlobalPosition` |
| 离地位姿（精灵、日后 HurtBox） | `VirtualZ` 屏幕偏移 | `Privot.Position`（相对 Actor） |

`Shadow` 从原 `RenderPrivot` 下挪到 `Actor` 直下，避免随跳跃离地。

## 初始化 / 生成 / 每帧写回

### 编辑器已摆好的 Actor

1. `MapOrigin` 进入树 → 注册到 `MapContext`
2. `TransformComponent._Ready`：解析父 `Actor` 与子级 `Privot`（路径 `Privot`）；缺则 `PushError`（根仍可更新，不写离地偏移）
3. **固定用 `CallDeferred(nameof(InitializeFromWorldPose))` 做反算初始化**，确保同帧内 `MapOrigin` 已完成注册
4. `InitializeFromWorldPose`：若 `MapContext.HasOrigin`，用 `Actor.GlobalPosition` 做 `WorldToLogicGround`，`VirtualZ = 0`，再 `UpdateVisualPosition` 写回（数值应与摆位一致）；若无 Origin：`PushError`（含节点路径），跳过写位置，不静默当作 `(0,0)`

### `TransformComponent` 对外位姿 API（本轮）

| API | 含义 |
|-----|------|
| `SetLogicX` / `SetLogicDepth` / `SetVirtualZ` | 分项写入并刷新视觉 |
| `SetLogicPose(float logicX, float logicDepth, float virtualZ = 0)` | 聚合写入，供生成器一次设定 |

### 运行时生成（后续 Spawner，本轮只定契约）

```text
传入 logicX, logicDepth, virtualZ（默认 0）
  → Instantiate 并 AddChild
  → TransformComponent.SetLogicPose(...)
  → 内部 UpdateVisualPosition
```

禁止在生成后再走「编辑器反算」覆盖逻辑值，除非输入本身是屏幕点：先 `WorldToLogicGround`，再 `SetLogicPose`。

### 每帧 / 移动后

- `MovementComponent` 等只改逻辑（`SetLogicX` / `SetLogicDepth` / `SetVirtualZ` 或聚合 `SetLogicPose`）
- `TransformComponent.UpdateVisualPosition`：
  - `Actor.GlobalPosition = LogicToWorld(origin, LogicX, LogicDepth, virtualZ: 0)`
  - `Privot.Position = VirtualZScreenOffset(VirtualZ)`（不覆盖 `Render` 的美术锚点）
- `MovementComponent` 不直接改 `Position`

### 离关

`MapOrigin` 退出树时清理 `MapContext`，避免下一关读到旧原点。

### 错误处理

| 情况 | 行为 |
|------|------|
| 无 Origin 却初始化/投影 | `GD.PushError`（含路径），跳过写位置 |
| 父节点不是 `Actor` | 保持现有：`PushError` 并 return |
| 缺失 `Privot` | `PushError`；仍可写根贴地位姿 |

## 文件清单

### 新增

| 文件 | 职责 |
|------|------|
| `scripts/MapCoordinates.cs` | 静态换算 + 投影常量 |
| `scripts/MapContext.cs` | Autoload 原点注册表 |
| `scripts/MapOrigin.cs` | 原点节点自注册/自清理 |

### 修改

| 文件 | 改动 |
|------|------|
| `project.godot` | 注册 Autoload `MapContext` |
| `main.tscn` | `Map` 下增加 `MapOrigin` |
| `prefabs/Player.tscn`（及同构的 Enemy） | `RenderPrivot` → `Privot`；`Shadow` 挪到 Actor 直下 |
| `scripts/TransformComponent.cs` | 反算初始化；根贴地；`Privot` 吃 VirtualZ；常量改引用 `MapCoordinates` |

## 范围外

- 可行走区域多边形/矩形钳制
- HurtBox / 攻击盒实现（仅约定挂在 `Privot` 下）
- `MovementComponent` 真实位移与跳跃物理
- 存档序列化逻辑坐标
- 将 `Privot` 更正拼写为 `Pivot`

## 完成标准

1. 关卡存在 `MapOrigin`，进出树自动注册/清理到 `MapContext`
2. 场景内已摆 Player：启动后由根 `GlobalPosition` 反算逻辑坐标；根位置不因 `VirtualZ` 漂移
3. `SetVirtualZ` 只移动 `Privot`；`Shadow` 与 `Actor` 根保持贴地
4. `DepthToScreenY` / `HeightToScreenY` 仅存在于 `MapCoordinates`
5. 工程 C# 编译通过；不要求自动化测试
