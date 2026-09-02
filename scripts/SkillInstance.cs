using System.Collections.Generic;

public sealed class PlayBoxState
{
	public HitboxEntry Entry;
	public float WindowStart;
	public float WindowEnd;
	public bool BoxOpen;
	public int BoxAttackId;
}

public sealed class PlayAttackState
{
	public AttackSpec Spec;
	public float Elapsed;
	public float Total;
	public int ComboIndex;
	public bool IsLastComboHit;
	public List<PlayBoxState> Boxes = new();
}

public sealed class SkillInstance
{
	public string ConfigId;
	public uint RuntimeId;
	public AttackKind Kind;
	public SkillDefinition Definition;
	public PlayAttackState PlayAttack;
}
