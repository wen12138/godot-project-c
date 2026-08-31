using Godot;

[GlobalClass]
public partial class HitboxEntry : Resource
{
	[Export]
	public float Start { get; set; } = -1f;

	[Export]
	public float End { get; set; } = -1f;

	[Export]
	public Vector3 Offset { get; set; } = new(48f, 0f, 36f);

	[Export]
	public Vector3 Size { get; set; } = new(72f, 28f, 72f);
}
