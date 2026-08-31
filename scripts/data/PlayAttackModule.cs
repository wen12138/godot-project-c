using Godot;

[GlobalClass]
public partial class PlayAttackModule : SkillModule
{
	[Export]
	public AttackSpec Spec { get; set; }

	public override void OnActivate(CombatComponent combat, SkillInstance instance)
	{
		combat?.BeginPlayAttack(instance, Spec);
	}
}
