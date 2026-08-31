using Godot;

[GlobalClass]
public partial class ApplyEffectModule : SkillModule
{
	[Export]
	public GameplayEffect Effect { get; set; }

	public override void OnActivate(CombatComponent combat, SkillInstance instance)
	{
		combat?.ApplyModuleEffect(instance, Effect, toSelfOnly: false);
	}
}
