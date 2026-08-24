using Godot;

public partial class CombatComponent : Node
{
	[Export]
	public AttackData Attack { get; set; }

	private HitboxComponent m_Hitbox;
	private float m_Remaining;
	private int m_NextAttackId = 1;

	public bool IsAttacking => m_Remaining > 0f;

	public override void _Ready()
	{
		m_Hitbox = GetNodeOrNull<HitboxComponent>("../HitboxComponent");
		if (m_Hitbox == null)
		{
			GD.PushError($"{GetPath()}: missing sibling HitboxComponent at ../HitboxComponent");
			return;
		}

		if (Attack == null)
		{
			GD.PushError($"{GetPath()}: Attack is null");
			return;
		}

		m_Hitbox.Hit += OnHit;
	}

	public override void _ExitTree()
	{
		if (m_Hitbox != null)
		{
			m_Hitbox.Hit -= OnHit;
		}
	}

	public void TryStartAttack()
	{
		if (m_Hitbox == null || Attack == null)
		{
			return;
		}

		if (m_Remaining > 0f)
		{
			return;
		}

		var attackId = m_NextAttackId;
		m_NextAttackId += 1;
		m_Hitbox.Activate(attackId, Attack.HitboxOffset, Attack.HitboxSize);
		m_Remaining = Attack.ActiveDuration;
	}

	public void PhysicsTick(double delta)
	{
		if (m_Hitbox == null || m_Remaining <= 0f)
		{
			return;
		}

		m_Remaining -= (float)delta;
		if (m_Remaining <= 0f)
		{
			m_Remaining = 0f;
			m_Hitbox.Deactivate();
		}
	}

	private void OnHit(HurtboxComponent hurtbox)
	{
		var target = hurtbox.GetOwnerActor();
		var name = target != null ? target.Name.ToString() : hurtbox.Name.ToString();
		GD.Print($"CombatComponent: hit {name} attackId={m_Hitbox.CurrentAttackId}");
	}
}
