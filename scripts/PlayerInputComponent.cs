using Godot;

public partial class PlayerInputComponent : Node
{
	private MovementComponent m_Movement;
	private CombatComponent m_Combat;

	public override void _Ready()
	{
		m_Movement = GetNodeOrNull<MovementComponent>("../MovementComponent");
		if (m_Movement == null)
		{
			GD.PushError("PlayerInputComponent: missing sibling MovementComponent at ../MovementComponent");
		}

		m_Combat = GetNodeOrNull<CombatComponent>("../CombatComponent");
		if (m_Combat == null)
		{
			GD.PushError("PlayerInputComponent: missing sibling CombatComponent at ../CombatComponent");
		}
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
	}
}
