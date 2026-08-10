using Godot;

[GlobalClass]
public partial class ActorMovementConfig : Resource
{
	[Export]
	public float BaseMoveSpeed { get; set; } = 200f;

	[Export]
	public float BaseJumpForce { get; set; } = 400f;

	[Export]
	public float BaseAerialMoveSpeedScale { get; set; } = 0.7f;
}
