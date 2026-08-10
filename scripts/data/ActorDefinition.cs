using Godot;

[GlobalClass]
public partial class ActorDefinition : Resource
{
	[Export]
	public string Id { get; set; } = "";

	[Export]
	public ActorMovementConfig Movement { get; set; }
}
