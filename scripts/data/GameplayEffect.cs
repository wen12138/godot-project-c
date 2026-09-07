using Godot;

/// <summary>
/// 持续效果蓝图：寿命、周期 Tick、监听附加盒、充能与爆发。
/// 配置：新建 Resource → GameplayEffect，再交给 ApplyEffectModule 或 GrantListenerModule。
/// 运行时 Duplicate 成实例挂在目标身上，不要在此存当前 Charge / 剩余时间。
/// </summary>
[GlobalClass]
public partial class GameplayEffect : Resource
{
	/// <summary>
	/// 效果寿命（秒），从真正施加时刻起算，不是从技能键按下起算。
	/// 配置：&lt;= 0 视为瞬时（挂上立刻卸掉，不跑到期爆发）。周期脉冲要把值填到盖住最后一窗（最后一跳 + ExtraHitboxDuration）。
	/// </summary>
	[Export]
	public float Duration { get; set; }

	/// <summary>
	/// Tick 间隔（秒）。
	/// 配置：&lt;= 0 不 Tick。&gt; 0 时施加当帧不打周期伤害 / 不开周期盒；第一次 Tick 在施加后经过 Period，之后每 Period 一次。
	/// </summary>
	[Export]
	public float Period { get; set; }

	/// <summary>
	/// 每次 Tick 对效果目标直接扣的伤害。
	/// 配置：0 表示不直接扣血。与 ExtraHitbox 开盒独立：可以只开盒、只扣 Tick、或两者都做。命中盒仍走攻击方攻击力。
	/// </summary>
	[Export]
	public int TickDamage { get; set; }

	/// <summary>
	/// 是否订阅普攻事件（BasicAttackStarted / BasicAttackHit）。
	/// 配置：默认 true。Started 可开 ExtraHitbox；Hit 可给 Charge +1。自身光环若不想跟普攻走，勾掉。
	/// </summary>
	[Export]
	public bool SubscribeBasic { get; set; } = true;

	/// <summary>
	/// 是否订阅技能伤害事件（SkillAttackStarted / SkillAttackHit）。
	/// 配置：默认 false。要让本效果响应战技 / 大招的 PlayAttack，必须显式勾选。
	/// </summary>
	[Export]
	public bool SubscribeSkill { get; set; }

	/// <summary>
	/// 监听 Started 或周期 Tick 时，在施放者身上开短命附加盒的几何。
	/// 配置：可空。填一份 HitboxEntry，只用 Offset / Size；Start / End 忽略。
	/// Tick 开盒要求效果挂在自己身上，且技能 Targeting 应为 Self。
	/// </summary>
	[Export]
	public HitboxEntry ExtraHitbox { get; set; }

	/// <summary>
	/// ExtraHitbox 打开的时长（秒）。
	/// 配置：默认 0.15。实现上仍按 Max(0.01, 值) 打开，避免短于一帧导致零命中。
	/// </summary>
	[Export]
	public float ExtraHitboxDuration { get; set; } = 0.15f;

	/// <summary>
	/// 充能满额。已订阅的 *Hit 使本实例 Charge +1。
	/// 配置：&lt;= 0 不充能。&gt; 0 时 Charge 达到此值立即爆发并卸掉效果（不走到期 OnExpire）。
	/// Replace / 死亡卸效果时 Charge 作废，不爆发。
	/// </summary>
	[Export]
	public int ChargeMax { get; set; }

	/// <summary>
	/// 满能或寿命到期时，对范围内敌人造成的爆发伤害。
	/// 配置：&lt;= 0 不打爆发伤害。目标固定为施放者周围 EnemiesInRadius，半径看 BurstRadius。
	/// </summary>
	[Export]
	public int BurstDamage { get; set; }

	/// <summary>
	/// 爆发选敌的水平半径（逻辑空间）。
	/// 配置：默认 80。高度规则与近战相同。仅 BurstDamage &gt; 0 时有意义。
	/// </summary>
	[Export]
	public float BurstRadius { get; set; } = 80f;
}
