using Godot;

public partial class Actor : Node2D
{
	[Export]
	public ActorDefinition Definition { get; set; }

	private TransformComponent m_TransformComponent;
	private MovementComponent m_MovementComponent;

	public override void _Ready()
	{
		m_TransformComponent = GetNode<TransformComponent>("TransformComponent");
		m_MovementComponent = GetNode<MovementComponent>("MovementComponent");
	}

	public override void _PhysicsProcess(double delta)
	{
	}
}
