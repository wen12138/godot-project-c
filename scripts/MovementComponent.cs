using Godot;

public partial class MovementComponent : Node
{
	private ActorMovementConfig m_MovementConfig;

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
	}

	public override void _Process(double delta)
	{
	}

	public void SetMoveInput(Vector2 direction)
	{
	}

	public void Jump()
	{
	}
}
