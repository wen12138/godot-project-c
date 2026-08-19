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

	public virtual float GetLogicX()
	{
		return m_LogicX;
	}

	public virtual float GetLogicDepth()
	{
		return m_LogicDepth;
	}

	public virtual float GetVirtualZ()
	{
		return m_VirtualZ;
	}
}
