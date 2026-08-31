public sealed class PlayAttackState
{
	public AttackSpec Spec;
	public HitboxEntry Entry;
	public float Elapsed;
	public float Total;
	public float WindowStart;
	public float WindowEnd;
	public bool BoxOpen;
	public int BoxAttackId;
}

public sealed class SkillInstance
{
	public string ConfigId;
	public uint RuntimeId;
	public AttackKind Kind;
	public SkillDefinition Definition;
	public PlayAttackState PlayAttack;
}
