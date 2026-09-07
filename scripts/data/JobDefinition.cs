using Godot;

/// <summary>
/// 职业蓝图：位移实现与技能槽。可被多份 ActorDefinition 共用。
/// 配置：新建 Resource → JobDefinition，填 Locomotion / Movement，再按角色需要绑 Attack / Skill / Ultimate。
/// </summary>
[GlobalClass]
public partial class JobDefinition : Resource
{
	/// <summary>
	/// 位移预制体。进场时实例化为 Actor 的直接子节点。
	/// 配置：拖入 PackedScene，根节点必须是 MovementComponent（或其子类）。必填。换移动实现 = 换这份场景。
	/// </summary>
	[Export]
	public PackedScene Locomotion { get; set; }

	/// <summary>
	/// 该职业的移动数值（移速、跳跃力、空中移速比、重力）。
	/// 配置：拖入 ActorMovementConfig。必填。同一 Locomotion 场景可配不同数值区分 Player / Enemy。
	/// </summary>
	[Export]
	public ActorMovementConfig Movement { get; set; }

	/// <summary>
	/// 普攻技能。
	/// 配置：拖入 Kind=Basic 的 SkillDefinition。挂了 CombatComponent 的角色必填；Enemy 不出招可留空。
	/// 禁止带 ApplyEffect / GrantListener。多段连招把多份 AttackSpec 放进其 PlayAttack.Specs。
	/// </summary>
	[Export]
	public SkillDefinition Attack { get; set; }

	/// <summary>
	/// 闪避实现场景。
	/// 配置：允许空。当前不实例化、不跑逻辑。
	/// </summary>
	[Export]
	public PackedScene Dodge { get; set; }

	/// <summary>
	/// 战技。
	/// 配置：拖入 Kind=Skill 的 SkillDefinition，允许空。多段续招 = PlayAttack.Specs 长度 &gt; 1，Cooldown 为整条技能总 CD。
	/// </summary>
	[Export]
	public SkillDefinition Skill { get; set; }

	/// <summary>
	/// 大招。
	/// 配置：拖入 Kind=Skill 的 SkillDefinition，允许空。与战技共用同一套激活与续招规则，按键与 ConfigId 独立。
	/// </summary>
	[Export]
	public SkillDefinition Ultimate { get; set; }
}
