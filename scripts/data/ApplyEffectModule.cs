using Godot;

public enum ApplyEffectCue
{
	PlayStartupStart = 0,
	PlayActiveStart = 1,
	PlayRecoveryStart = 2,
	PlayComplete = 3,
	PlayElapsed = 4
}

[GlobalClass]
public partial class ApplyEffectModule : SkillModule
{
	[Export]
	public GameplayEffect Effect { get; set; }

	[Export]
	public ApplyEffectCue Cue { get; set; } = ApplyEffectCue.PlayStartupStart;

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
