using Godot;

[GlobalClass]
public partial class PlayAttackModule : SkillModule
{
	[Export]
	public Godot.Collections.Array<AttackSpec> Specs { get; set; } = new();

	public override void OnActivate(CombatComponent combat, SkillInstance instance)
	{
		if (combat == null)
		{
			return;
		}

		combat.BeginPlayAttack(instance, this);
	}
}
