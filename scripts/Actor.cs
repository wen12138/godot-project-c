using Godot;

public partial class Actor : Node2D
{
	[Export]
	public ActorDefinition Definition { get; set; }

	private TransformComponent m_TransformComponent;
	private MovementComponent m_MovementComponent;

	public override void _Ready()
	{
		m_TransformComponent = GetNodeOrNull<TransformComponent>("TransformComponent");
		if (m_TransformComponent == null)
		{
			GD.PushError($"{GetPath()}: missing child TransformComponent");
		}

		m_MovementComponent = GetNodeOrNull<MovementComponent>("MovementComponent");
		if (m_MovementComponent == null)
		{
			GD.PushError($"{GetPath()}: missing child MovementComponent");
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		m_MovementComponent?.PhysicsTick(delta);
	}
}
