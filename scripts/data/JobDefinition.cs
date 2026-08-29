using Godot;

[GlobalClass]
public partial class JobDefinition : Resource
{
	[Export]
	public PackedScene Locomotion { get; set; }

	[Export]
	public ActorMovementConfig Movement { get; set; }

	[Export]
	public AttackData Attack { get; set; }

	[Export]
	public PackedScene Dodge { get; set; }

	[Export]
	public PackedScene Skill { get; set; }

	[Export]
	public PackedScene Ultimate { get; set; }
}
