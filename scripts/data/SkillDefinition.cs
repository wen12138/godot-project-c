using Godot;

/// <summary>
/// 技能蓝图：普攻、战技、大招共用此类型，挂到 Job 对应槽上。
/// 配置：新建 Resource → SkillDefinition，填 ConfigId / Kind / Modules，再拖到 JobDefinition.Attack / Skill / Ultimate。
/// 至少要有 PlayAttack，或 ApplyEffect / GrantListener 之一；运行时不会改这份 .tres。
/// </summary>
[GlobalClass]
public partial class SkillDefinition : Resource
{
	/// <summary>
	/// 蓝图身份，也是 Replace 与冷却、续招表的查找键。
	/// 配置：稳定字符串，不要用 .tres 路径。约定 skill.&lt;职业键&gt;.&lt;槽或名&gt;，例如 skill.player_default.attack。
	/// 空字符串无法激活。
	/// </summary>
	[Export]
	public string ConfigId { get; set; } = "";

	/// <summary>
	/// 本定义打出的招式事件通道。
	/// 配置：Job.Attack 必须 Basic；Job.Skill / Ultimate 必须 Skill。不符则进场报错。授予模块不改此项。
	/// </summary>
	[Export]
	public AttackKind Kind { get; set; } = AttackKind.Basic;

	/// <summary>
	/// 激活消耗。
	/// 配置：资源池尚未实现，必须填 0（普攻也是 0）。非 0 会报错并拒绝激活。
	/// </summary>
	[Export]
	public int Cost { get; set; }

	/// <summary>
	/// 激活成功当帧起算的冷却（秒）。
	/// 配置：0 表示无 CD。战技 / 大招多段续招成功时不刷新 CD，只在窗关着的起手写入。
	/// </summary>
	[Export]
	public float Cooldown { get; set; }

	/// <summary>
	/// 同 ConfigId 再激活时的叠法。
	/// 配置：默认 Replace（卸旧上新，不跑到期爆发）。Independent / Reject 尚未实现，读到会报错并仍按 Replace。
	/// </summary>
	[Export]
	public SkillStacking Stacking { get; set; } = SkillStacking.Replace;

	/// <summary>
	/// ApplyEffect 的选目标。PlayAttack 忽略此项，命中只看判定盒重叠。
	/// 配置：自身光环 / 脉冲盒填 Self；范围 Buff 或 DoT 填对应半径项，并给 AreaRadius &gt; 0。
	/// ExtraHitbox 周期开盒要求 Targeting=Self，否则报错且 Tick 不开盒。
	/// </summary>
	[Export]
	public SkillTargeting Targeting { get; set; } = SkillTargeting.Self;

	/// <summary>
	/// 范围选目标的水平半径（逻辑空间 LogicX + LogicDepth）。
	/// 配置：仅 Targeting 不是 Self 时使用。必须 &gt; 0，否则该次 ApplyEffect 跳过。高度仍用 VirtualZ 重叠，与近战相同。
	/// </summary>
	[Export]
	public float AreaRadius { get; set; }

	/// <summary>
	/// 模块列表，组合出招、施加效果、授予监听。
	/// 配置：元素类型选 PlayAttackModule / ApplyEffectModule / GrantListenerModule。
	/// 数组顺序只决定同一帧内的执行次序，模块默认互不等待。普攻禁止带授予模块。
	/// </summary>
	[Export]
	public Godot.Collections.Array<SkillModule> Modules { get; set; } = new();

	public bool HasPlayAttack()
	{
		if (Modules == null)
		{
			return false;
		}

		foreach (var module in Modules)
		{
			if (module is PlayAttackModule)
			{
				return true;
			}
		}

		return false;
	}

	public bool HasGrantModules()
	{
		if (Modules == null)
		{
			return false;
		}

		foreach (var module in Modules)
		{
			if (module is ApplyEffectModule || module is GrantListenerModule)
			{
				return true;
			}
		}

		return false;
	}
}
