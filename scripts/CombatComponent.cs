using Godot;

public partial class CombatComponent : Node
{
	private AttackData m_Attack;
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

		var actor = GetParentOrNull<Actor>();
		if (actor == null)
		{
			GD.PushError($"{GetPath()}: parent is not Actor");
			return;
		}

		if (actor.Definition?.Job == null)
		{
			GD.PushError($"{GetPath()}: Actor.Definition.Job is null");
			return;
		}

		m_Attack = actor.Definition.Job.Attack;
		if (m_Attack == null)
		{
			GD.PushError($"{GetPath()}: Job.Attack is null");
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
		if (m_Hitbox == null || m_Attack == null)
		{
			return;
		}

		if (m_Remaining > 0f)
		{
			return;
		}

		var attackId = m_NextAttackId;
		m_NextAttackId += 1;
		m_Hitbox.Activate(attackId, m_Attack.HitboxOffset, m_Attack.HitboxSize);
		m_Remaining = m_Attack.ActiveDuration;
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
			m_Hitbox.DeactivateAll();
		}
	}

	private void OnHit(HurtboxComponent hurtbox, int attackId)
	{
		var target = hurtbox.GetOwnerActor();
		var name = target != null ? target.Name.ToString() : hurtbox.Name.ToString();
		GD.Print($"CombatComponent: hit {name} attackId={attackId}");

		var attacker = GetParentOrNull<Actor>();
		if (attacker == null)
		{
			return;
		}

		var health = target?.Health;
		if (health == null)
		{
			return;
		}

		health.TakeDamage(attacker.GetAttackPower());
	}
}
