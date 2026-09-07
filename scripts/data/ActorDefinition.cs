using Godot;

/// <summary>
/// Actor 上唯一导出的角色模板：身份 + 属性 + 职业。
/// 配置：新建 Resource → ActorDefinition，绑 Attributes 与 Job，再赋给场景里 Actor.Definition。
/// 进场后改 Definition 不会热切换职业。
/// </summary>
[GlobalClass]
public partial class ActorDefinition : Resource
{
	/// <summary>
	/// 角色模板身份。
	/// 配置：稳定字符串，例如 player_default。供调试与日后表查找，不参与技能 Replace。
	/// </summary>
	[Export]
	public string Id { get; set; } = "";

	/// <summary>
	/// 基础属性蓝图。
	/// 配置：拖入 CombatAttributes。必填；进场 Duplicate 后作为 GetMaxHealth / GetAttackPower 的输入。
	/// </summary>
	[Export]
	public CombatAttributes Attributes { get; set; }

	/// <summary>
	/// 职业蓝图：位移、移动数值、普攻 / 战技 / 大招槽。
	/// 配置：拖入 JobDefinition。必填。技能只从这里读取，不要在 Actor 上再导出一份 Job。
	/// </summary>
	[Export]
	public JobDefinition Job { get; set; }
}
