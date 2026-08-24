using Godot;

public partial class Actor : Node2D
{
	[Export]
	public ActorDefinition Definition { get; set; }

	private TransformComponent m_TransformComponent;
	private MovementComponent m_MovementComponent;
	private CombatComponent m_CombatComponent;
	private HitboxComponent m_HitboxComponent;
	private HurtboxComponent m_HurtboxComponent;

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

		m_HurtboxComponent = GetNodeOrNull<HurtboxComponent>("HurtboxComponent");
		if (m_HurtboxComponent == null)
		{
			GD.PushError($"{GetPath()}: missing child HurtboxComponent");
		}

		m_CombatComponent = GetNodeOrNull<CombatComponent>("CombatComponent");
		m_HitboxComponent = GetNodeOrNull<HitboxComponent>("HitboxComponent");
	}

	public override void _PhysicsProcess(double delta)
	{
		m_MovementComponent?.PhysicsTick(delta);
		m_HitboxComponent?.PhysicsTick(delta);
		m_CombatComponent?.PhysicsTick(delta);
		m_HurtboxComponent?.RedrawDebug();
	}
}
