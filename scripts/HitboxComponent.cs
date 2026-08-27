using System.Collections.Generic;
using Godot;

public partial class HitboxComponent : Node2D
{
	[Export]
	public CombatTeam Team { get; set; } = CombatTeam.Player;

	[Export]
	public bool DebugDrawEnabled { get; set; } = true;

	[Signal]
	public delegate void HitEventHandler(HurtboxComponent hurtbox);

	private TransformComponent m_Transform;
	private Actor m_OwnerActor;
	private readonly HashSet<HurtboxComponent> m_HitThisAttack = new();
	private bool m_Active;
	private int m_AttackId;
	private Vector3 m_Offset;
	private Vector3 m_Size;

	public bool IsActive => m_Active;

	public int CurrentAttackId => m_AttackId;

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

	public void Activate(int attackId, Vector3 offset, Vector3 size)
	{
		m_AttackId = attackId;
		m_Offset = offset;
		m_Size = size;
		m_Active = true;
		m_HitThisAttack.Clear();
		QueueRedraw();
	}

	public void Deactivate()
	{
		m_Active = false;
		QueueRedraw();
	}

	public void PhysicsTick(double delta)
	{
		_ = delta;
		if (!m_Active || m_Transform == null)
		{
			return;
		}

		if (!TryGetWorldAabb(out var myAabb))
		{
			return;
		}

		foreach (var hurtbox in HurtboxRegistry.Snapshot())
		{
			if (hurtbox == null || !GodotObject.IsInstanceValid(hurtbox))
			{
				continue;
			}

			var targetActor = hurtbox.GetOwnerActor();
			if (targetActor == null || targetActor == m_OwnerActor)
			{
				continue;
			}

			if (hurtbox.Team == Team)
			{
				continue;
			}

			if (!hurtbox.TryGetWorldAabb(out var theirAabb))
			{
				continue;
			}

			if (!myAabb.Overlaps(theirAabb))
			{
				continue;
			}

			if (!m_HitThisAttack.Add(hurtbox))
			{
				continue;
			}

			EmitSignal(SignalName.Hit, hurtbox);
		}

		if (DebugDrawEnabled)
		{
			QueueRedraw();
		}
	}

	public bool TryGetWorldAabb(out LogicAabb aabb)
	{
		aabb = default;
		if (!m_Active || m_Transform == null)
		{
			return false;
		}

		var signed = LogicAabb.ApplyFacingOffset(m_Offset, m_Transform.GetFacing());
		var center = new Vector3(
			m_Transform.GetLogicX() + signed.X,
			m_Transform.GetLogicDepth() + signed.Y,
			m_Transform.GetVirtualZ() + signed.Z);
		aabb = LogicAabb.FromCenterSize(center, m_Size);
		return aabb.HasVolume;
	}

	public override void _Draw()
	{
		if (!DebugDrawEnabled || !m_Active)
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

		CombatDebugDraw.DrawVolume(
			this,
			aabb,
			m_Transform.GetLogicX(),
			m_Transform.GetLogicDepth(),
			new Color(0.95f, 0.2f, 0.2f, 0.95f));
	}
}
