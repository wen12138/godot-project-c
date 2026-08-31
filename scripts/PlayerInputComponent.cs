using Godot;

public partial class PlayerInputComponent : Node
{
	private MovementComponent m_Movement;
	private CombatComponent m_Combat;

	public void Bind(MovementComponent movement, CombatComponent combat)
	{
		m_Movement = movement;
		m_Combat = combat;
	}

	public void PhysicsTick(double delta)
	{
		if (m_Movement != null)
		{
			m_Movement.SetMoveInput(InputActions.GetMoveVector());

			if (InputActions.IsJumpJustPressed())
			{
				m_Movement.Jump();
			}
		}

		if (m_Combat != null && InputActions.IsAttackJustPressed())
		{
			m_Combat.TryStartAttack();
		}

		if (m_Combat != null && InputActions.IsSkillJustPressed())
		{
			m_Combat.TryStartSkill();
		}

		if (m_Combat != null && InputActions.IsUltimateJustPressed())
		{
			m_Combat.TryStartUltimate();
		}
	}
}
