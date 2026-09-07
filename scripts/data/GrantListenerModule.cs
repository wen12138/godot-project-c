using Godot;

/// <summary>
/// 激活当帧把效果挂到施放者自己身上（忽略 Targeting），用于监听普攻 / 技能并开附加盒或充能。
/// 配置：作为 SkillDefinition.Modules 的一项，拖入带 SubscribeBasic / ExtraHitbox / ChargeMax 的 GameplayEffect。
/// 无 Cue，不随招式时钟推迟。Job.Attack（Kind=Basic）禁止带本模块。
/// </summary>
[GlobalClass]
public partial class GrantListenerModule : SkillModule
{
	/// <summary>
	/// 挂到施放者自己的效果蓝图。
	/// 配置：Duration &gt; 0 才会在身上停留。勾选 SubscribeBasic（默认）以响应普攻；要吃技能伤害再勾 SubscribeSkill。
	/// </summary>
	[Export]
	public GameplayEffect Effect { get; set; }

	public override void OnActivate(CombatComponent combat, SkillInstance instance)
	{
		combat?.ApplyModuleEffect(instance, Effect, toSelfOnly: true);
	}
}
