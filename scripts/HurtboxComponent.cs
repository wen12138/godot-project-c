using Godot;

public partial class HurtboxComponent : Node2D
{
	[Export]
	public CombatTeam Team { get; set; } = CombatTeam.Player;

	[Export]
	public Vector3 Offset { get; set; } = new(0f, 0f, 36f);

	[Export]
	public Vector3 Size { get; set; } = new(36f, 24f, 72f);

	[Export]
	public bool DebugDrawEnabled { get; set; } = true;

	private TransformComponent m_Transform;
	private Actor m_OwnerActor;

	public override void _EnterTree()
	{
		HurtboxRegistry.Register(this);
	}

	public override void _ExitTree()
	{
		HurtboxRegistry.Unregister(this);
	}

	public override void _Ready()
	{
		ZIndex = 100;
		m_OwnerActor = GetParentOrNull<Actor>();
		if (m_OwnerActor == null)
		{
			GD.PushError($"{GetPath()}: parent is not Actor");
		}

		m_Transform = GetNodeOrNull<TransformComponent>("../TransformComponent");
		if (m_Transform == null)
		{
			GD.PushError($"{GetPath()}: missing sibling TransformComponent at ../TransformComponent");
		}
	}

	public Actor GetOwnerActor()
	{
		return m_OwnerActor;
	}

	public bool TryGetWorldAabb(out LogicAabb aabb)
	{
		aabb = default;
		if (m_Transform == null)
		{
			return false;
		}

		var signed = LogicAabb.ApplyFacingOffset(Offset, m_Transform.GetFacing());
		var center = new Vector3(
			m_Transform.GetLogicX() + signed.X,
			m_Transform.GetLogicDepth() + signed.Y,
			m_Transform.GetVirtualZ() + signed.Z);
		aabb = LogicAabb.FromCenterSize(center, Size);
		return aabb.HasVolume;
	}

	public void RedrawDebug()
	{
		if (DebugDrawEnabled)
		{
			QueueRedraw();
		}
	}

	public override void _Draw()
	{
		if (!DebugDrawEnabled)
		{
			return;
		}

		if (MapContext.Instance == null || !MapContext.Instance.HasOrigin)
		{
			return;
		}

		if (m_Transform == null || !TryGetWorldAabb(out var aabb))
		{
			return;
		}

		var rect = aabb.ToActorLocalRect(m_Transform.GetLogicX(), m_Transform.GetLogicDepth());
		DrawRect(rect, new Color(0.2f, 0.85f, 0.35f, 0.15f), filled: true);
		DrawRect(rect, new Color(0.2f, 0.85f, 0.35f, 0.9f), filled: false, width: 2f);
	}
}
