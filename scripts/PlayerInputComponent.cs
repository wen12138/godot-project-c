using Godot;

public partial class PlayerInputComponent : Node
{
	private MovementComponent m_Movement;

	public override void _Ready()
	{
		m_Movement = GetNodeOrNull<MovementComponent>("../MovementComponent");
		if (m_Movement == null)
		{
			GD.PushError("PlayerInputComponent: missing sibling MovementComponent at ../MovementComponent");
		}
	}

	public void PhysicsTick(double delta)
	{
		if (m_Movement == null)
		{
			return;
		}

		m_Movement.SetMoveInput(InputActions.GetMoveVector());

		if (InputActions.IsJumpJustPressed())
		{
			m_Movement.Jump();
		}
	}
}
