using Godot;

public partial class MovementComponent : Node
{
	private ActorMovementConfig m_MovementConfig;
	private TransformComponent m_Transform;
	private Vector2 m_MoveInput = Vector2.Zero;

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
	}

	public void PhysicsTick(double delta)
	{
		if (m_MovementConfig == null || m_Transform == null)
		{
			return;
		}

		if (m_MoveInput == Vector2.Zero)
		{
			return;
		}

		var dt = (float)delta;
		var speed = m_MovementConfig.BaseMoveSpeed;
		var newX = m_Transform.GetLogicX() + m_MoveInput.X * speed * dt;
		var newDepth = m_Transform.GetLogicDepth() + m_MoveInput.Y * speed * dt;
		m_Transform.SetLogicX(newX);
		m_Transform.SetLogicDepth(newDepth);
	}
}
