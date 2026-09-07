using Godot;

/// <summary>
/// 角色基础属性蓝图。只存基础值，不存当前生命或结算后的最终攻击。
/// 配置：新建 Resource → CombatAttributes，再拖到 ActorDefinition.Attributes。
/// Actor 进场会 Duplicate，禁止在运行时改这份 .tres。
/// </summary>
[GlobalClass]
public partial class CombatAttributes : Resource
{
	/// <summary>
	/// 基础生命。当前结算恒等为最大生命。
	/// 配置：正整数，默认 100。不要在此写当前 HP；可变生命在 HealthComponent。
	/// </summary>
	[Export]
	public int BaseHealth { get; set; } = 100;

	/// <summary>
	/// 基础攻击力。命中扣血读攻击方 GetAttackPower()，当前结算恒等为此值。
	/// 配置：非负整数，默认 10。判定盒命中不读 AttackSpec 伤害字段。
	/// </summary>
	[Export]
	public int BaseAttack { get; set; } = 10;
}
