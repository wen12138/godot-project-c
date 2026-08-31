using System.Collections.Generic;
using Godot;

public sealed class EffectInstance
{
	public GameplayEffect Blueprint;
	public Actor Target;
	public uint SourceRuntimeId;
	public string SourceConfigId;
	public int ApplyOrder;
	public float Elapsed;
	public float TickAccum;
	public int Charge;
	public bool BurstConsumed;
}

public sealed class EffectHolder
{
	private readonly List<EffectInstance> m_Effects = new();
	private int m_NextApplyOrder = 1;

	public IReadOnlyList<EffectInstance> Effects => m_Effects;

	public EffectInstance Apply(GameplayEffect blueprint, Actor target, uint sourceRuntimeId, string sourceConfigId)
	{
		if (blueprint == null || target == null)
		{
			return null;
		}

		var instance = new EffectInstance
		{
			Blueprint = blueprint,
			Target = target,
			SourceRuntimeId = sourceRuntimeId,
			SourceConfigId = sourceConfigId,
			ApplyOrder = m_NextApplyOrder
		};
		m_NextApplyOrder += 1;
		m_Effects.Add(instance);
		GD.Print($"EffectHolder: apply src={sourceRuntimeId} cfg={sourceConfigId} dur={blueprint.Duration} -> {target.Name}");
		return instance;
	}

	public void RemoveBySourceRuntimeId(uint sourceRuntimeId, bool expire)
	{
		for (var i = m_Effects.Count - 1; i >= 0; i--)
		{
			if (m_Effects[i].SourceRuntimeId == sourceRuntimeId)
			{
				RemoveAt(i, expire);
			}
		}
	}

	public void RemoveAll(bool expire)
	{
		for (var i = m_Effects.Count - 1; i >= 0; i--)
		{
			RemoveAt(i, expire);
		}
	}

	public void PhysicsTick(float dt, CombatComponent combat)
	{
		for (var i = m_Effects.Count - 1; i >= 0; i--)
		{
			var effect = m_Effects[i];
			if (effect.Blueprint.Duration <= 0f)
			{
				RemoveAt(i, expire: false);
				continue;
			}

			effect.Elapsed += dt;
			if (effect.Blueprint.Period > 0f)
			{
				effect.TickAccum += dt;
				if (effect.TickAccum >= effect.Blueprint.Period)
				{
					effect.TickAccum -= effect.Blueprint.Period;
					combat.HandleEffectTick(effect);
				}
			}

			if (effect.Elapsed >= effect.Blueprint.Duration)
			{
				if (combat != null)
				{
					combat.OnEffectExpired(effect);
				}

				RemoveAt(i, expire: true);
			}
		}
	}

	public List<EffectInstance> SnapshotListeners()
	{
		var list = new List<EffectInstance>();
		foreach (var effect in m_Effects)
		{
			list.Add(effect);
		}

		list.Sort((a, b) =>
		{
			var cmp = a.ApplyOrder.CompareTo(b.ApplyOrder);
			if (cmp != 0)
			{
				return cmp;
			}

			return a.SourceRuntimeId.CompareTo(b.SourceRuntimeId);
		});
		return list;
	}

	private void RemoveAt(int index, bool expire)
	{
		var effect = m_Effects[index];
		m_Effects.RemoveAt(index);
		if (expire)
		{
			GD.Print($"EffectHolder: expire src={effect.SourceRuntimeId} cfg={effect.SourceConfigId}");
		}
		else
		{
			GD.Print($"EffectHolder: remove src={effect.SourceRuntimeId} cfg={effect.SourceConfigId}");
		}
	}
}
