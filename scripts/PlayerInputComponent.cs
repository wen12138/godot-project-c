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
		_ = delta;
		var playBusy = m_Combat != null && m_Combat.IsPlayOccupied;
		var grounded = m_Movement == null || m_Movement.IsOnGround;

		if (m_Movement != null)
		{
			m_Movement.SetMoveInput(playBusy ? Vector2.Zero : InputActions.GetMoveVector());
		}

		if (m_Combat == null)
		{
			if (m_Movement != null && InputActions.IsJumpJustPressed() && !playBusy)
			{
				m_Movement.Jump();
			}

			return;
		}

		if (InputActions.IsUltimateJustPressed())
		{
			if (grounded && !playBusy)
			{
				m_Combat.TryStartUltimate();
			}

			return;
		}

		if (InputActions.IsSkillJustPressed())
		{
			if (grounded && !playBusy)
			{
				m_Combat.TryStartSkill();
			}

			return;
		}

		if (InputActions.IsAttackJustPressed())
		{
			if (grounded && !playBusy)
			{
				m_Combat.TryStartAttack();
			}

			return;
		}

		if (InputActions.IsJumpJustPressed() && !playBusy && m_Movement != null)
		{
			if (m_Movement.Jump())
			{
				m_Combat.BreakCombo();
			}
		}
	}
}
