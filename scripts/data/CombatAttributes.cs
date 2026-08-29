using Godot;

[GlobalClass]
public partial class CombatAttributes : Resource
{
	[Export]
	public int BaseHealth { get; set; } = 100;

	[Export]
	public int BaseAttack { get; set; } = 10;
}
