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
	private readonly EffectHolder m_Effects = new();
	private readonly List<ListenerBox> m_ListenerBoxes = new();
	private int m_ComboNextIndex;
	private float m_FollowUpRemaining;
	private readonly Dictionary<string, SkillComboState> m_SkillCombos = new();

	private struct SkillComboState
	{
		public int NextIndex;
		public float FollowUpRemaining;
	}

	private struct StrikeInfo
	{
		public uint RuntimeId;
		public AttackKind Kind;
		public bool FromListener;
	}

	private struct ListenerBox
	{
		public int AttackId;
		public float Remaining;
	}

	public bool IsAttacking => FindBasicPlayAttack() != null;

	public bool IsPlayOccupied => FindAnyPlayAttack() != null;

	public void BreakCombo()
	{
		m_ComboNextIndex = 0;
		m_FollowUpRemaining = 0f;
	}

	public void ClearSkillCombo(string configId)
	{
		if (string.IsNullOrEmpty(configId))
		{
			return;
		}

		m_SkillCombos.Remove(configId);
	}

	private bool IsSkillFollowUpOpen(string configId)
	{
		return !string.IsNullOrEmpty(configId)
			&& m_SkillCombos.TryGetValue(configId, out var state)
			&& state.FollowUpRemaining > 0f;
	}

	private static PlayAttackModule FindPlayAttackModule(SkillDefinition def)
	{
		if (def?.Modules == null)
		{
			return null;
		}

		foreach (var module in def.Modules)
		{
			if (module is PlayAttackModule play)
			{
				return play;
			}
		}

		return null;
	}

	private int ResolveComboIndex(SkillDefinition def, int specCount)
	{
		var index = 0;
		if (def.Kind == AttackKind.Basic)
		{
			index = m_ComboNextIndex;
		}
		else if (IsSkillFollowUpOpen(def.ConfigId))
		{
			index = m_SkillCombos[def.ConfigId].NextIndex;
		}

		if (index < 0 || index >= specCount)
		{
			GD.PushError($"{GetPath()}: combo index {index} out of range ({def.ConfigId})");
			index = 0;
		}

		return index;
	}

	private bool TryResolvePlaySpec(SkillDefinition def, out AttackSpec spec, out int index)
	{
		spec = null;
		index = 0;
		if (def == null || !def.HasPlayAttack())
		{
			return true;
		}

		var module = FindPlayAttackModule(def);
		var specs = module?.Specs;
		if (specs == null || CountNonNull(specs) == 0)
		{
			GD.PushError($"{GetPath()}: AttackSpec list is empty ({def.ConfigId})");
			if (def.Kind == AttackKind.Basic)
			{
				BreakCombo();
			}

			return false;
		}

		index = ResolveComboIndex(def, specs.Count);
		spec = specs[index];
		if (spec == null || spec.Hitboxes == null || spec.Hitboxes.Count == 0)
		{
			GD.PushError($"{GetPath()}: invalid AttackSpec at {index} ({def.ConfigId})");
			if (def.Kind == AttackKind.Basic)
			{
				BreakCombo();
			}

			return false;
		}

		return true;
	}

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
		BasicAttackStarted += OnBasicAttackStarted;
		SkillAttackStarted += OnSkillAttackStarted;
		BasicAttackHit += OnBasicAttackHit;
		SkillAttackHit += OnSkillAttackHit;

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

		BasicAttackStarted -= OnBasicAttackStarted;
		SkillAttackStarted -= OnSkillAttackStarted;
		BasicAttackHit -= OnBasicAttackHit;
		SkillAttackHit -= OnSkillAttackHit;

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

		if (!def.HasPlayAttack() && !def.HasGrantModules())
		{
			GD.PushError($"{GetPath()}: skill has no PlayAttack and no grant modules ({def.ConfigId})");
			return false;
		}

		if (FindAnyPlayAttack() != null)
		{
			return false;
		}

		if (def.HasPlayAttack() && !TryResolvePlaySpec(def, out _, out _))
		{
			return false;
		}

		var followUp = def.Kind == AttackKind.Skill && IsSkillFollowUpOpen(def.ConfigId);
		if (!followUp && m_CooldownRemaining.TryGetValue(def.ConfigId, out var cdLeft) && cdLeft > 0f)
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

		if (!followUp && def.Cooldown > 0f)
		{
			m_CooldownRemaining[def.ConfigId] = def.Cooldown;
		}

		if (def.Kind == AttackKind.Skill)
		{
			BreakCombo();
		}

		return true;
	}

	public void BeginPlayAttack(SkillInstance instance, PlayAttackModule module)
	{
		if (instance == null || module == null)
		{
			GD.PushError($"{GetPath()}: BeginPlayAttack missing instance or module");
			return;
		}

		if (instance.Definition == null)
		{
			GD.PushError($"{GetPath()}: BeginPlayAttack missing SkillDefinition ({instance.ConfigId})");
			return;
		}

		var specs = module.Specs;
		if (specs == null || CountNonNull(specs) == 0)
		{
			GD.PushError($"{GetPath()}: AttackSpec list is empty ({instance.ConfigId})");
			if (instance.Kind == AttackKind.Basic)
			{
				BreakCombo();
			}

			return;
		}

		var index = ResolveComboIndex(instance.Definition, specs.Count);
		var spec = specs[index];
		if (spec == null || spec.Hitboxes == null || spec.Hitboxes.Count == 0)
		{
			GD.PushError($"{GetPath()}: invalid AttackSpec at {index} ({instance.ConfigId})");
			if (instance.Kind == AttackKind.Basic)
			{
				BreakCombo();
			}

			return;
		}

		if (instance.Kind == AttackKind.Basic)
		{
			m_FollowUpRemaining = 0f;
		}
		else if (instance.Kind == AttackKind.Skill
			&& m_SkillCombos.TryGetValue(instance.ConfigId, out var skillCombo))
		{
			skillCombo.FollowUpRemaining = 0f;
			m_SkillCombos[instance.ConfigId] = skillCombo;
		}

		BeginPlayAttackFromSpec(instance, spec, index, isLast: index >= specs.Count - 1);
	}

	private static int CountNonNull(Godot.Collections.Array<AttackSpec> specs)
	{
		var count = 0;
		foreach (var spec in specs)
		{
			if (spec != null)
			{
				count += 1;
			}
		}

		return count;
	}

	private void BeginPlayAttackFromSpec(SkillInstance instance, AttackSpec spec, int comboIndex, bool isLast)
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
			BoxAttackId = 0,
			ComboIndex = comboIndex,
			IsLastComboHit = isLast
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
		TickFollowUpWindow(dt);
		TickListenerBoxes(dt);
		m_Effects.PhysicsTick(dt, this);
	}

	private void TickFollowUpWindow(float dt)
	{
		if (m_FollowUpRemaining > 0f)
		{
			m_FollowUpRemaining -= dt;
			if (m_FollowUpRemaining <= 0f)
			{
				BreakCombo();
			}
		}

		if (m_SkillCombos.Count == 0)
		{
			return;
		}

		var keys = new List<string>(m_SkillCombos.Keys);
		foreach (var key in keys)
		{
			var state = m_SkillCombos[key];
			if (state.FollowUpRemaining <= 0f)
			{
				continue;
			}

			state.FollowUpRemaining -= dt;
			if (state.FollowUpRemaining <= 0f)
			{
				m_SkillCombos.Remove(key);
			}
			else
			{
				m_SkillCombos[key] = state;
			}
		}
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
				if (!InstanceStillAlive(instance))
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

				if (instance.Kind == AttackKind.Basic)
				{
					if (play.IsLastComboHit || play.Spec == null || play.Spec.FollowUpWindow <= 0f)
					{
						BreakCombo();
					}
					else
					{
						m_ComboNextIndex = play.ComboIndex + 1;
						m_FollowUpRemaining = play.Spec.FollowUpWindow;
					}
				}
				else if (instance.Kind == AttackKind.Skill)
				{
					if (play.IsLastComboHit || play.Spec == null || play.Spec.FollowUpWindow <= 0f)
					{
						ClearSkillCombo(instance.ConfigId);
					}
					else
					{
						m_SkillCombos[instance.ConfigId] = new SkillComboState
						{
							NextIndex = play.ComboIndex + 1,
							FollowUpRemaining = play.Spec.FollowUpWindow
						};
					}
				}

				instance.PlayAttack = null;
				if (!InstanceStillAlive(instance))
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

	private void OnBasicAttackStarted(int attackId, int runtimeId)
	{
		_ = attackId;
		_ = runtimeId;
		FanOutStarted(subscribeSkill: false);
	}

	private void OnSkillAttackStarted(int attackId, int runtimeId)
	{
		_ = attackId;
		_ = runtimeId;
		FanOutStarted(subscribeSkill: true);
	}

	private void OnBasicAttackHit(int attackId, int runtimeId, HurtboxComponent hurtbox)
	{
		_ = attackId;
		_ = runtimeId;
		_ = hurtbox;
		foreach (var effect in m_Effects.SnapshotListeners())
		{
			if (effect.Target != m_Actor || !effect.Blueprint.SubscribeBasic)
			{
				continue;
			}

			TryAddCharge(effect);
		}
	}

	private void OnSkillAttackHit(int attackId, int runtimeId, HurtboxComponent hurtbox)
	{
		_ = attackId;
		_ = runtimeId;
		_ = hurtbox;
		foreach (var effect in m_Effects.SnapshotListeners())
		{
			if (effect.Target != m_Actor || !effect.Blueprint.SubscribeSkill)
			{
				continue;
			}

			TryAddCharge(effect);
		}
	}

	private void FanOutStarted(bool subscribeSkill)
	{
		foreach (var effect in m_Effects.SnapshotListeners())
		{
			if (effect.Target != m_Actor)
			{
				continue;
			}

			var listen = subscribeSkill ? effect.Blueprint.SubscribeSkill : effect.Blueprint.SubscribeBasic;
			if (!listen)
			{
				continue;
			}

			OpenListenerHitbox(effect);
		}
	}

	private void OpenListenerHitbox(EffectInstance effect)
	{
		var entry = effect.Blueprint.ExtraHitbox;
		if (entry == null)
		{
			return;
		}

		var attackId = m_NextAttackId;
		m_NextAttackId += 1;
		m_Strikes[attackId] = new StrikeInfo
		{
			RuntimeId = effect.SourceRuntimeId,
			Kind = AttackKind.Skill,
			FromListener = true
		};
		m_Hitbox.Activate(attackId, entry.Offset, entry.Size);
		m_ListenerBoxes.Add(new ListenerBox
		{
			AttackId = attackId,
			Remaining = Mathf.Max(0.01f, effect.Blueprint.ExtraHitboxDuration)
		});
	}

	private void TickListenerBoxes(float dt)
	{
		for (var i = m_ListenerBoxes.Count - 1; i >= 0; i--)
		{
			var box = m_ListenerBoxes[i];
			box.Remaining -= dt;
			if (box.Remaining <= 0f)
			{
				m_Hitbox.Deactivate(box.AttackId);
				m_Strikes.Remove(box.AttackId);
				m_ListenerBoxes.RemoveAt(i);
			}
			else
			{
				m_ListenerBoxes[i] = box;
			}
		}
	}

	private void CloseListenerBoxesForSource(uint sourceRuntimeId)
	{
		for (var i = m_ListenerBoxes.Count - 1; i >= 0; i--)
		{
			var box = m_ListenerBoxes[i];
			if (!m_Strikes.TryGetValue(box.AttackId, out var info) || info.RuntimeId != sourceRuntimeId)
			{
				continue;
			}

			m_Hitbox.Deactivate(box.AttackId);
			m_Strikes.Remove(box.AttackId);
			m_ListenerBoxes.RemoveAt(i);
		}
	}

	private void TryAddCharge(EffectInstance effect)
	{
		if (effect.Blueprint.ChargeMax <= 0 || effect.BurstConsumed)
		{
			return;
		}

		effect.Charge += 1;
		GD.Print($"CombatComponent: charge {effect.Charge}/{effect.Blueprint.ChargeMax} cfg={effect.SourceConfigId}");
		if (effect.Charge >= effect.Blueprint.ChargeMax)
		{
			HandleBurst(effect);
			m_Effects.RemoveBySourceRuntimeId(effect.SourceRuntimeId, expire: false);
			RemoveInstanceIfOrphan(effect.SourceRuntimeId);
		}
	}

	public void OnEffectExpired(EffectInstance effect)
	{
		if (!effect.BurstConsumed)
		{
			HandleBurst(effect);
		}
	}

	private void HandleBurst(EffectInstance effect)
	{
		effect.BurstConsumed = true;
		var damage = effect.Blueprint.BurstDamage;
		if (damage <= 0)
		{
			return;
		}

		var radius = effect.Blueprint.BurstRadius;
		foreach (var target in CollectTargets(SkillTargeting.EnemiesInRadius, radius))
		{
			target.Health?.TakeDamage(damage);
			GD.Print($"CombatComponent: burst hit {target.Name} dmg={damage}");
		}
	}

	private void RemoveInstanceIfOrphan(uint runtimeId)
	{
		for (var i = m_Instances.Count - 1; i >= 0; i--)
		{
			if (m_Instances[i].RuntimeId == runtimeId && !InstanceStillAlive(m_Instances[i]))
			{
				m_Instances.RemoveAt(i);
			}
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
			CloseListenerBoxesForSource(instance.RuntimeId);
			m_Effects.RemoveBySourceRuntimeId(instance.RuntimeId, expire: false);
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

	private SkillInstance FindAnyPlayAttack()
	{
		foreach (var instance in m_Instances)
		{
			if (instance.PlayAttack != null)
			{
				return instance;
			}
		}

		return null;
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

		if (expectedKind == AttackKind.Basic && def.HasGrantModules())
		{
			GD.PushError($"{GetPath()}: Job.Attack must not grant duration effects");
		}
	}

	public void ApplyModuleEffect(SkillInstance instance, GameplayEffect effect, bool toSelfOnly)
	{
		if (instance == null || effect == null || m_Actor == null)
		{
			return;
		}

		var targeting = toSelfOnly ? SkillTargeting.Self : instance.Definition.Targeting;
		var radius = instance.Definition.AreaRadius;
		if (targeting != SkillTargeting.Self && radius <= 0f)
		{
			GD.PushError($"{GetPath()}: AreaRadius must be > 0 for targeting {targeting}");
			return;
		}

		foreach (var target in CollectTargets(targeting, radius))
		{
			m_Effects.Apply(effect, target, instance.RuntimeId, instance.ConfigId);
		}
	}

	public void HandleEffectTick(EffectInstance effect)
	{
		if (effect.Blueprint.TickDamage <= 0 || effect.Target?.Health == null)
		{
			return;
		}

		effect.Target.Health.TakeDamage(effect.Blueprint.TickDamage);
	}

	private List<Actor> CollectTargets(SkillTargeting targeting, float radius)
	{
		var result = new List<Actor>();
		if (m_Actor == null)
		{
			return result;
		}

		if (targeting == SkillTargeting.Self)
		{
			result.Add(m_Actor);
			return result;
		}

		var selfTeam = m_Hitbox != null ? m_Hitbox.Team : CombatTeam.Player;
		if (!TryGetSelfLogicAabb(out var selfAabb))
		{
			return result;
		}

		if (targeting == SkillTargeting.AlliesInRadius || targeting == SkillTargeting.EveryoneInRadius)
		{
			result.Add(m_Actor);
		}

		foreach (var hurtbox in HurtboxRegistry.Snapshot())
		{
			if (hurtbox == null || !GodotObject.IsInstanceValid(hurtbox))
			{
				continue;
			}

			var target = hurtbox.GetOwnerActor();
			if (target == null || target == m_Actor)
			{
				continue;
			}

			if (!hurtbox.TryGetWorldAabb(out var theirAabb))
			{
				continue;
			}

			var dx = theirAabb.Center.X - selfAabb.Center.X;
			var depth = theirAabb.Center.Y - selfAabb.Center.Y;
			if (dx * dx + depth * depth > radius * radius)
			{
				continue;
			}

			if (Mathf.Abs(selfAabb.Center.Z - theirAabb.Center.Z) > selfAabb.HalfExtents.Z + theirAabb.HalfExtents.Z)
			{
				continue;
			}

			var sameTeam = hurtbox.Team == selfTeam;
			if (targeting == SkillTargeting.EnemiesInRadius && sameTeam)
			{
				continue;
			}

			if (targeting == SkillTargeting.AlliesInRadius && !sameTeam)
			{
				continue;
			}

			if (!result.Contains(target))
			{
				result.Add(target);
			}
		}

		return result;
	}

	private bool InstanceStillAlive(SkillInstance instance)
	{
		if (instance.PlayAttack != null)
		{
			return true;
		}

		foreach (var effect in m_Effects.Effects)
		{
			if (effect.SourceRuntimeId == instance.RuntimeId)
			{
				return true;
			}
		}

		return false;
	}

	private bool TryGetSelfLogicAabb(out LogicAabb aabb)
	{
		var hurtbox = GetNodeOrNull<HurtboxComponent>("../HurtboxComponent");
		if (hurtbox != null && hurtbox.TryGetWorldAabb(out aabb))
		{
			return true;
		}

		aabb = default;
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
		m_ListenerBoxes.Clear();
		m_Effects.RemoveAll(expire: false);
		BreakCombo();
		m_SkillCombos.Clear();
		m_CooldownRemaining.Clear();
	}
}
