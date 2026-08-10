# 地图原点与逻辑坐标变换实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 落地 `MapCoordinates` / `MapContext` / `MapOrigin`，并让 `TransformComponent` 从世界 `Position` 反算逻辑坐标；根节点贴地，`Privot` 承载 `VirtualZ`。

**Architecture:** 关卡用 `Marker2D`（`MapOrigin`）自注册到 Autoload `MapContext`。静态类 `MapCoordinates` 提供唯一投影常量与双向换算。`TransformComponent` 进场 `CallDeferred(InitializeFromWorldPose)`；之后只改逻辑再写回 `Actor.GlobalPosition`（忽略 Z）与 `Privot.Position`（仅 Z 偏移）。本轮不实现 Spawner / 移动物理 / 自动测试。

**Tech Stack:** Godot 4.6、C# / .NET、Autoload、`Marker2D`、Node2D 场景

**Spec:** `docs/superpowers/specs/2026-08-10-map-origin-logic-transform-design.md`

## Global Constraints

- 文档与计划使用中文；代码标识符保持英文
- 全局命名空间（与现有 `scripts/` 一致）
- 节点名保持 `Privot`（不改名为 `Pivot`）
- 进场以世界 `GlobalPosition` 为入口；可选 `PendingInitialVirtualZ`（默认 `0` 贴地）
- 进场之后逻辑坐标为权威；`MovementComponent` 不直接改 `Position`
- 不做可行走区域钳制、HurtBox、Spawner、存档逻辑坐标、移动物理
- 不做自动化测试；以 `dotnet build ProjectC.csproj` 成功为完成标准
- 无 Origin / 父非 Actor / 缺 `Privot` 时 `GD.PushError`，不静默回退

## 文件结构

| 文件 | 职责 |
|------|------|
| `scripts/MapCoordinates.cs` | 投影常量 + `LogicToWorld` / `WorldToLogicGround` / `VirtualZScreenOffset` |
| `scripts/MapContext.cs` | Autoload：注册/清理/暴露当前原点 |
| `scripts/MapOrigin.cs` | 原点节点进出树自注册 |
| `project.godot` | 注册 Autoload `MapContext` |
| `main.tscn` | `Map/MapOrigin` |
| `prefabs/Player.tscn` / `Enemy.tscn` | `RenderPrivot`→`Privot`；`Shadow` 上移到 Actor 直下 |
| `scripts/TransformComponent.cs` | 反算初始化；根贴地；`Privot` 吃 VirtualZ |

---

### Task 1: `MapCoordinates` 静态换算

**Files:**
- Create: `scripts/MapCoordinates.cs`

**Interfaces:**
- Produces:
  - `public const float DepthToScreenY = 0.5f`
  - `public const float HeightToScreenY = 1.0f`
  - `public static Vector2 LogicToWorld(Vector2 origin, float logicX, float logicDepth, float virtualZ = 0f)`
  - `public static void WorldToLogicGround(Vector2 origin, Vector2 world, out float logicX, out float logicDepth)`
  - `public static Vector2 VirtualZScreenOffset(float virtualZ)`

- [ ] **Step 1: 创建 `scripts/MapCoordinates.cs`**

```csharp
using Godot;

public static class MapCoordinates
{
	public const float DepthToScreenY = 0.5f;
	public const float HeightToScreenY = 1.0f;

	public static Vector2 LogicToWorld(Vector2 origin, float logicX, float logicDepth, float virtualZ = 0f)
	{
		return origin + new Vector2(
			logicX,
			logicDepth * DepthToScreenY - virtualZ * HeightToScreenY);
	}

	public static void WorldToLogicGround(Vector2 origin, Vector2 world, out float logicX, out float logicDepth)
	{
		var local = world - origin;
		logicX = local.X;
		logicDepth = local.Y / DepthToScreenY;
	}

	public static Vector2 VirtualZScreenOffset(float virtualZ)
	{
		return new Vector2(0f, -virtualZ * HeightToScreenY);
	}
}
```

- [ ] **Step 2: 编译确认**

Run: `dotnet build ProjectC.csproj`  
Expected: 成功（0 Error）

- [ ] **Step 3: Commit**

```bash
git add scripts/MapCoordinates.cs
git commit -m "添加 MapCoordinates 逻辑与世界坐标换算。"
```

---

### Task 2: `MapContext` Autoload

**Files:**
- Create: `scripts/MapContext.cs`
- Modify: `project.godot`（`[autoload]` 增加一行）

**Interfaces:**
- Consumes: 无
- Produces:
  - Autoload 节点名 / 类型：`MapContext`
  - `public static MapContext Instance { get; private set; }`
  - `public bool HasOrigin { get; }`
  - `public Node2D Origin { get; }`（未注册时 `PushError`，返回 `null`）
  - `public void RegisterOrigin(Node2D origin)`
  - `public void ClearOrigin()`

- [ ] **Step 1: 创建 `scripts/MapContext.cs`**

```csharp
using Godot;

public partial class MapContext : Node
{
	public static MapContext Instance { get; private set; }

	private Node2D m_Origin;

	public bool HasOrigin => m_Origin != null && GodotObject.IsInstanceValid(m_Origin);

	public Node2D Origin
	{
		get
		{
			if (!HasOrigin)
			{
				GD.PushError("MapContext: Origin 尚未注册");
				return null;
			}

			return m_Origin;
		}
	}

	public override void _EnterTree()
	{
		Instance = this;
	}

	public override void _ExitTree()
	{
		if (Instance == this)
		{
			Instance = null;
		}
	}

	public void RegisterOrigin(Node2D origin)
	{
		if (origin == null)
		{
			GD.PushError("MapContext.RegisterOrigin: origin 为 null");
			return;
		}

		m_Origin = origin;
	}

	public void ClearOrigin()
	{
		m_Origin = null;
	}
}
```

- [ ] **Step 2: 在 `project.godot` 的 `[autoload]` 中注册**

在现有 `SaveService=...` 下一行增加：

```ini
MapContext="*res://scripts/MapContext.cs"
```

完整 `[autoload]` 段应类似：

```ini
[autoload]

McpInteractionServer="*res://addons/godot_mcp/mcp_interaction_server.gd"
SaveService="*res://scripts/data/SaveService.cs"
MapContext="*res://scripts/MapContext.cs"
```

- [ ] **Step 3: 编译确认**

Run: `dotnet build ProjectC.csproj`  
Expected: 成功（0 Error）

- [ ] **Step 4: Commit**

```bash
git add scripts/MapContext.cs project.godot
git commit -m "添加 MapContext Autoload 并注册到项目。"
```

---

### Task 3: `MapOrigin` 节点与 `main.tscn`

**Files:**
- Create: `scripts/MapOrigin.cs`
- Modify: `main.tscn`

**Interfaces:**
- Consumes: `MapContext.Instance.RegisterOrigin` / `ClearOrigin`；仅当当前注册仍是自身时清理
- Produces: 场景路径 `Map/MapOrigin`（`Marker2D`）

- [ ] **Step 1: 创建 `scripts/MapOrigin.cs`**

```csharp
using Godot;

public partial class MapOrigin : Marker2D
{
	public override void _EnterTree()
	{
		if (MapContext.Instance == null)
		{
			GD.PushError($"{GetPath()}: MapContext.Instance 为空，无法注册原点");
			return;
		}

		MapContext.Instance.RegisterOrigin(this);
	}

	public override void _ExitTree()
	{
		if (MapContext.Instance == null)
		{
			return;
		}

		if (MapContext.Instance.HasOrigin && MapContext.Instance.Origin == this)
		{
			MapContext.Instance.ClearOrigin();
		}
	}
}
```

- [ ] **Step 2: 修改 `main.tscn`——在 `Map` 下增加 `MapOrigin`**

在 `[ext_resource]` 区增加脚本引用（id 按文件内未占用编号选取，例如 `5_mapori`）：

```ini
[ext_resource type="Script" path="res://scripts/MapOrigin.cs" id="5_mapori"]
```

在 `[node name="Map" ...]` 之后、`Background` 之前插入：

```ini
[node name="MapOrigin" type="Marker2D" parent="Map"]
position = Vector2(0, 0)
script = ExtResource("5_mapori")
```

说明：`position = (0, 0)` 表示逻辑原点落在世界原点；日后只需移动该 Marker 即可平移整关逻辑空间。若编辑器生成了 `.uid`，一并提交。

- [ ] **Step 3: 编译确认**

Run: `dotnet build ProjectC.csproj`  
Expected: 成功（0 Error）

- [ ] **Step 4: Commit**

```bash
git add scripts/MapOrigin.cs scripts/MapOrigin.cs.uid main.tscn
git commit -m "关卡增加 MapOrigin 并挂接自注册脚本。"
```

（若无 `.uid` 文件则从 `git add` 中省略。）

---

### Task 4: Player / Enemy 场景层级调整

**Files:**
- Modify: `prefabs/Player.tscn`
- Modify: `prefabs/Enemy.tscn`

**Interfaces:**
- Produces: 两预制体均符合

```text
Actor
├── …Components…
├── Shadow          (Actor 直下)
└── Privot
    ├── ForwardArrow
    └── Render
```

- [ ] **Step 1: 编辑 `prefabs/Player.tscn`**

将原：

```ini
[node name="RenderPrivot" type="Node2D" parent="." unique_id=2008766164]

[node name="Shadow" type="Sprite2D" parent="RenderPrivot" unique_id=1583956380]
texture = ExtResource("4_1nynx")

[node name="ForwardArrow" type="Sprite2D" parent="RenderPrivot" unique_id=482277145]
position = Vector2(21, 0)
texture = ExtResource("6_64pgi")

[node name="Render" type="Sprite2D" parent="RenderPrivot" unique_id=2131274872]
position = Vector2(0, -75)
texture = ExtResource("2_wuy1y")
```

改为（保持各 `ExtResource` / `unique_id` 与纹理引用不变；仅改父路径与节点名）：

```ini
[node name="Shadow" type="Sprite2D" parent="." unique_id=1583956380]
texture = ExtResource("4_1nynx")

[node name="Privot" type="Node2D" parent="." unique_id=2008766164]

[node name="ForwardArrow" type="Sprite2D" parent="Privot" unique_id=482277145]
position = Vector2(21, 0)
texture = ExtResource("6_64pgi")

[node name="Render" type="Sprite2D" parent="Privot" unique_id=2131274872]
position = Vector2(0, -75)
texture = ExtResource("2_wuy1y")
```

- [ ] **Step 2: 编辑 `prefabs/Enemy.tscn`**

将原 `RenderPrivot` 块改为同样结构（Enemy 的 `Render.position` 保持 `Vector2(0, -57.5)`，纹理 id 保持 Enemy 文件内现有 ExtResource）：

```ini
[node name="Shadow" type="Sprite2D" parent="." unique_id=113315737]
texture = ExtResource("4_hgomy")

[node name="Privot" type="Node2D" parent="." unique_id=1795713165]

[node name="ForwardArrow" type="Sprite2D" parent="Privot" unique_id=1892403766]
position = Vector2(21, 0)
texture = ExtResource("6_oovrp")

[node name="Render" type="Sprite2D" parent="Privot" unique_id=2131274872]
position = Vector2(0, -57.5)
texture = ExtResource("2_duu5m")
```

- [ ] **Step 3: 快速核对**

确认两文件中均不再出现 `RenderPrivot` 字符串；`Shadow` 的 `parent` 为 `"."`。

- [ ] **Step 4: Commit**

```bash
git add prefabs/Player.tscn prefabs/Enemy.tscn
git commit -m "Actor 预制体：RenderPrivot 更名为 Privot，Shadow 贴地。"
```

---

### Task 5: `TransformComponent` 接线

**Files:**
- Modify: `scripts/TransformComponent.cs`

**Interfaces:**
- Consumes:
  - `MapCoordinates.*`
  - `MapContext.Instance` / `HasOrigin` / `Origin.GlobalPosition`
  - 父 `Actor`；子节点路径 `"Privot"`（`Node2D`）
- Produces:
  - `public float PendingInitialVirtualZ { get; set; }`（生成空中刷出时在进场前赋值；默认 `0`）
  - `public void InitializeFromWorldPose(float initialVirtualZ = 0f)`
  - 既有 `SetLogicX` / `SetLogicDepth` / `SetVirtualZ`（内部改调新 `UpdateVisualPosition`）
  - `UpdateVisualPosition`：根 `GlobalPosition = LogicToWorld(..., virtualZ: 0)`；`Privot.Position = VirtualZScreenOffset(VirtualZ)`

- [ ] **Step 1: 重写 `scripts/TransformComponent.cs` 为如下实现**

```csharp
using Godot;

public partial class TransformComponent : Node
{
	protected float m_VisualX;
	protected float m_VisualY;
	protected float m_LogicX;
	protected float m_LogicDepth;
	protected float m_VirtualZ;

	private Actor m_Actor;
	private Node2D m_Privot;
	private bool m_HasPrivot;

	/// <summary>
	/// 进场前可设：空中刷出的初始高度。默认 0（贴地）。
	/// deferred InitializeFromWorldPose 会读取并清零。
	/// </summary>
	public float PendingInitialVirtualZ { get; set; }

	public override void _Ready()
	{
		m_Actor = GetParentOrNull<Actor>();
		if (m_Actor == null)
		{
			GD.PushError($"{GetPath()}: parent is not Actor");
			return;
		}

		m_Privot = m_Actor.GetNodeOrNull<Node2D>("Privot");
		m_HasPrivot = m_Privot != null;
		if (!m_HasPrivot)
		{
			GD.PushError($"{GetPath()}: missing sibling/child path Actor/Privot");
		}

		CallDeferred(MethodName.InitializeFromWorldPoseDeferred);
	}

	private void InitializeFromWorldPoseDeferred()
	{
		var z = PendingInitialVirtualZ;
		PendingInitialVirtualZ = 0f;
		InitializeFromWorldPose(z);
	}

	public void InitializeFromWorldPose(float initialVirtualZ = 0f)
	{
		if (m_Actor == null)
		{
			return;
		}

		if (MapContext.Instance == null || !MapContext.Instance.HasOrigin)
		{
			GD.PushError($"{GetPath()}: MapContext Origin 未注册，无法从世界位置初始化逻辑坐标");
			return;
		}

		var origin = MapContext.Instance.Origin.GlobalPosition;
		MapCoordinates.WorldToLogicGround(origin, m_Actor.GlobalPosition, out m_LogicX, out m_LogicDepth);
		m_VirtualZ = initialVirtualZ;
		UpdateVisualPosition();
	}

	public override void _PhysicsProcess(double delta)
	{
	}

	public virtual void SetLogicDepth(float depth)
	{
		m_LogicDepth = depth;
		UpdateVisualPosition();
	}

	public virtual void SetLogicX(float x)
	{
		m_LogicX = x;
		UpdateVisualPosition();
	}

	public virtual void SetVirtualZ(float height)
	{
		m_VirtualZ = height;
		UpdateVisualPosition();
	}

	protected virtual void UpdateVisualPosition()
	{
		if (m_Actor == null)
		{
			return;
		}

		if (MapContext.Instance == null || !MapContext.Instance.HasOrigin)
		{
			GD.PushError($"{GetPath()}: MapContext Origin 未注册，跳过写回位置");
			return;
		}

		var origin = MapContext.Instance.Origin.GlobalPosition;
		var ground = MapCoordinates.LogicToWorld(origin, m_LogicX, m_LogicDepth, virtualZ: 0f);
		m_Actor.GlobalPosition = ground;
		m_VisualX = ground.X;
		m_VisualY = ground.Y;

		if (m_HasPrivot)
		{
			m_Privot.Position = MapCoordinates.VirtualZScreenOffset(m_VirtualZ);
		}
	}

	public virtual float GetVisualX()
	{
		return m_VisualX;
	}

	public virtual float GetVisualY()
	{
		return m_VisualY;
	}

	public virtual float GetLogicDepth()
	{
		return m_LogicDepth;
	}
}
```

说明：

- `Privot` 是 `Actor` 的子节点，故用 `m_Actor.GetNodeOrNull<Node2D>("Privot")`（不要用 `GetNode("../Privot")`）。
- `GetVisualX/Y` 表示**根（贴地）**世界坐标分量，不再把 `VirtualZ` 折进 `m_VisualY`。
- 删除本文件内的 `DepthToScreenY` / `HeightToScreenY` 常量。

- [ ] **Step 2: 编译确认**

Run: `dotnet build ProjectC.csproj`  
Expected: 成功（0 Error）

- [ ] **Step 3: 手动冒烟（可选但推荐）**

1. 用 Godot 打开并运行主场景  
2. 确认无 `MapContext Origin 未注册` / `missing ... Privot` 错误  
3. Player 仍出现在约 `(640, 440)`；Shadow 在脚下；`Privot` 本地约为 `(0, 0)`  
4.（可选）临时在调试里对 `TransformComponent.SetVirtualZ(40)`：角色精灵上移，Shadow / 根不跟着离地

- [ ] **Step 4: Commit**

```bash
git add scripts/TransformComponent.cs
git commit -m "TransformComponent：世界进场反算逻辑，根贴地且 Privot 承载 VirtualZ。"
```

---

## 规格覆盖自检

| Spec 要求 | 任务 |
|-----------|------|
| `MapCoordinates` 常量与三 API | Task 1 |
| `MapContext` Autoload + Register/Clear/HasOrigin/Origin | Task 2 |
| `MapOrigin` 自注册/自清理 + `main.tscn` | Task 3 |
| `RenderPrivot`→`Privot`，Shadow 贴地 | Task 4 |
| `InitializeFromWorldPose` + deferred + PendingInitialVirtualZ | Task 5 |
| 根忽略 VirtualZ，`Privot` 吃偏移 | Task 5 |
| 常量仅一处 | Task 1 创建 + Task 5 删除旧常量 |
| 不实现 Spawner / 钳制 / HurtBox / 物理 | 全任务未包含 |

## 执行交接

计划完成后，可选执行方式见下（由用户选择）。
