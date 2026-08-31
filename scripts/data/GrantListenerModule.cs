using Godot;

[GlobalClass]
public partial class GrantListenerModule : SkillModule
{
	[Export]
	public GameplayEffect Effect { get; set; }

	public override void OnActivate(CombatComponent combat, SkillInstance instance)
	{
		combat?.ApplyModuleEffect(instance, Effect, toSelfOnly: true);
	}
}
