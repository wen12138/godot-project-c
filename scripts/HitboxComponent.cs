using System.Collections.Generic;
using Godot;

public partial class HitboxComponent : Node2D
{
	[Export]
	public CombatTeam Team { get; set; } = CombatTeam.Player;

	[Export]
	public bool DebugDrawEnabled { get; set; } = true;

	[Signal]
	public delegate void HitEventHandler(HurtboxComponent hurtbox, int attackId);

	private sealed class ActiveStrike
	{
		public int AttackId;
		public Vector3 Offset;
		public Vector3 Size;
		public HashSet<HurtboxComponent> HitThisAttack = new();
	}

	private TransformComponent m_Transform;
	private Actor m_OwnerActor;
	private readonly List<ActiveStrike> m_Strikes = new();

	public bool IsActive => m_Strikes.Count > 0;

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
		for (var i = 0; i < m_Strikes.Count; i++)
		{
			if (m_Strikes[i].AttackId == attackId)
			{
				m_Strikes[i].Offset = offset;
				m_Strikes[i].Size = size;
				m_Strikes[i].HitThisAttack.Clear();
				QueueRedraw();
				return;
			}
		}

		m_Strikes.Add(new ActiveStrike
		{
			AttackId = attackId,
			Offset = offset,
			Size = size
		});
		QueueRedraw();
	}

	public void Deactivate(int attackId)
	{
		for (var i = m_Strikes.Count - 1; i >= 0; i--)
		{
			if (m_Strikes[i].AttackId == attackId)
			{
				m_Strikes.RemoveAt(i);
			}
		}

		QueueRedraw();
	}

	public void DeactivateAll()
	{
		m_Strikes.Clear();
		QueueRedraw();
	}

	public void PhysicsTick(double delta)
	{
		_ = delta;
		if (m_Strikes.Count == 0 || m_Transform == null)
		{
			return;
		}

		foreach (var strike in m_Strikes)
		{
			if (!TryGetWorldAabb(strike, out var myAabb))
			{
				continue;
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

				if (!strike.HitThisAttack.Add(hurtbox))
				{
					continue;
				}

				EmitSignal(SignalName.Hit, hurtbox, strike.AttackId);
			}
		}

		if (DebugDrawEnabled)
		{
			QueueRedraw();
		}
	}

	public override void _Draw()
	{
		if (!DebugDrawEnabled || m_Strikes.Count == 0)
		{
			return;
		}

		if (MapContext.Instance == null || !MapContext.Instance.HasOrigin)
		{
			return;
		}

		if (m_Transform == null)
		{
			return;
		}

		foreach (var strike in m_Strikes)
		{
			if (!TryGetWorldAabb(strike, out var aabb))
			{
				continue;
			}

			CombatDebugDraw.DrawVolume(
				this,
				aabb,
				m_Transform.GetLogicX(),
				m_Transform.GetLogicDepth(),
				new Color(0.95f, 0.2f, 0.2f, 0.95f));
		}
	}

	private bool TryGetWorldAabb(ActiveStrike strike, out LogicAabb aabb)
	{
		aabb = default;
		if (m_Transform == null)
		{
			return false;
		}

		var signed = LogicAabb.ApplyFacingOffset(strike.Offset, m_Transform.GetFacing());
		var center = new Vector3(
			m_Transform.GetLogicX() + signed.X,
			m_Transform.GetLogicDepth() + signed.Y,
			m_Transform.GetVirtualZ() + signed.Z);
		aabb = LogicAabb.FromCenterSize(center, strike.Size);
		return aabb.HasVolume;
	}
}
