using Godot;

/// <summary>
/// ApplyEffect 相对本次 PlayAttack 时钟的施加时刻。
/// 配置：无 PlayAttack 的纯授予技只能用 PlayStartupStart（激活当帧）；其它 Cue 需要同定义里有 PlayAttack。
/// </summary>
public enum ApplyEffectCue
{
	/// <summary>招式 elapsed=0，即前摇开始。默认。无 PlayAttack 时退化为激活当帧。</summary>
	PlayStartupStart = 0,

	/// <summary>本段 AttackSpec.Startup，即判定段开始。前摇被取消则效果不挂。</summary>
	PlayActiveStart = 1,

	/// <summary>Startup+Active，即后摇开始。</summary>
	PlayRecoveryStart = 2,

	/// <summary>招式正常结束（TotalDuration）。取消导致从未走到此时刻则不施加。</summary>
	PlayComplete = 3,

	/// <summary>使用模块上的 ApplyAt（相对招式起点的秒）。任意提前量用此项。</summary>
	PlayElapsed = 4
}

/// <summary>
/// 按 Targeting 对选中目标施加一份 GameplayEffect。
/// 配置：作为 SkillDefinition.Modules 的一项，指定 Effect 与 Cue。
/// 有 PlayAttack 时等招式时钟跨过 Cue 才挂上；无 PlayAttack 且 Cue=PlayStartupStart 则激活当帧施加。
/// </summary>
[GlobalClass]
public partial class ApplyEffectModule : SkillModule
{
	/// <summary>
	/// 要施加的效果蓝图。
	/// 配置：拖入一份 GameplayEffect。空则跳过。运行时按目标 Duplicate 成实例，不会改这份 .tres。
	/// </summary>
	[Export]
	public GameplayEffect Effect { get; set; }

	/// <summary>
	/// 何时施加。有 PlayAttack 时相对正在播的那一段 AttackSpec 解析时刻。
	/// 配置：默认 PlayStartupStart。要「进入判定段才挂持续」选 PlayActiveStart。无招式时钟时不要选其它值。
	/// </summary>
	[Export]
	public ApplyEffectCue Cue { get; set; } = ApplyEffectCue.PlayStartupStart;

	/// <summary>
	/// 仅 Cue=PlayElapsed 时有效的施加时刻（秒，相对招式起点）。
	/// 配置：默认 -1。填 &lt; 0 会报错并跳过该模块；大于招式总长则钳到结束时刻。
	/// </summary>
	[Export]
	public float ApplyAt { get; set; } = -1f;

	public override void OnActivate(CombatComponent combat, SkillInstance instance)
	{
		if (combat == null || instance?.Definition == null)
		{
			return;
		}

		if (instance.Definition.HasPlayAttack())
		{
			return;
		}

		if (Cue != ApplyEffectCue.PlayStartupStart)
		{
			GD.PushError($"{combat.GetPath()}: ApplyEffect Cue={Cue} requires PlayAttack ({instance.ConfigId})");
			return;
		}

		combat.ApplyModuleEffect(instance, Effect, toSelfOnly: false);
	}
}
