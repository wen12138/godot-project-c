using Godot;

[GlobalClass]
public partial class JobDefinition : Resource
{
	[Export]
	public PackedScene Locomotion { get; set; }

	[Export]
	public ActorMovementConfig Movement { get; set; }

	[Export]
	public SkillDefinition Attack { get; set; }

	[Export]
	public PackedScene Dodge { get; set; }

	[Export]
	public SkillDefinition Skill { get; set; }

	[Export]
	public SkillDefinition Ultimate { get; set; }
}
