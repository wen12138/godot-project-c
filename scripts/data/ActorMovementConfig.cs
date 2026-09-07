using Godot;

/// <summary>
/// 职业位移数值。只存蓝图，不积分坐标。
/// 配置：新建 Resource → ActorMovementConfig，拖到 JobDefinition.Movement。同一位移场景可配不同数值。
/// </summary>
[GlobalClass]
public partial class ActorMovementConfig : Resource
{
	/// <summary>
	/// 地面水平移速（逻辑单位 / 秒）。
	/// 配置：默认 200。出招占用期间位移输入会被忽略，改这项不影响判定盒。
	/// </summary>
	[Export]
	public float BaseMoveSpeed { get; set; } = 200f;

	/// <summary>
	/// 起跳初速度（逻辑高度 / 秒）。
	/// 配置：默认 400。出招占用期间不能跳。跳跃成功会清普攻连，不清战技续招段。
	/// </summary>
	[Export]
	public float BaseJumpForce { get; set; } = 400f;

	/// <summary>
	/// 空中水平移速相对地面的倍率。
	/// 配置：默认 0.7，即空中为地面移速的 70%。填 1 则空中与地面同速。
	/// </summary>
	[Export]
	public float BaseAerialMoveSpeedScale { get; set; } = 0.7f;

	/// <summary>
	/// 重力加速度（逻辑高度 / 秒²）。
	/// 配置：默认 980。只影响 VirtualZ 下落，不参与水平判定半径。
	/// </summary>
	[Export]
	public float BaseGravity { get; set; } = 980f;
}
