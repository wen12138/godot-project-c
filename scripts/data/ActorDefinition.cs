using Godot;

[GlobalClass]
public partial class ActorDefinition : Resource
{
	[Export]
	public string Id { get; set; } = "";

	[Export]
	public CombatAttributes Attributes { get; set; }

	[Export]
	public JobDefinition Job { get; set; }
}
