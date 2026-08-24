using Godot;

[GlobalClass]
public partial class AttackData : Resource
{
	[Export]
	public float ActiveDuration { get; set; } = 0.2f;

	[Export]
	public Vector3 HitboxOffset { get; set; } = new(48f, 0f, 36f);

	[Export]
	public Vector3 HitboxSize { get; set; } = new(72f, 28f, 72f);
}
