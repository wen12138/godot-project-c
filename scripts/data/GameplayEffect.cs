using Godot;

[GlobalClass]
public partial class GameplayEffect : Resource
{
	[Export]
	public float Duration { get; set; }

	[Export]
	public float Period { get; set; }

	[Export]
	public int TickDamage { get; set; }

	[Export]
	public bool SubscribeBasic { get; set; } = true;

	[Export]
	public bool SubscribeSkill { get; set; }

	[Export]
	public HitboxEntry ExtraHitbox { get; set; }

	[Export]
	public float ExtraHitboxDuration { get; set; } = 0.15f;

	[Export]
	public int ChargeMax { get; set; }

	[Export]
	public int BurstDamage { get; set; }

	[Export]
	public float BurstRadius { get; set; } = 80f;
}
