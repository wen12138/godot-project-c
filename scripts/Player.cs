using Godot;

public partial class Player : Actor
{
	private PlayerInputComponent m_PlayerInputComponent;

	public override void _Ready()
	{
		base._Ready();
		m_PlayerInputComponent = GetNodeOrNull<PlayerInputComponent>("PlayerInputComponent");
		if (m_PlayerInputComponent == null)
		{
			GD.PushError($"{GetPath()}: missing child PlayerInputComponent");
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		m_PlayerInputComponent?.PhysicsTick(delta);
		base._PhysicsProcess(delta);
	}
}
