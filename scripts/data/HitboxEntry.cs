using Godot;

/// <summary>
/// 一只逻辑 AABB 判定盒的时间窗与几何。
/// 配置：作为 AttackSpec.Hitboxes 的元素，或作为 GameplayEffect.ExtraHitbox 的附加盒几何。
/// Vector3 含义与 LogicAabb 相同：X=LogicX，Y=LogicDepth，Z=VirtualZ。
/// </summary>
[GlobalClass]
public partial class HitboxEntry : Resource
{
	/// <summary>
	/// 开盒时刻，相对本次招式起点（秒）。
	/// 配置：填 &gt;= 0 的绝对时刻；默认 -1 表示沿用 AttackSpec.Startup。
	/// 用作 ExtraHitbox 时 Start/End 均被忽略，寿命改看 ExtraHitboxDuration。
	/// </summary>
	[Export]
	public float Start { get; set; } = -1f;

	/// <summary>
	/// 关盒时刻，相对本次招式起点（秒）。必须大于 Start。
	/// 配置：填 &gt;= 0 的绝对时刻；默认 -1 表示沿用 Startup+Active。超出招式总长会被钳到总长。
	/// </summary>
	[Export]
	public float End { get; set; } = -1f;

	/// <summary>
	/// 相对角色逻辑坐标的盒中心偏移。
	/// 配置：X 会随面向翻转（朝左时 X 取反）；Y 为纵深；Z 为离地高度。默认 (48, 0, 36) 偏向前方。
	/// </summary>
	[Export]
	public Vector3 Offset { get; set; } = new(48f, 0f, 36f);

	/// <summary>
	/// 判定盒全尺寸（不是半长）。
	/// 配置：X=水平宽度，Y=纵深厚度，Z=高度。默认 (72, 28, 72)。命中看与目标 Hurtbox 的逻辑 AABB 重叠。
	/// </summary>
	[Export]
	public Vector3 Size { get; set; } = new(72f, 28f, 72f);
}
