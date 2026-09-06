using System;
using System.Collections.Generic;
using Godot;

public partial class UI_SkillDebug : Control
{
	[Export]
	public PackedScene TimeLinePrefab { get; set; }

	[Export]
	public NodePath PlayerPath { get; set; }

	private Player m_Player;
	private VBoxContainer m_TimeLineVBox;
	private readonly Dictionary<TimelineKey, UI_SkillTimeLine> m_TimeLines = new();
	private readonly List<UI_SkillTimeLine> m_TimeLinePool = new();
	private readonly HashSet<TimelineKey> m_LiveKeys = new();
	private readonly List<TimelineKey> m_StaleKeys = new();

	public override void _Ready()
	{
		m_TimeLineVBox = GetNodeOrNull<VBoxContainer>("TimeLineVBox");
		TryBindPlayer();
	}

	public void SetPlayer(Player player)
	{
		if (m_Player == player)
		{
			return;
		}

		m_Player = player;
		ReleaseAllTimeLines();
	}

	public override void _PhysicsProcess(double delta)
	{
		TryBindPlayer();
		if (m_Player?.Combat == null || m_TimeLineVBox == null)
		{
			return;
		}

		SyncTimeLines();
	}

	private void TryBindPlayer()
	{
		if (GodotObject.IsInstanceValid(m_Player))
		{
			return;
		}

		m_Player = null;
		if (PlayerPath != null && !PlayerPath.IsEmpty)
		{
			m_Player = GetNodeOrNull<Player>(PlayerPath);
		}

		if (m_Player == null)
		{
			m_Player = GetTree()?.Root?.FindChild("Player", recursive: true, owned: false) as Player;
		}
	}

	private void SyncTimeLines()
	{
		m_LiveKeys.Clear();
		var order = 0;
		var combat = m_Player.Combat;
		var skillInstances = combat.GetSkillInstances();
		if (skillInstances != null)
		{
			foreach (var instance in skillInstances)
			{
				if (!ShouldShowPlay(instance))
				{
					continue;
				}

				order = ShowPlay(instance, order);
			}
		}

		var effects = combat.GetEffects();
		if (effects != null)
		{
			foreach (var effect in effects)
			{
				if (!ShouldShowEffect(effect))
				{
					continue;
				}

				order = ShowEffect(effect, order);
			}
		}

		CollectStaleKeys();
		foreach (var key in m_StaleKeys)
		{
			ReleaseTimeLine(key);
		}

		FitTimeLineVBoxWidth();
	}

	private void FitTimeLineVBoxWidth()
	{
		if (m_TimeLineVBox == null)
		{
			return;
		}

		var width = 0f;
		foreach (var child in m_TimeLineVBox.GetChildren())
		{
			if (child is not Control control || !control.Visible)
			{
				continue;
			}

			width = Mathf.Max(width, control.GetCombinedMinimumSize().X);
		}

		if (width <= 0f)
		{
			width = 205f;
		}

		m_TimeLineVBox.OffsetLeft = -width * 0.5f;
		m_TimeLineVBox.OffsetRight = width * 0.5f;
	}

	private int ShowPlay(SkillInstance instance, int order)
	{
		var key = TimelineKey.ForPlay(instance.RuntimeId);
		m_LiveKeys.Add(key);
		var timeLine = GetOrAcquireTimeLine(key);
		if (timeLine == null)
		{
			return order;
		}

		MoveToOrder(timeLine, order);
		timeLine.ShowSkillInstanceInfo(instance, m_Player.Combat.GetFollowUpRemaining(instance));
		return order + 1;
	}

	private int ShowEffect(EffectInstance effect, int order)
	{
		var key = TimelineKey.ForEffect(effect.SourceRuntimeId, effect.ApplyOrder);
		m_LiveKeys.Add(key);
		var timeLine = GetOrAcquireTimeLine(key);
		if (timeLine == null)
		{
			return order;
		}

		MoveToOrder(timeLine, order);
		timeLine.ShowEffectInfo(effect);
		return order + 1;
	}

	private void MoveToOrder(UI_SkillTimeLine timeLine, int order)
	{
		if (timeLine.GetIndex() != order)
		{
			m_TimeLineVBox.MoveChild(timeLine, order);
		}
	}

	private bool ShouldShowPlay(SkillInstance instance)
	{
		if (instance == null || instance.RuntimeId == 0)
		{
			return false;
		}

		if (instance.PlayAttack != null && instance.PlayAttack.Total > 0f)
		{
			return true;
		}

		return instance.LastPlayAttack != null
			&& instance.LastPlayAttack.Total > 0f
			&& m_Player.Combat.GetFollowUpRemaining(instance) > 0f;
	}

	private static bool ShouldShowEffect(EffectInstance effect)
	{
		return effect?.Blueprint != null && effect.Blueprint.Duration > 0f;
	}

	private UI_SkillTimeLine GetOrAcquireTimeLine(TimelineKey key)
	{
		if (m_TimeLines.TryGetValue(key, out var timeLine)
			&& GodotObject.IsInstanceValid(timeLine))
		{
			return timeLine;
		}

		timeLine = AcquireTimeLine();
		if (timeLine == null)
		{
			return null;
		}

		m_TimeLines[key] = timeLine;
		return timeLine;
	}

	private UI_SkillTimeLine AcquireTimeLine()
	{
		if (m_TimeLinePool.Count > 0)
		{
			var index = m_TimeLinePool.Count - 1;
			var pooled = m_TimeLinePool[index];
			m_TimeLinePool.RemoveAt(index);
			if (GodotObject.IsInstanceValid(pooled))
			{
				pooled.Visible = true;
				return pooled;
			}
		}

		var spawned = SpawnTimeLine();
		if (spawned == null)
		{
			return null;
		}

		m_TimeLineVBox.AddChild(spawned);
		return spawned;
	}

	private void CollectStaleKeys()
	{
		m_StaleKeys.Clear();
		foreach (var key in m_TimeLines.Keys)
		{
			if (!m_LiveKeys.Contains(key))
			{
				m_StaleKeys.Add(key);
			}
		}
	}

	private void ReleaseTimeLine(TimelineKey key)
	{
		if (!m_TimeLines.Remove(key, out var timeLine))
		{
			return;
		}

		if (!GodotObject.IsInstanceValid(timeLine))
		{
			return;
		}

		timeLine.ResetBinding();
		timeLine.Visible = false;
		m_TimeLinePool.Add(timeLine);
	}

	private void ReleaseAllTimeLines()
	{
		m_StaleKeys.Clear();
		m_StaleKeys.AddRange(m_TimeLines.Keys);
		foreach (var key in m_StaleKeys)
		{
			ReleaseTimeLine(key);
		}

		m_LiveKeys.Clear();
	}

	private UI_SkillTimeLine SpawnTimeLine()
	{
		if (TimeLinePrefab == null)
		{
			GD.PushError($"{GetPath()}: TimeLinePrefab is null");
			return null;
		}

		var instance = TimeLinePrefab.Instantiate();
		if (instance is not UI_SkillTimeLine timeLine)
		{
			GD.PushError($"{GetPath()}: TimeLinePrefab root is not UI_SkillTimeLine");
			instance.QueueFree();
			return null;
		}

		return timeLine;
	}

	private readonly struct TimelineKey : IEquatable<TimelineKey>
	{
		public readonly bool IsEffect;
		public readonly uint RuntimeId;
		public readonly int ApplyOrder;

		private TimelineKey(bool isEffect, uint runtimeId, int applyOrder)
		{
			IsEffect = isEffect;
			RuntimeId = runtimeId;
			ApplyOrder = applyOrder;
		}

		public static TimelineKey ForPlay(uint runtimeId)
		{
			return new TimelineKey(false, runtimeId, 0);
		}

		public static TimelineKey ForEffect(uint runtimeId, int applyOrder)
		{
			return new TimelineKey(true, runtimeId, applyOrder);
		}

		public bool Equals(TimelineKey other)
		{
			return IsEffect == other.IsEffect
				&& RuntimeId == other.RuntimeId
				&& ApplyOrder == other.ApplyOrder;
		}

		public override bool Equals(object obj)
		{
			return obj is TimelineKey other && Equals(other);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(IsEffect, RuntimeId, ApplyOrder);
		}
	}
}
