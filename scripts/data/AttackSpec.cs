using Godot;

/// <summary>
/// 单段招式的时间窗与判定盒蓝图，由 <see cref="PlayAttackModule.Specs"/> 按顺序引用。
/// 配置：在文件系统中新建 Resource → AttackSpec，再拖进 PlayAttack 的 Specs。
/// 总时长 = Startup + Active + Recovery。连招每段各用一份，最后一段即使填了续招窗也不会再开窗。
/// </summary>
[GlobalClass]
public partial class AttackSpec : Resource
{
	/// <summary>
	/// 前摇时长（秒）。此段不开判定盒。
	/// 配置：填非负秒数，默认 0。HitboxEntry 未单独填 Start 时，判定窗从此刻开始。
	/// </summary>
	[Export]
	public float Startup { get; set; }

	/// <summary>
	/// 默认判定段长度（秒）。
	/// 配置：默认 0.2，与现普攻一致。HitboxEntry 未单独填 End 时，判定窗在 Startup+Active 结束。
	/// </summary>
	[Export]
	public float Active { get; set; } = 0.2f;

	/// <summary>
	/// 后摇时长（秒）。此段默认已关盒，但仍占用出招（不能走、跳、放别的招）。
	/// 配置：填非负秒数，默认 0。
	/// </summary>
	[Export]
	public float Recovery { get; set; }

	/// <summary>
	/// 允许取消的时刻，相对本次招式起点（秒）。
	/// 配置：&lt; 0 表示不开放取消（默认 -1）。当前运行时不读取，留给预输入 / 后摇取消。
	/// </summary>
	[Export]
	public float CancelOpenAt { get; set; } = -1f;

	/// <summary>
	/// 本段 Startup+Active+Recovery 走尽之后的续招窗（秒）。
	/// 配置：默认 0.5。普攻窗内再按接下一段；战技窗内再按同一技能接下一段。
	/// 最后一段即使 &gt; 0 也不开窗。填 &lt;= 0 则该段收招后立刻清连 / 清段。
	/// </summary>
	[Export]
	public float FollowUpWindow { get; set; } = 0.5f;

	/// <summary>
	/// 本段判定盒列表。至少填一只；多只时间可重叠，每只开盒分配独立 AttackId。
	/// 配置：数组元素选 HitboxEntry。未填 Start/End（&lt; 0）时使用 Startup ~ Startup+Active 作为整段默认窗。
	/// </summary>
	[Export]
	public Godot.Collections.Array<HitboxEntry> Hitboxes { get; set; } = new();

	public float TotalDuration => Mathf.Max(0f, Startup) + Mathf.Max(0f, Active) + Mathf.Max(0f, Recovery);

	public bool TryResolveWindow(HitboxEntry entry, out float start, out float end)
	{
		start = 0f;
		end = 0f;
		if (entry == null)
		{
			return false;
		}

		start = entry.Start >= 0f ? entry.Start : Startup;
		end = entry.End >= 0f ? entry.End : Startup + Active;
		return end > start;
	}
}
