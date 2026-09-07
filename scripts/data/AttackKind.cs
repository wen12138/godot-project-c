/// <summary>
/// 招式事件通道。决定本次 PlayAttack 发出 Basic* 还是 Skill*，从而控制监听效果是否响应。
/// 配置：挂到 Job.Attack 必须填 Basic；挂到 Job.Skill / Ultimate 必须填 Skill。授予模块不改 Kind。
/// </summary>
public enum AttackKind
{
	/// <summary>职业普攻槽打出的招式，发出 BasicAttackStarted / BasicAttackHit。监听默认只订阅此项。</summary>
	Basic = 0,

	/// <summary>战技 / 大招的 PlayAttack，发出 SkillAttackStarted / SkillAttackHit。监听要吃技能伤害须另勾 SubscribeSkill。</summary>
	Skill = 1
}

/// <summary>
/// 同 ConfigId 再次激活时的叠法。
/// 配置：目前只实现 Replace；填 Independent / Reject 会报错并仍按 Replace 处理。
/// </summary>
public enum SkillStacking
{
	/// <summary>卸掉旧实例（不跑到期爆发）再挂新实例。战技 / 大招默认。普攻每刀都是新短寿命实例，不走此路径。</summary>
	Replace = 0,

	/// <summary>允许同 ConfigId 多份并存。尚未实现。</summary>
	Independent = 1,

	/// <summary>已有同 ConfigId 实例则拒绝新激活。尚未实现。</summary>
	Reject = 2
}

/// <summary>
/// ApplyEffect 的选目标方式。PlayAttack 不读此项，命中只看 Hitbox 与 Hurtbox 重叠。
/// 配置：范围三项必须把 AreaRadius 填 &gt; 0；Self 不使用半径。
/// </summary>
public enum SkillTargeting
{
	/// <summary>只施加给施放者。光环、自身监听、周期脉冲盒用此项。</summary>
	Self = 0,

	/// <summary>水平半径内敌对 Actor。半径为 AreaRadius，高度用与近战相同的 VirtualZ 重叠。</summary>
	EnemiesInRadius = 1,

	/// <summary>水平半径内同阵营（含自己）。</summary>
	AlliesInRadius = 2,

	/// <summary>水平半径内所有 Actor（含自己）。</summary>
	EveryoneInRadius = 3
}
