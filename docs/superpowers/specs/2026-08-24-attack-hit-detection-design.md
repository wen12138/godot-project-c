# 近战攻击命中判定设计

日期：2026-08-24  
项目：ProjectC（Godot 4.6 / C#）  
状态：已定稿

## 目标

为 Beat ’em up 伪 3D 落地**判定核心**：在逻辑空间用三轴 AABB（`LogicX` / `LogicDepth` / `VirtualZ`）做近战命中；Player 按攻击键开启一次挥击攻击盒；同一挥击对同一 Hurtbox 只算一次；开发时绘制调试盒。本轮只发命中信号与日志，**不扣血、不结算击退/硬直**。

## 约束

- **判定真相在逻辑空间**，不是屏幕空间，也不是 Godot 2D 物理空间。真正命中 = 三轴盒子重叠。
- `Area2D` / `CollisionShape2D` **不是**命中依据；本轮不挂这些节点做检测。调试绘制用 `Node2D._Draw` 把逻辑盒投影到屏幕。
- 延续既有调度：组件不自挂 `_PhysicsProcess` 做玩法。`Player` 先跑输入，`Actor` 再跑移动，然后跑命中。
- 组件是 `Actor` 直下兄弟 `Node` / `Node2D`，兄弟直连；节点名拼写保持 `Privot`。
- 朝向权威是 `TransformComponent.GetFacing()`，不要用 `Privot.Scale` 当判定依据。
- 阵营最小可用：`Player` vs `Enemy`。不要用 Godot 32 层物理层当真相。
- 本轮一个近战攻击；时长与盒子尺寸用小型 `AttackData` Resource。没有 AnimationPlayer，不做动画命中帧。
- 完成标准：C# 编译通过 + 手动跑图；不做自动化测试 / CI。
- `ActorDefinition` 本轮仍只有 `Id` + `Movement`，不把血量/攻击字段加进去。

## 对既有文档的修订

`docs/superpowers/specs/2026-08-10-map-origin-logic-transform-design.md` 曾约定：日后 HurtBox 挂在 `Privot` 下，以便与跳跃同高。

**本轮修订该约定：**

| 旧约定 | 新约定 |
|--------|--------|
| HurtBox 作为 `Privot` 子节点，跟精灵同屏高 | Hurtbox / Hitbox **数据与判定**是 `Actor` 直下组件，用逻辑坐标 |
| 屏幕重叠或 2D 物理重叠视为命中 | 屏幕重叠 ≠ 逻辑命中；`Area2D` 最多未来做调试，本轮连这个也不做 |
| 盒子挂在 `Privot` 下以跟随 `VirtualZ` | 盒子每帧从 `TransformComponent` 读 `LogicX` / `LogicDepth` / `VirtualZ` 构图；跳跃高度已经在逻辑 Z 里 |

调试矩形可以画在角色附近（投影后看起来跟 `Privot`/朝向一致），但**绘制位置不是判定输入**。

---

## A. 教学：Godot 里「判定区域」到底用什么

Godot 把「谁碰到谁」做成了好几套工具。它们解决的问题不同。2D 平台、俯视枪、本项目这种伪 3D 巷战，选错工具会把**屏幕叠在一起**误当成**打中了**。

### 节点 / API 对照

| 节点/API | 做什么 | 典型用途 | 为何本项目不用它做真相 |
|----------|--------|----------|------------------------|
| `Area2D` + `CollisionShape2D` | 重叠检测，不产生物理推挤 | 常规 2D Hitbox / Hurtbox、拾取、触发区 | 形状活在**屏幕 XY**。`MapCoordinates` 把深度和高度都投影到屏幕 Y（`DepthToScreenY=0.5`，`HeightToScreenY=1`），两条巷的人、一跳一站的人，精灵都能叠在一起。`area_entered` 会在逻辑上打不中时也触发。 |
| `CharacterBody2D` | 代码移动 + 碰撞响应（`MoveAndSlide`） | 平台角色、会撞墙的 2D 人 | `Actor` 根是 `Node2D`，不是物理体。走位权威在 `TransformComponent` 逻辑坐标，不走 2D 碰撞滑步。 |
| `StaticBody2D` / `RigidBody2D` | 静态挡体 / 刚体积分 | 墙、箱子、用物理飞的子弹 | 近战判定不需要推挤或重力刚体；本轮也没有投射物。 |
| `RayCast2D` | 沿线段探测第一个碰撞体 | 枪、探地、视线 | 近战是一块体积，不是一条线。射线过不了「刀光厚度」。 |
| `ShapeCast2D` | 把形状沿运动扫掠，防隧道穿透 | 高速刀光、瞬移、子弹扫掠 | 本轮近战低速、盒不大、物理帧内角色走不了整盒宽度，逻辑盒逐帧检测足够。扫掠仍在 2D 物理空间，同样吃投影混叠。 |
| `PhysicsDirectSpaceState2D`（`IntersectRay` / `IntersectShape` / `IntersectPoint`） | 按需查询当前物理空间 | 自定义点选、自写射线 | 查询的仍是 **2D 物理世界**，不是 `LogicX/Depth/Z`。把逻辑盒镜像成物理形状等于维护双轨。 |

3D 侧还有 `Area3D`、`PhysicsDirectSpaceState3D`：它们在真正的 X/Y/Z 里工作，和本项目逻辑三轴同构，但引擎要多一套 3D 世界、碰撞层、同步。本轮否决（见文末备选）。

**本项目选用：** 纯 C# 逻辑 AABB + Hurtbox 注册表。Godot 物理节点全部不参与命中。

### 为何屏幕重叠 ≠ 逻辑命中

投影（相对 `MapOrigin`）：

```text
屏幕X = Origin.X + LogicX
屏幕Y = Origin.Y + LogicDepth * 0.5 - VirtualZ * 1.0
```

`LogicDepth` 增加 → 精灵下移；`VirtualZ` 增加 → 精灵上移。二者在屏幕 Y 上对冲。于是：

- 站在更「里」的巷、同时跳起来的人，可以和近处贴地的人**画在同一像素附近**。
- 只看 `Area2D` 或精灵矩形，会判中；看三轴盒子，`LogicDepth` 或 `VirtualZ` 不重叠则**不中**。

这就是 Beat ’em up 的「同一条巷 / 深度门限」：必须站在差不多的纵深上才能挨打。高度同理：对方跳过头顶时，贴地挥击打不中。

### 判定算法分层

任何命中管线都可以拆成四层。本轮每一层都要落地，但不做更重的加速结构。

| 层 | 问题 | 本轮做法 |
|----|------|----------|
| 1. Broadphase | 谁**可能**打到谁 | `HurtboxRegistry` 保存场景里所有 Hurtbox。人数少（个位数到几十），**O(n) 扫全表**，不做网格/四叉树。 |
| 2. Narrowphase | 是否**精确重叠** | 轴对齐 AABB3：X、Depth、Z 三轴都用间隔判断。盒子不旋转（朝左只翻转 X 偏移，尺寸不转）。 |
| 3. 玩法过滤 | 这次算不算一击 | 盒子未激活则不查；自己的 Hurtbox 跳过；同阵营跳过；本 `AttackId` 已命中过的 Hurtbox 跳过。 |
| 4. 结算 | 打中之后干什么 | **只**发 `Hit` 信号并打日志。不改 HP，不推速度，不进入硬直。 |

层 1 可以错（多包含），不能漏；层 2 决定几何；层 3 决定规则；层 4 本轮故意为空操作以外的副作用。

### AABB 重叠公式（全文唯一约定）

统一用 **中心 + 半伸展（center + half extents）**。导出给策划的是**全尺寸** `Size`，构图时再除以 2。不要在有的文件写 min/max、有的文件写中心半径。

逻辑坐标约定（`Vector3` 三轴）：

| 分量 | 逻辑轴 | 说明 |
|------|--------|------|
| `X` | `LogicX` | 巷左右，与屏幕 X 1:1 |
| `Y` | `LogicDepth` | 巷纵深，投影到屏幕 Y 时乘 `0.5` |
| `Z` | `VirtualZ` | 离地高度，投影到屏幕 Y 时乘 `1` 且向上为负 |

盒子：

```text
若 Size 任一轴 <= 0：
  无体积（HalfExtents = (0,0,0)，HasVolume = false）
否则：
  Center      = 角色逻辑位 + 朝向修正后的 Offset
  HalfExtents = Size * 0.5
```

负尺寸不取绝对值充当有效盒。重叠（闭区间，**边贴边算命中**）：

```text
Overlaps(A, B) =
    |A.Center.X - B.Center.X| <= A.HalfExtents.X + B.HalfExtents.X
 && |A.Center.Y - B.Center.Y| <= A.HalfExtents.Y + B.HalfExtents.Y
 && |A.Center.Z - B.Center.Z| <= A.HalfExtents.Z + B.HalfExtents.Z
```

三条必须同时成立。任意一条失败即未命中。无体积的盒子不参与命中（不靠「退化成平面」去刮刀）。

朝左：只把 `Offset.X` 取负，`Offset.Y` / `Offset.Z` 与 `Size` 不变。判定盒始终轴对齐，不随 `Privot.Scale.X = -1` 做矩阵变换。

### 三组对照数字（实现时按此核对）

半伸展已写出，便于心算。

**1. 同巷贴地，应命中**

| | Center `(X, Depth, Z)` | HalfExtents |
|---|------------------------|-------------|
| 攻击盒 | `(48, 0, 36)` | `(36, 14, 36)` |
| 受击盒 | `(80, 0, 36)` | `(18, 12, 36)` |

`|48-80|=32 <= 36+18=54`；深度与高度差为 0。重叠。

**2. 屏幕可能叠上，但深度差开，不应命中**

受击盒改为 Center `(80, 40, 36)`，半伸展不变。  
`|0-40|=40 <= 14+12=26`？`40 > 26`，深度失败。即使精灵在屏幕上接近，也不中。

**3. 对方跳起高度差开，不应命中**

受击盒 Center `(80, 0, 117)`（约等于贴地中心 Z=36 再加跳跃峰值附近的 `VirtualZ≈81`）。  
`|36-117|=81 <= 36+36=72`？`81 > 72`，高度失败。

---

## B. 推荐架构（方案 1，已选）

逻辑空间 Hitbox / Hurtbox 组件 + 静态注册表。攻击生命周期由 `CombatComponent` 拥有；几何查询由 `HitboxComponent` 拥有。不引入 EventBus（命中发生在挥击者自己树上，信号向上/供兄弟监听即可）。

### 场景树

Player：

```text
Player (Node2D, 脚本 Player : Actor)
├── TransformComponent      # Node；逻辑位姿权威
├── MovementComponent       # Node；走位 / 跳跃
├── HurtboxComponent        # Node2D；常开逻辑 AABB；进树注册
├── HitboxComponent         # Node2D；默认关闭；激活后每物理帧查询
├── CombatComponent         # Node；开盒、时长、消费 AttackData、打命中日志
├── PlayerInputComponent    # Node；读 Input Map
├── Shadow
└── Privot
    ├── ForwardArrow
    └── Render
```

Enemy（能挨打，不出招）：

```text
Enemy (Node2D, 脚本 Actor)
├── TransformComponent
├── MovementComponent
├── HurtboxComponent
├── Shadow
└── Privot
    ├── ForwardArrow
    └── Render
```

`HurtboxComponent` / `HitboxComponent` 用 `Node2D` 只为了 `_Draw`。节点自身的 `Position` / `Scale` **不是**盒子数据；保持 `(0,0)` 与 `Scale=(1,1)`，跟随父 `Actor` 即可。

### 节点职责

| 类型 | 本轮职责 | 禁止 |
|------|----------|------|
| `LogicAabb` | 中心+半伸展；`FromCenterSize`；`Overlaps`；朝向翻转偏移；投影成 Actor 本地 `Rect2` | 持有 Node；读 Input；当物理形状用 |
| `HurtboxRegistry` | 静态表：进树 `Register`，离树 `Unregister`，`Snapshot()` 拷贝列表 | Autoload；做重叠运算；持有攻击状态 |
| `HurtboxComponent` | 常开受击盒；跟随 Transform；Facing 翻 X；注册表；调试绘制 | 扣血；自挂 `_PhysicsProcess` 做判定；把 `Privot.Scale` 当朝向 |
| `HitboxComponent` | `Activate` / `Deactivate`；每 tick 对注册表做 AABB；本挥击去重；发 `Hit` | 读 Input；改 HP；用 Area2D 信号当命中；自挂 `_PhysicsProcess` |
| `CombatComponent` | 持有 `AttackData`；`TryStartAttack` 分配 `AttackId` 并开盒；扣剩余时长；到期关盒；监听 `Hit` 打日志 | 自己算 AABB；读 Input Map 字符串；无 Hitbox 时抛未处理异常 |
| `AttackData` | 只读配置：时长、Hitbox 偏移与全尺寸 | 运行时命中列表；Node 引用 |
| `CombatTeam` | `Player` / `Enemy` 二值枚举 | 映射到 32 个物理层 |
| `InputActions` | 新增 `attack` 常量与 `IsAttackJustPressed()` | 在其它文件硬编码 `"attack"` |
| `PlayerInputComponent` | 在既有移动/跳跃之后，若刚按下攻击则 `Combat.TryStartAttack()` | 直接 `Hitbox.Activate`；改 Transform |
| `Actor` | `_Ready` 缓存组件；`_PhysicsProcess` 按顺序 tick | 解释攻击规则；在 Actor 里写 AABB |
| `Player` | 保持「先 Input 再 `base._PhysicsProcess`」 | 本轮改这个先后关系 |
| `TransformComponent` | 继续提供 Get/Set 逻辑坐标与 Facing | 知道 Hitbox 存在 |
| `ActorDefinition` | 不变 | 本轮加 Health / Attack 字段 |

### 信号与数据流

```text
按键 attack
  → PlayerInputComponent.PhysicsTick
      → CombatComponent.TryStartAttack()
          若已在挥击中：忽略
          否则：AttackId++，Hitbox.Activate(id, offset, size)，Remaining = ActiveDuration

同帧稍后（见调度）：
  Movement 先把 LogicX/Depth/Z 更新完
  Hitbox.PhysicsTick
      若未激活：return
      取自身逻辑 AABB（含 Facing）
      遍历 HurtboxRegistry.Snapshot()
          无效节点 / 无体积 / 取盒失败 → 跳过
          同一 Actor（自己打自己） → 跳过
          Team 相同 → 跳过
          三轴不重叠 → 跳过
          已在本 AttackId 命中集合中 → 跳过
          否则加入集合，Emit Hit(hurtbox)
  Combat.PhysicsTick
      Remaining -= delta
      若 Remaining <= 0：Deactivate，Remaining = 0

Hitbox.Hit(hurtbox)
  → CombatComponent 打印：
     CombatComponent: hit {目标Actor名} attackId={id}
```

`Hit` 的 payload 是 `HurtboxComponent`（Godot 可封送的 `GodotObject` 子类）。监听方用 `hurtbox.GetParent()` 拿到 `Actor`。本轮不另做 `HitEventData` Resource。

同一挥击同一 Hurtbox：`HashSet<HurtboxComponent>` 在 `Activate` 时清空；`Add` 失败即重复，不再发信号。关盒不要求清集合（下次 `Activate` 会清）。新的一次按键产生新的 `AttackId` 和新集合，可以对同一敌人再打一次。

挥击中再按攻击：**忽略**，不刷新时长、不更换 `AttackId`、不清命中集合。

### 阵营

```text
enum CombatTeam { Player, Enemy }
```

`HurtboxComponent` 与 `HitboxComponent` 各有 `[Export] CombatTeam Team`。

| 预制体 | Hurtbox.Team | Hitbox.Team |
|--------|--------------|-------------|
| Player | `Player` | `Player` |
| Enemy | `Enemy` | （无 Hitbox） |

过滤：`hitbox.Team == hurtbox.Team` 则跳过。因此 Player 打 Enemy；不会打到另一个 Player（本轮也没有第二个 Player）。自己打自己在阵营过滤之前用 `Actor` 引用再挡一层。

不要配置 `collision_layer` / `mask` 来表达阵营。

### 盒子数据

Hurtbox（组件 Export，常开）：

| 字段 | 类型 | Player/Enemy 默认 | 含义 |
|------|------|-------------------|------|
| `Offset` | `Vector3` | `(0, 0, 36)` | 相对角色逻辑位的中心偏移 `(X, Depth, Z)` |
| `Size` | `Vector3` | `(36, 24, 72)` | 全尺寸；半伸展为 `(18, 12, 36)` |
| `Team` | `CombatTeam` | 见上表 | 阵营 |
| `DebugDrawEnabled` | `bool` | `true` | 是否画受击盒 |

贴地时 Z 覆盖约 `[0, 72]`，与跳跃峰值（`BaseJumpForce=400`、`BaseGravity=980` 时约 `v²/2g ≈ 81`）错开，便于验收「跳起打不中」。

Hitbox 几何**不**在组件上长期 Export，而由本次 `Activate` 从 `AttackData` 写入运行时偏移/尺寸。未激活时无体积、不查询、不画（除非将来做预览；本轮不做）。

世界（逻辑）中心：

```text
signedOffset = Facing==Left ? (-Offset.X, Offset.Y, Offset.Z) : Offset
Center.X = LogicX + signedOffset.X
Center.Y = LogicDepth + signedOffset.Y
Center.Z = VirtualZ + signedOffset.Z
```

Hurtbox / 激活中的 Hitbox 都用当前帧的 `GetLogicX` / `GetLogicDepth` / `GetVirtualZ` / `GetFacing()`，因此跳跃与转身当帧生效。

### 攻击数据

`AttackData`（`[GlobalClass] Resource`），本轮一份近战：

| 字段 | 类型 | 默认（代码与 `.tres`） | 含义 |
|------|------|------------------------|------|
| `ActiveDuration` | `float` | `0.2` | 开盒秒数（物理时间） |
| `HitboxOffset` | `Vector3` | `(48, 0, 36)` | 朝右时的中心偏移 |
| `HitboxSize` | `Vector3` | `(72, 28, 72)` | 全尺寸；半伸展 `(36, 14, 36)` |

资源路径：`data/actors/attacks/player_melee_default.tres`。  
`CombatComponent` `[Export] AttackData Attack`，Player 预制体绑这份资源。

不做：多段攻击表、伤害数字、命中暂停、动画轨道。

### 调度

组件**禁止** override `_PhysicsProcess` 做判定或开盒。调试用 `QueueRedraw` 由 `Actor` 在同一物理帧末尾调用，或由 `Hitbox.PhysicsTick` / `Deactivate` 顺带调用；这不是玩法 tick。

```text
Player._PhysicsProcess
  → PlayerInputComponent.PhysicsTick
        SetMoveInput / Jump / TryStartAttack   // 可能已 Activate
  → Actor._PhysicsProcess
        MovementComponent.PhysicsTick          // 先走位，再判定
        HitboxComponent.PhysicsTick            // 开盒本帧必查
        CombatComponent.PhysicsTick            // 后扣时长，到期才 Deactivate
        HurtboxComponent.RedrawDebug()
```

**Hitbox 必须排在 Combat 扣时之前。** 否则一帧短于 `ActiveDuration` 的攻击会在查询前被关掉，整段挥击零命中。本轮 `0.2s` 远大于 `1/60s`，但仍按这个顺序写死，避免以后把时长调短时踩坑。

`Actor` 对 Combat / Hitbox 用 `GetNodeOrNull`：Enemy 没有这两个节点是合法的，缺则跳过 tick，不报错。Hurtbox 本轮 Player 与 Enemy 都必须有，缺则 `PushError`。

### 调试绘制

把逻辑 AABB 投影成 **Actor 本地空间** 的轴对齐矩形（该投影下 3D 盒的屏幕外接矩形仍是 AABB，因为 Depth 与 Z 都只进 Y）：

```text
localMinX = (Center.X - HalfX) - actorLogicX
localMaxX = (Center.X + HalfX) - actorLogicX
localMinY = (Center.Y - HalfY - actorLogicDepth) * DepthToScreenY
            - (Center.Z + HalfZ) * HeightToScreenY
localMaxY = (Center.Y + HalfY - actorLogicDepth) * DepthToScreenY
            - (Center.Z - HalfZ) * HeightToScreenY
```

`DepthToScreenY` / `HeightToScreenY` 只引用 `MapCoordinates`，禁止再写一份常量。

绘制：半透明填充 + 描边。Hurtbox 绿色；激活中的 Hitbox 红色。`ZIndex = 100`，盖在精灵之上以便验收。

`MapContext` 无 Origin 时：**跳过绘制**（与 Transform 无 Origin 则不写世界坐标一致）。判定仍可在纯逻辑坐标进行，不依赖 Origin。

不要开 `Area2D.Monitoring`，不要用碰撞形状可见性当调试。

### API 表面

#### `LogicAabb`（`readonly struct`）

| 成员 | 含义 |
|------|------|
| `Vector3 Center` / `Vector3 HalfExtents` | 中心与半伸展 |
| `static LogicAabb FromCenterSize(Vector3 center, Vector3 size)` | 全尺寸 → 盒；任一轴 `Size <= 0` 则半伸展为 0 |
| `bool Overlaps(in LogicAabb other)` | 三轴闭区间重叠 |
| `bool HasVolume` | 三轴半伸展均 `> 0` |
| `static Vector3 ApplyFacingOffset(Vector3 offset, ActorFacing facing)` | 朝左则 `X` 取负 |
| `Rect2 ToActorLocalRect(float actorLogicX, float actorLogicDepth)` | 调试用外接矩形 |

#### `HurtboxRegistry`

| API | 含义 |
|-----|------|
| `void Register(HurtboxComponent hurtbox)` | `null` 则 `PushError` 并 return；重复注册安全 |
| `void Unregister(HurtboxComponent hurtbox)` | `null` 则忽略 |
| `List<HurtboxComponent> Snapshot()` | 拷贝当前表，供本帧遍历 |

#### `HurtboxComponent`

| API | 含义 |
|-----|------|
| `bool TryGetWorldAabb(out LogicAabb aabb)` | 缺 Transform 或无体积则 `false` |
| `Actor GetOwnerActor()` | 父节点；不是 Actor 则为 `null` |
| `void RedrawDebug()` | 允许绘制时 `QueueRedraw` |
| `_EnterTree` / `_ExitTree` | 注册 / 注销 |

#### `HitboxComponent`

| API | 含义 |
|-----|------|
| `void Activate(int attackId, Vector3 offset, Vector3 size)` | 开盒、记下几何、清空命中集合 |
| `void Deactivate()` | 关盒并请求重绘 |
| `void PhysicsTick(double delta)` | 仅当激活：查询并发信号 |
| `int CurrentAttackId { get; }` | 本次挥击 id |
| `bool IsActive { get; }` | 是否开盒 |
| `[Signal] Hit(HurtboxComponent hurtbox)` | 首次命中该 Hurtbox |

`delta` 本轮查询不用（几何瞬时），保留签名以便与其它 `PhysicsTick` 一致。

#### `CombatComponent`

| API | 含义 |
|-----|------|
| `[Export] AttackData Attack` | 本轮唯一近战配置 |
| `void TryStartAttack()` | 见数据流 |
| `void PhysicsTick(double delta)` | 扣时长，到期 `Deactivate` |
| `bool IsAttacking` | `Remaining > 0` |

### 错误处理

| 情况 | 行为 |
|------|------|
| Hurtbox / Hitbox 缺 `TransformComponent` | `_Ready` `PushError`（含路径）；`TryGetWorldAabb` 失败；Hitbox 本帧不发命中 |
| Hurtbox 父节点不是 `Actor` | `_Ready` `PushError`；`GetOwnerActor()` 为 `null`；Hitbox 将其视为非法目标跳过 |
| Actor 缺 Hurtbox | `Actor._Ready` `PushError`；无法挨打 |
| Player 缺 Combat | `PlayerInputComponent` `PushError`；按攻击无效果 |
| Combat 缺 Hitbox 或 `Attack == null` | `_Ready` `PushError`；`TryStartAttack` / `PhysicsTick` 直接 return |
| 自己打到自己 | 静默跳过 |
| 同阵营 | 静默跳过 |
| 挥击中再按攻击 | 静默忽略 |
| 空中按攻击 | **允许**开盒（用于验收高度差）；不禁止跳跃攻击 |
| 无 `MapContext` Origin | 判定照常；**调试绘制跳过** |
| Hurtbox 已释放但仍短暂留在快照 | `IsInstanceValid` 失败则跳过 |
| `Size` 某轴 `<= 0` | `HasVolume == false`，不命中、不绘制该盒 |

缺依赖时 `GD.PushError` 并跳过，不抛未处理异常（与 Transform / Movement 一致）。

### 文件清单

#### 新增

| 文件 | 职责 |
|------|------|
| `scripts/LogicAabb.cs` | 逻辑 AABB 数学 |
| `scripts/CombatTeam.cs` | 阵营枚举 |
| `scripts/HurtboxRegistry.cs` | 静态注册表 |
| `scripts/HurtboxComponent.cs` | 受击盒 |
| `scripts/HitboxComponent.cs` | 攻击盒 |
| `scripts/CombatComponent.cs` | 挥击生命周期 |
| `scripts/data/AttackData.cs` | 近战配置 Resource |
| `data/actors/attacks/player_melee_default.tres` | Player 默认近战 |

#### 修改

| 文件 | 改动 |
|------|------|
| `scripts/Actor.cs` | 缓存 Hurtbox（必有）与 Combat/Hitbox（可选）；调整 `_PhysicsProcess` 顺序 |
| `scripts/PlayerInputComponent.cs` | 调用 `TryStartAttack` |
| `scripts/InputActions.cs` | `Attack` 常量与 `IsAttackJustPressed` |
| `project.godot` | Input Map 增加 `attack`（默认物理键 **J**） |
| `prefabs/Player.tscn` | 挂 Hurtbox / Hitbox / Combat，绑定 `AttackData` |
| `prefabs/Enemy.tscn` | 挂 Hurtbox，`Team = Enemy` |

#### 不改（本轮）

| 文件 | 说明 |
|------|------|
| `scripts/TransformComponent.cs` | 已有 Get/Set 与 Facing |
| `scripts/MovementComponent.cs` | 攻击不打断移动 |
| `scripts/Player.cs` | 仍是 Input → `base` |
| `scripts/data/ActorDefinition.cs` | 不加战斗字段 |
| `scripts/MapCoordinates.cs` | 只被调试投影引用 |

---

## C. 曾考虑的备选

### 备选 2：`Area2D` 粗检 + 深度/高度过滤

编辑器里能拖 `CollisionShape2D`，和常见 2D Hitbox 教程一致。流程会是：`area_entered` 先用屏幕形状筛一轮，再比较 `LogicDepth` / `VirtualZ`。

否决原因：

- **双轨**：屏幕形状与逻辑盒必须同步；改偏移时容易只改了一边。
- **漏判 / 误判**：屏幕不重叠但逻辑重叠（例如深度差被高度投影抵消后分开，或反过来）时，粗检会漏掉或多报，过滤规则难写对。
- 本项目已经证明「屏幕 Y 混叠」，粗检建立在不可靠空间上。

### 备选 3：隐藏 `Area3D` 镜像逻辑空间

把 `LogicX/Depth/Z` 写成 `Area3D` 的 `Position`，用 3D 物理做重叠，语义正确。

否决原因：

- 要维护平行的 3D 世界、层、原点，和 2D 关卡脱节。
- 对近战低速、人数少的判定过重；调试还要在 3D 视口里看盒。
- 与现有「全是 Node2D Actor」的场景组织冲突。

方案 1 用几十行 AABB 和一张静态表就能验收，后续若人数上千再考虑空间哈希，不必先上物理引擎。

---

## 范围外

- `HealthComponent`、扣血、死亡、HP UI
- 击退、硬直、顿帧、受击闪白
- 连段、取消、攻击状态机
- 投射物、多 Hitbox 帧、AnimationPlayer 命中帧
- 可行走区钳制
- Enemy AI 出招（Enemy 只要能挨打）
- 输入缓冲、手柄绑定、重绑定 UI
- 用物理层代替 `CombatTeam`
- 自动化测试 / gdUnit / CI
- 将 `Privot` 更正拼写为 `Pivot`
- 把战斗字段写入 `ActorDefinition`

## 完成标准

1. Player 按 **J**（Input Map `attack`）：前方逻辑攻击盒出现（红色调试矩形），约 `0.2s` 后消失。
2. 与 Enemy **三轴重叠**时，输出一次 `CombatComponent: hit ... attackId=...`（或等价的 `Hit` 信号被 Combat 接到）；同一挥击内走近/离开再走进，不重复。
3. 挥击结束后再按攻击，可以再次命中同一 Enemy（新的 `AttackId`）。
4. 仅深度拉开（明显另一条巷）时不中；仅高度拉开（Enemy 贴地、Player 在跳跃接近顶点时挥击，或反过来）时不中。
5. 绿色 Hurtbox、红色激活 Hitbox 在开发运行中可见；无 Origin 时不画盒且不崩。
6. `dotnet build ProjectC.csproj` 成功；不要求自动化测试。
