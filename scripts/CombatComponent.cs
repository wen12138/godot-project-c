using System.Collections.Generic;
using Godot;

public partial class CombatComponent : Node
{
	[Signal]
	public delegate void BasicAttackStartedEventHandler(int attackId, int runtimeId);

	[Signal]
	public delegate void BasicAttackHitEventHandler(int attackId, int runtimeId, HurtboxComponent hurtbox);

	[Signal]
	public delegate void SkillAttackStartedEventHandler(int attackId, int runtimeId);

	[Signal]
	public delegate void SkillAttackHitEventHandler(int attackId, int runtimeId, HurtboxComponent hurtbox);

	private HitboxComponent m_Hitbox;
	private HealthComponent m_Health;
	private Actor m_Actor;
	private SkillDefinition m_BasicAttack;
	private uint m_NextRuntimeId = 1;
	private int m_NextAttackId = 1;
	private readonly List<SkillInstance> m_Instances = new();
	private readonly Dictionary<int, StrikeInfo> m_Strikes = new();
	private readonly Dictionary<string, float> m_CooldownRemaining = new();

	private struct StrikeInfo
	{
		public uint RuntimeId;
		public AttackKind Kind;
		public bool FromListener;
	}

	public bool IsAttacking => FindBasicPlayAttack() != null;

	public override void _Ready()
	{
		m_Hitbox = GetNodeOrNull<HitboxComponent>("../HitboxComponent");
		if (m_Hitbox == null)
		{
			GD.PushError($"{GetPath()}: missing sibling HitboxComponent at ../HitboxComponent");
			return;
		}

		m_Actor = GetParentOrNull<Actor>();
		if (m_Actor == null)
		{
			GD.PushError($"{GetPath()}: parent is not Actor");
			return;
		}

		if (m_Actor.Definition?.Job == null)
		{
			GD.PushError($"{GetPath()}: Actor.Definition.Job is null");
			return;
		}

		m_BasicAttack = m_Actor.Definition.Job.Attack;
		if (m_BasicAttack == null)
		{
			GD.PushError($"{GetPath()}: Job.Attack is null");
			return;
		}

		ValidateSlot(m_BasicAttack, expectedKind: AttackKind.Basic, slotName: "Attack");
		ValidateSlot(m_Actor.Definition.Job.Skill, expectedKind: AttackKind.Skill, slotName: "Skill");
		ValidateSlot(m_Actor.Definition.Job.Ultimate, expectedKind: AttackKind.Skill, slotName: "Ultimate");

		m_Hitbox.Hit += OnHit;

		m_Health = GetNodeOrNull<HealthComponent>("../HealthComponent");
		if (m_Health != null)
		{
			m_Health.Died += OnOwnerDied;
		}
	}

	public override void _ExitTree()
	{
		if (m_Hitbox != null)
		{
			m_Hitbox.Hit -= OnHit;
		}

		if (m_Health != null)
		{
			m_Health.Died -= OnOwnerDied;
		}
	}

	public void TryStartAttack()
	{
		TryActivate(m_BasicAttack);
	}

	public void TryStartSkill()
	{
		TryActivate(m_Actor?.Definition?.Job?.Skill);
	}

	public void TryStartUltimate()
	{
		TryActivate(m_Actor?.Definition?.Job?.Ultimate);
	}

	public bool TryActivate(SkillDefinition def)
	{
		if (m_Hitbox == null || m_Actor == null || def == null)
		{
			return false;
		}

		if (string.IsNullOrEmpty(def.ConfigId))
		{
			GD.PushError($"{GetPath()}: SkillDefinition.ConfigId is empty");
			return false;
		}

		if (def.Cost != 0)
		{
			GD.PushError($"{GetPath()}: Cost={def.Cost} but resource pool is not implemented ({def.ConfigId})");
			return false;
		}

		if (def.Stacking != SkillStacking.Replace)
		{
			GD.PushError($"{GetPath()}: Stacking={def.Stacking} not implemented, using Replace ({def.ConfigId})");
		}

		if (!def.HasPlayAttack() && !HasGrantModules(def))
		{
			GD.PushError($"{GetPath()}: skill has no PlayAttack and no grant modules ({def.ConfigId})");
			return false;
		}

		if (def.Kind == AttackKind.Basic && FindBasicPlayAttack() != null)
		{
			return false;
		}

		if (m_CooldownRemaining.TryGetValue(def.ConfigId, out var cdLeft) && cdLeft > 0f)
		{
			return false;
		}

		if (def.Kind == AttackKind.Skill)
		{
			ReplaceByConfigId(def.ConfigId);
		}

		var instance = new SkillInstance
		{
			ConfigId = def.ConfigId,
			RuntimeId = m_NextRuntimeId,
			Kind = def.Kind,
			Definition = def
		};
		m_NextRuntimeId += 1;
		m_Instances.Add(instance);

		if (def.Modules != null)
		{
			foreach (var module in def.Modules)
			{
				module?.OnActivate(this, instance);
			}
		}

		if (def.Cooldown > 0f)
		{
			m_CooldownRemaining[def.ConfigId] = def.Cooldown;
		}

		return true;
	}

	public void BeginPlayAttack(SkillInstance instance, AttackSpec spec)
	{
		if (instance == null || spec == null)
		{
			GD.PushError($"{GetPath()}: BeginPlayAttack missing instance or spec");
			return;
		}

		if (spec.Hitboxes == null || spec.Hitboxes.Count == 0)
		{
			GD.PushError($"{GetPath()}: AttackSpec.Hitboxes is empty ({instance.ConfigId})");
			return;
		}

		if (spec.Hitboxes.Count > 1)
		{
			GD.PushError($"{GetPath()}: AttackSpec has {spec.Hitboxes.Count} hitboxes; using the first ({instance.ConfigId})");
		}

		var entry = spec.Hitboxes[0];
		if (!spec.TryResolveWindow(entry, out var start, out var end))
		{
			GD.PushError($"{GetPath()}: invalid hitbox window ({instance.ConfigId})");
			return;
		}

		instance.PlayAttack = new PlayAttackState
		{
			Spec = spec,
			Entry = entry,
			Elapsed = 0f,
			Total = spec.TotalDuration,
			WindowStart = start,
			WindowEnd = end,
			BoxOpen = false,
			BoxAttackId = 0
		};

		if (start <= 0f)
		{
			TryOpenPlayBox(instance, instance.PlayAttack, previous: -1f);
		}
	}

	public void PhysicsTick(double delta)
	{
		var dt = (float)delta;
		TickCooldowns(dt);
		TickPlayAttacks(dt);
	}

	private void TickCooldowns(float dt)
	{
		if (m_CooldownRemaining.Count == 0)
		{
			return;
		}

		var keys = new List<string>(m_CooldownRemaining.Keys);
		foreach (var key in keys)
		{
			var left = m_CooldownRemaining[key] - dt;
			if (left <= 0f)
			{
				m_CooldownRemaining.Remove(key);
			}
			else
			{
				m_CooldownRemaining[key] = left;
			}
		}
	}

	private void TickPlayAttacks(float dt)
	{
		for (var i = m_Instances.Count - 1; i >= 0; i--)
		{
			var instance = m_Instances[i];
			var play = instance.PlayAttack;
			if (play == null)
			{
				if (instance.Kind == AttackKind.Basic)
				{
					m_Instances.RemoveAt(i);
				}

				continue;
			}

			var previous = play.Elapsed;
			play.Elapsed += dt;
			TryOpenPlayBox(instance, play, previous);
			TryClosePlayBox(instance, play, previous);

			if (play.Elapsed >= play.Total)
			{
				if (play.BoxOpen)
				{
					m_Hitbox.Deactivate(play.BoxAttackId);
					m_Strikes.Remove(play.BoxAttackId);
					play.BoxOpen = false;
				}

				instance.PlayAttack = null;
				if (instance.Kind == AttackKind.Basic)
				{
					m_Instances.RemoveAt(i);
				}
			}
		}
	}

	private void TryOpenPlayBox(SkillInstance instance, PlayAttackState play, float previous)
	{
		if (play.BoxOpen)
		{
			return;
		}

		if (previous < play.WindowStart && play.Elapsed >= play.WindowStart)
		{
			var attackId = m_NextAttackId;
			m_NextAttackId += 1;
			play.BoxAttackId = attackId;
			play.BoxOpen = true;
			m_Strikes[attackId] = new StrikeInfo
			{
				RuntimeId = instance.RuntimeId,
				Kind = instance.Kind,
				FromListener = false
			};
			m_Hitbox.Activate(attackId, play.Entry.Offset, play.Entry.Size);
			EmitAttackStarted(instance.Kind, attackId, instance.RuntimeId);
		}
	}

	private void TryClosePlayBox(SkillInstance instance, PlayAttackState play, float previous)
	{
		_ = instance;
		if (!play.BoxOpen)
		{
			return;
		}

		if (previous < play.WindowEnd && play.Elapsed >= play.WindowEnd)
		{
			m_Hitbox.Deactivate(play.BoxAttackId);
			m_Strikes.Remove(play.BoxAttackId);
			play.BoxOpen = false;
		}
	}

	private void EmitAttackStarted(AttackKind kind, int attackId, uint runtimeId)
	{
		if (kind == AttackKind.Basic)
		{
			EmitSignal(SignalName.BasicAttackStarted, attackId, (int)runtimeId);
		}
		else
		{
			EmitSignal(SignalName.SkillAttackStarted, attackId, (int)runtimeId);
		}
	}

	private void OnHit(HurtboxComponent hurtbox, int attackId)
	{
		var target = hurtbox.GetOwnerActor();
		var name = target != null ? target.Name.ToString() : hurtbox.Name.ToString();
		GD.Print($"CombatComponent: hit {name} attackId={attackId}");

		if (m_Actor == null)
		{
			return;
		}

		var health = target?.Health;
		if (health != null)
		{
			health.TakeDamage(m_Actor.GetAttackPower());
		}

		if (!m_Strikes.TryGetValue(attackId, out var info) || info.FromListener)
		{
			return;
		}

		if (info.Kind == AttackKind.Basic)
		{
			EmitSignal(SignalName.BasicAttackHit, attackId, (int)info.RuntimeId, hurtbox);
		}
		else
		{
			EmitSignal(SignalName.SkillAttackHit, attackId, (int)info.RuntimeId, hurtbox);
		}
	}

	private void ReplaceByConfigId(string configId)
	{
		for (var i = m_Instances.Count - 1; i >= 0; i--)
		{
			var instance = m_Instances[i];
			if (instance.ConfigId != configId)
			{
				continue;
			}

			CancelPlayAttack(instance);
			m_Instances.RemoveAt(i);
		}
	}

	private void CancelPlayAttack(SkillInstance instance)
	{
		var play = instance.PlayAttack;
		if (play == null)
		{
			return;
		}

		if (play.BoxOpen)
		{
			m_Hitbox.Deactivate(play.BoxAttackId);
			m_Strikes.Remove(play.BoxAttackId);
		}

		instance.PlayAttack = null;
	}

	private SkillInstance FindBasicPlayAttack()
	{
		foreach (var instance in m_Instances)
		{
			if (instance.Kind == AttackKind.Basic && instance.PlayAttack != null)
			{
				return instance;
			}
		}

		return null;
	}

	private void ValidateSlot(SkillDefinition def, AttackKind expectedKind, string slotName)
	{
		if (def == null)
		{
			return;
		}

		if (def.Kind != expectedKind)
		{
			GD.PushError($"{GetPath()}: Job.{slotName} Kind is {def.Kind}, expected {expectedKind}");
		}

		if (expectedKind == AttackKind.Basic && HasGrantModules(def))
		{
			GD.PushError($"{GetPath()}: Job.Attack must not grant duration effects");
		}
	}

	private static bool HasGrantModules(SkillDefinition def)
	{
		if (def?.Modules == null)
		{
			return false;
		}

		foreach (var module in def.Modules)
		{
			if (module != null && module is not PlayAttackModule)
			{
				return true;
			}
		}

		return false;
	}

	private void OnOwnerDied()
	{
		for (var i = m_Instances.Count - 1; i >= 0; i--)
		{
			CancelPlayAttack(m_Instances[i]);
			m_Instances.RemoveAt(i);
		}

		m_Hitbox?.DeactivateAll();
		m_Strikes.Clear();
	}
}
