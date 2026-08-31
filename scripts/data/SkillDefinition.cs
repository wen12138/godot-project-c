using Godot;

[GlobalClass]
public partial class SkillDefinition : Resource
{
	[Export]
	public string ConfigId { get; set; } = "";

	[Export]
	public AttackKind Kind { get; set; } = AttackKind.Basic;

	[Export]
	public int Cost { get; set; }

	[Export]
	public float Cooldown { get; set; }

	[Export]
	public SkillStacking Stacking { get; set; } = SkillStacking.Replace;

	[Export]
	public SkillTargeting Targeting { get; set; } = SkillTargeting.Self;

	[Export]
	public float AreaRadius { get; set; }

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
