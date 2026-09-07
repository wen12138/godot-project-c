using Godot;

/// <summary>
/// 激活成功后按招式时钟播放判定盒。占用出招直到本段总时长结束。
/// 配置：作为 SkillDefinition.Modules 的一项；Specs 拖入一份或多份 AttackSpec（有序）。
/// 普攻按连段下标取 Specs；战技 / 大招按该 ConfigId 的续招表取段。长度为 1 时只打一段、收招不开窗。
/// </summary>
[GlobalClass]
public partial class PlayAttackModule : SkillModule
{
	/// <summary>
	/// 有序招式段。下标 0 为起手，之后为续招。
	/// 配置：至少一项且每项必须带至少一只 Hitbox。空列表或无效项会报错并拒绝这次激活。
	/// 最后一段收招后不开续招窗；非最后一段 FollowUpWindow &lt;= 0 则该段收招立刻清段。
	/// </summary>
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
