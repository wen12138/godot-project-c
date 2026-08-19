using Godot;

public partial class MovementComponent : Node
{
	private ActorMovementConfig m_MovementConfig;
	private TransformComponent m_Transform;
	private Vector2 m_MoveInput = Vector2.Zero;
	private float m_VerticalVelocity;

	public ActorMovementConfig MovementConfig => m_MovementConfig;

	public override void _Ready()
	{
		var actor = GetParentOrNull<Actor>();
		if (actor == null)
		{
			GD.PushError($"{GetPath()}: parent is not Actor");
			return;
		}

		if (actor.Definition == null)
		{
			GD.PushError($"{GetPath()}: Actor.Definition is null");
			return;
		}

		if (actor.Definition.Movement == null)
		{
			GD.PushError($"{GetPath()}: Actor.Definition.Movement is null (Id={actor.Definition.Id})");
			return;
		}

		m_MovementConfig = actor.Definition.Movement;

		m_Transform = GetNodeOrNull<TransformComponent>("../TransformComponent");
		if (m_Transform == null)
		{
			GD.PushError($"{GetPath()}: missing sibling TransformComponent at ../TransformComponent");
		}
	}

	public void SetMoveInput(Vector2 direction)
	{
		m_MoveInput = direction == Vector2.Zero ? Vector2.Zero : direction.Normalized();
	}

	public void Jump()
	{
		if (m_MovementConfig == null || m_Transform == null)
		{
			return;
		}

		if (!IsGrounded(m_Transform.GetVirtualZ()))
		{
			return;
		}

		m_VerticalVelocity = m_MovementConfig.BaseJumpForce;
	}

	public void PhysicsTick(double delta)
	{
		if (m_MovementConfig == null || m_Transform == null)
		{
			return;
		}

		var dt = (float)delta;
		var virtualZ = m_Transform.GetVirtualZ();

		if (!IsGrounded(virtualZ))
		{
			m_VerticalVelocity -= m_MovementConfig.BaseGravity * dt;
			virtualZ += m_VerticalVelocity * dt;
		}

		if (virtualZ <= 0f)
		{
			virtualZ = 0f;
			m_VerticalVelocity = 0f;
		}

		if (!Mathf.IsEqualApprox(virtualZ, m_Transform.GetVirtualZ()))
		{
			m_Transform.SetVirtualZ(virtualZ);
		}

		var grounded = IsGrounded(virtualZ);
		if (m_MoveInput == Vector2.Zero)
		{
			return;
		}

		var speed = m_MovementConfig.BaseMoveSpeed;
		if (!grounded)
		{
			speed *= m_MovementConfig.BaseAerialMoveSpeedScale;
		}

		var newX = m_Transform.GetLogicX() + m_MoveInput.X * speed * dt;
		var newDepth = m_Transform.GetLogicDepth() + m_MoveInput.Y * speed * dt;
		m_Transform.SetLogicX(newX);
		m_Transform.SetLogicDepth(newDepth);
	}

	private bool IsGrounded(float virtualZ)
	{
		return virtualZ <= 0f && m_VerticalVelocity <= 0f;
	}
}
