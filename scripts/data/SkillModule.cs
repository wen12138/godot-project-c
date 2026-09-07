using Godot;

/// <summary>
/// 技能模块基类。具体能力由子类 Resource 承担，填进 SkillDefinition.Modules。
/// 配置：不要直接建本类型；在 Modules 数组里新建 PlayAttackModule、ApplyEffectModule 或 GrantListenerModule。
/// </summary>
[GlobalClass]
public partial class SkillModule : Resource
{
	public virtual void OnActivate(CombatComponent combat, SkillInstance instance)
	{
	}
}
