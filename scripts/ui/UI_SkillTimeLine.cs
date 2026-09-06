using System.Collections.Generic;
using Godot;

public partial class UI_SkillTimeLine : VBoxContainer
{
	private const float BaseTimelineWidthPx = 200f;
	private const float MinVisibleSegmentPx = 3f;
	private const float MinSegmentDuration = 0.0001f;

	[Export]
	public PackedScene EntryTimeRect { get; set; }

	[ExportGroup("Timeline Colors")]
	[Export]
	public Color StartupColor { get; set; } = new(0.55f, 0.82f, 1f);

	[Export]
	public Color HitBoxColor { get; set; } = new(0.92f, 0.22f, 0.22f);

	[Export]
	public Color RecoveryColor { get; set; } = new(1f, 0.92f, 0.45f);

	[Export]
	public Color IdleColor { get; set; } = new(0.5f, 0.5f, 0.5f);

	[Export]
	public Color FollowUpColor { get; set; } = new(0.55f, 0.95f, 0.62f);

	[Export]
	public Color FillOverlayColor { get; set; } = new(1f, 1f, 1f, 0.28f);

	private Label m_SkillName;
	private MarginContainer m_Margin;
	private HBoxContainer m_TimeLineBox;
	private HBoxContainer m_MarkerBox;
	private ColorRect m_TimeFillRect;
	private float m_TrackWidthPx = BaseTimelineWidthPx;
	private uint m_BoundRuntimeId;
	private int m_BoundComboIndex = int.MinValue;
	private AttackSpec m_BoundSpec;
	private float m_BoundDisplayTotal;
	private bool m_BoundIsEffect;
	private int m_BoundApplyOrder = int.MinValue;
	private GameplayEffect m_BoundEffect;

	public override void _Ready()
	{
		m_SkillName = GetNodeOrNull<Label>("SkillName");
		m_Margin = GetNodeOrNull<MarginContainer>("MarginContainer");
		m_TimeLineBox = GetNodeOrNull<HBoxContainer>("MarginContainer/TimeLineBox");
		m_MarkerBox = GetNodeOrNull<HBoxContainer>("MarginContainer/MarkerBox");
		m_TimeFillRect = GetNodeOrNull<ColorRect>("MarginContainer/MarkerBox/TimeFillRect");
		ApplyFillOverlayStyle();
	}

	public void ShowSkillInstanceInfo(SkillInstance skillInstance, float followUpRemaining = 0f)
	{
		EnsureNodes();
		if (m_SkillName != null)
		{
			m_SkillName.Text = skillInstance?.ConfigId ?? string.Empty;
		}

		var play = skillInstance?.PlayAttack ?? skillInstance?.LastPlayAttack;
		if (play == null || play.Total <= 0f)
		{
			ResetBinding();
			return;
		}

		if (NeedsRebuild(skillInstance, play))
		{
			RebuildTimeline(play);
			m_BoundIsEffect = false;
			m_BoundRuntimeId = skillInstance.RuntimeId;
			m_BoundComboIndex = play.ComboIndex;
			m_BoundSpec = play.Spec;
			m_BoundDisplayTotal = GetDisplayTotal(play);
			m_BoundApplyOrder = int.MinValue;
			m_BoundEffect = null;
		}

		var displayTotal = m_BoundDisplayTotal > 0f ? m_BoundDisplayTotal : GetDisplayTotal(play);
		var elapsed = skillInstance.PlayAttack != null
			? skillInstance.PlayAttack.Elapsed
			: play.Total + Mathf.Max(0f, GetFollowUpDuration(play) - followUpRemaining);
		var fill = displayTotal > 0f
			? Mathf.Clamp(elapsed / displayTotal, 0f, 1f) * m_TrackWidthPx
			: 0f;
		SetFillWidth(fill);
	}

	public void ShowEffectInfo(EffectInstance effect)
	{
		EnsureNodes();
		if (m_SkillName != null)
		{
			m_SkillName.Text = string.IsNullOrEmpty(effect?.SourceConfigId)
				? "持续效果"
				: $"{effect.SourceConfigId} 持续";
		}

		var blueprint = effect?.Blueprint;
		if (blueprint == null || blueprint.Duration <= 0f)
		{
			ResetBinding();
			return;
		}

		if (NeedsRebuildEffect(effect))
		{
			RebuildEffectTimeline(blueprint);
			m_BoundIsEffect = true;
			m_BoundRuntimeId = effect.SourceRuntimeId;
			m_BoundApplyOrder = effect.ApplyOrder;
			m_BoundEffect = blueprint;
			m_BoundSpec = null;
			m_BoundComboIndex = int.MinValue;
			m_BoundDisplayTotal = blueprint.Duration;
		}

		var fill = Mathf.Clamp(effect.Elapsed / blueprint.Duration, 0f, 1f) * m_TrackWidthPx;
		SetFillWidth(fill);
	}

	private void EnsureNodes()
	{
		m_SkillName ??= GetNodeOrNull<Label>("SkillName");
		m_Margin ??= GetNodeOrNull<MarginContainer>("MarginContainer");
		m_TimeLineBox ??= GetNodeOrNull<HBoxContainer>("MarginContainer/TimeLineBox");
		m_MarkerBox ??= GetNodeOrNull<HBoxContainer>("MarginContainer/MarkerBox");
		m_TimeFillRect ??= GetNodeOrNull<ColorRect>("MarginContainer/MarkerBox/TimeFillRect");
		ApplyFillOverlayStyle();
	}

	private void ApplyFillOverlayStyle()
	{
		if (m_TimeFillRect == null)
		{
			return;
		}

		m_TimeFillRect.Color = FillOverlayColor;
		m_TimeFillRect.MouseFilter = MouseFilterEnum.Ignore;
		m_TimeFillRect.SizeFlagsHorizontal = 0;
	}

	private bool NeedsRebuild(SkillInstance skillInstance, PlayAttackState play)
	{
		return m_BoundIsEffect
			|| skillInstance.RuntimeId != m_BoundRuntimeId
			|| play.ComboIndex != m_BoundComboIndex
			|| play.Spec != m_BoundSpec;
	}

	private bool NeedsRebuildEffect(EffectInstance effect)
	{
		return !m_BoundIsEffect
			|| effect.SourceRuntimeId != m_BoundRuntimeId
			|| effect.ApplyOrder != m_BoundApplyOrder
			|| effect.Blueprint != m_BoundEffect;
	}

	private void RebuildTimeline(PlayAttackState play)
	{
		if (m_TimeLineBox == null)
		{
			return;
		}

		ClearTimeLineBoxes();
		var displayTotal = GetDisplayTotal(play);
		if (displayTotal <= 0f)
		{
			SetTrackWidth(BaseTimelineWidthPx);
			return;
		}

		var segments = BuildSegments(play, displayTotal);
		ApplySegments(segments, displayTotal);
	}

	private void RebuildEffectTimeline(GameplayEffect blueprint)
	{
		if (m_TimeLineBox == null)
		{
			return;
		}

		ClearTimeLineBoxes();
		var duration = blueprint.Duration;
		if (duration <= 0f)
		{
			SetTrackWidth(BaseTimelineWidthPx);
			return;
		}

		ApplySegments(BuildEffectSegments(blueprint), duration);
	}

	private void ApplySegments(List<TimeLineSegment> segments, float displayTotal)
	{
		if (segments == null || segments.Count == 0 || displayTotal <= 0f)
		{
			SetTrackWidth(BaseTimelineWidthPx);
			return;
		}

		var trackWidthPx = ResolveTrackWidth(segments, displayTotal);
		SetTrackWidth(trackWidthPx);
		var widths = ComputeSegmentWidths(segments, displayTotal, trackWidthPx);
		for (var i = 0; i < segments.Count; i++)
		{
			SpawnEntry(segments[i].Kind, widths[i]);
		}
	}

	private static float ResolveTrackWidth(List<TimeLineSegment> segments, float displayTotal)
	{
		var minDuration = float.MaxValue;
		foreach (var segment in segments)
		{
			if (segment.Duration > MinSegmentDuration && segment.Duration < minDuration)
			{
				minDuration = segment.Duration;
			}
		}

		if (minDuration >= float.MaxValue)
		{
			return BaseTimelineWidthPx;
		}

		var mappedPx = minDuration / displayTotal * BaseTimelineWidthPx;
		if (mappedPx >= MinVisibleSegmentPx)
		{
			return BaseTimelineWidthPx;
		}

		return Mathf.Ceil(MinVisibleSegmentPx * displayTotal / minDuration);
	}

	private void SetTrackWidth(float widthPx)
	{
		m_TrackWidthPx = widthPx;
		if (m_TimeLineBox != null)
		{
			m_TimeLineBox.CustomMinimumSize = new Vector2(widthPx, m_TimeLineBox.CustomMinimumSize.Y);
		}

		if (m_MarkerBox != null)
		{
			m_MarkerBox.CustomMinimumSize = new Vector2(widthPx, m_MarkerBox.CustomMinimumSize.Y);
		}

		var marginX = 0;
		if (m_Margin != null)
		{
			marginX = m_Margin.GetThemeConstant("margin_left") + m_Margin.GetThemeConstant("margin_right");
		}

		CustomMinimumSize = new Vector2(widthPx + marginX, CustomMinimumSize.Y);
	}

	private static float[] ComputeSegmentWidths(
		List<TimeLineSegment> segments,
		float displayTotal,
		float trackWidthPx)
	{
		var widths = new float[segments.Count];
		for (var i = 0; i < segments.Count; i++)
		{
			widths[i] = Mathf.Max(0f, segments[i].Duration / displayTotal * trackWidthPx);
		}

		return SnapWidthsToPixels(widths, trackWidthPx);
	}

	private static float[] SnapWidthsToPixels(float[] widths, float trackWidthPx)
	{
		var n = widths.Length;
		var floors = new int[n];
		var order = new int[n];
		var floorSum = 0;
		for (var i = 0; i < n; i++)
		{
			widths[i] = Mathf.Max(0f, widths[i]);
			floors[i] = Mathf.FloorToInt(widths[i]);
			floorSum += floors[i];
			order[i] = i;
		}

		System.Array.Sort(order, (a, b) =>
		{
			var fa = widths[a] - floors[a];
			var fb = widths[b] - floors[b];
			var cmp = fb.CompareTo(fa);
			return cmp != 0 ? cmp : a.CompareTo(b);
		});

		var leftover = Mathf.RoundToInt(trackWidthPx) - floorSum;
		if (leftover > 0)
		{
			for (var k = 0; k < leftover && k < n; k++)
			{
				floors[order[k]] += 1;
			}
		}
		else
		{
			for (var k = n - 1; leftover < 0 && k >= 0; k--)
			{
				var i = order[k];
				if (floors[i] <= 0)
				{
					continue;
				}

				floors[i] -= 1;
				leftover += 1;
			}
		}

		var pixels = new float[n];
		for (var i = 0; i < n; i++)
		{
			pixels[i] = floors[i];
		}

		return pixels;
	}

	private void SpawnEntry(TimeLineKind kind, float widthPx)
	{
		if (widthPx <= 0f)
		{
			return;
		}

		ColorRect rect;
		if (EntryTimeRect != null)
		{
			var instance = EntryTimeRect.Instantiate();
			if (instance is not ColorRect packed)
			{
				GD.PushError($"{GetPath()}: EntryTimeRect root is not ColorRect");
				instance.QueueFree();
				return;
			}

			rect = packed;
		}
		else
		{
			rect = new ColorRect();
		}

		rect.Color = ColorFor(kind);
		rect.MouseFilter = MouseFilterEnum.Ignore;
		rect.SizeFlagsHorizontal = 0;
		rect.SizeFlagsVertical = SizeFlags.Fill;
		rect.CustomMinimumSize = new Vector2(widthPx, rect.CustomMinimumSize.Y);
		m_TimeLineBox.AddChild(rect);
	}

	public void ResetBinding()
	{
		if (m_BoundRuntimeId == 0
			&& m_BoundComboIndex == int.MinValue
			&& m_BoundSpec == null
			&& m_BoundDisplayTotal == 0f
			&& (m_TimeLineBox == null || m_TimeLineBox.GetChildCount() == 0))
		{
			SetTrackWidth(BaseTimelineWidthPx);
			SetFillWidth(0f);
			return;
		}

		m_BoundRuntimeId = 0;
		m_BoundComboIndex = int.MinValue;
		m_BoundSpec = null;
		m_BoundDisplayTotal = 0f;
		m_BoundIsEffect = false;
		m_BoundApplyOrder = int.MinValue;
		m_BoundEffect = null;
		ClearTimeLineBoxes();
		SetTrackWidth(BaseTimelineWidthPx);
		SetFillWidth(0f);
	}

	private void ClearTimeLineBoxes()
	{
		if (m_TimeLineBox == null)
		{
			return;
		}

		var children = m_TimeLineBox.GetChildren();
		foreach (var child in children)
		{
			m_TimeLineBox.RemoveChild(child);
			child.Free();
		}
	}

	private void SetFillWidth(float widthPx)
	{
		if (m_TimeFillRect == null)
		{
			return;
		}

		m_TimeFillRect.CustomMinimumSize = new Vector2(widthPx, m_TimeFillRect.CustomMinimumSize.Y);
	}

	private static float GetFollowUpDuration(PlayAttackState play)
	{
		if (play.IsLastComboHit || play.Spec == null)
		{
			return 0f;
		}

		return Mathf.Max(0f, play.Spec.FollowUpWindow);
	}

	private static float GetDisplayTotal(PlayAttackState play)
	{
		return play.Total + GetFollowUpDuration(play);
	}

	private static List<TimeLineSegment> BuildEffectSegments(GameplayEffect blueprint)
	{
		var duration = blueprint.Duration;
		var cuts = new List<float> { 0f };
		var hasHitbox = blueprint.ExtraHitbox != null;
		var period = blueprint.Period;
		var boxDuration = hasHitbox ? Mathf.Max(0.01f, blueprint.ExtraHitboxDuration) : 0f;
		if (hasHitbox && period > MinSegmentDuration && boxDuration > 0f)
		{
			var tickAt = period;
			var windows = 0;
			while (tickAt < duration && windows < 128)
			{
				cuts.Add(tickAt);
				cuts.Add(Mathf.Min(tickAt + boxDuration, duration));
				tickAt += period;
				windows += 1;
			}
		}

		cuts.Sort();
		var unique = new List<float>(cuts.Count);
		foreach (var cut in cuts)
		{
			var clamped = Mathf.Clamp(cut, 0f, duration);
			if (unique.Count == 0 || clamped - unique[^1] > MinSegmentDuration)
			{
				unique.Add(clamped);
			}
		}

		var segments = new List<TimeLineSegment>();
		for (var i = 0; i < unique.Count - 1; i++)
		{
			var start = unique[i];
			var end = unique[i + 1];
			var segmentDuration = end - start;
			if (segmentDuration <= MinSegmentDuration)
			{
				continue;
			}

			var kind = ClassifyEffect(start + segmentDuration * 0.5f, period, boxDuration, hasHitbox);
			if (segments.Count > 0 && segments[^1].Kind == kind)
			{
				segments[^1] = new TimeLineSegment(kind, segments[^1].Duration + segmentDuration);
			}
			else
			{
				segments.Add(new TimeLineSegment(kind, segmentDuration));
			}
		}

		return segments;
	}

	private static TimeLineKind ClassifyEffect(float time, float period, float boxDuration, bool hasHitbox)
	{
		if (!hasHitbox || period <= MinSegmentDuration || boxDuration <= 0f)
		{
			return TimeLineKind.Idle;
		}

		var tickIndex = Mathf.FloorToInt((time + MinSegmentDuration) / period);
		if (tickIndex < 1)
		{
			return TimeLineKind.Idle;
		}

		var windowStart = tickIndex * period;
		if (time >= windowStart && time < windowStart + boxDuration)
		{
			return TimeLineKind.HitBox;
		}

		return TimeLineKind.Idle;
	}

	private static List<TimeLineSegment> BuildSegments(PlayAttackState play, float displayTotal)
	{
		var playEnd = play.Total;
		var startupEnd = Mathf.Max(0f, play.Spec?.Startup ?? 0f);
		var recoveryStart = startupEnd + Mathf.Max(0f, play.Spec?.Active ?? 0f);
		startupEnd = Mathf.Clamp(startupEnd, 0f, playEnd);
		recoveryStart = Mathf.Clamp(recoveryStart, 0f, playEnd);

		var cuts = new List<float> { 0f, playEnd, displayTotal, startupEnd, recoveryStart };
		if (play.Boxes != null)
		{
			foreach (var box in play.Boxes)
			{
				if (box == null)
				{
					continue;
				}

				cuts.Add(Mathf.Clamp(box.WindowStart, 0f, playEnd));
				cuts.Add(Mathf.Clamp(box.WindowEnd, 0f, playEnd));
			}
		}

		cuts.Sort();
		var unique = new List<float>(cuts.Count);
		foreach (var cut in cuts)
		{
			var clamped = Mathf.Clamp(cut, 0f, displayTotal);
			if (unique.Count == 0 || clamped - unique[^1] > MinSegmentDuration)
			{
				unique.Add(clamped);
			}
		}

		var segments = new List<TimeLineSegment>();
		for (var i = 0; i < unique.Count - 1; i++)
		{
			var start = unique[i];
			var end = unique[i + 1];
			var duration = end - start;
			if (duration <= MinSegmentDuration)
			{
				continue;
			}

			var kind = Classify(start + duration * 0.5f, startupEnd, recoveryStart, playEnd, play.Boxes);
			if (segments.Count > 0 && segments[^1].Kind == kind)
			{
				segments[^1] = new TimeLineSegment(kind, segments[^1].Duration + duration);
			}
			else
			{
				segments.Add(new TimeLineSegment(kind, duration));
			}
		}

		return segments;
	}

	private static TimeLineKind Classify(
		float time,
		float startupEnd,
		float recoveryStart,
		float playEnd,
		List<PlayBoxState> boxes)
	{
		if (time >= playEnd)
		{
			return TimeLineKind.FollowUp;
		}

		if (boxes != null)
		{
			foreach (var box in boxes)
			{
				if (box != null && time >= box.WindowStart && time < box.WindowEnd)
				{
					return TimeLineKind.HitBox;
				}
			}
		}

		if (time < startupEnd)
		{
			return TimeLineKind.Startup;
		}

		if (time >= recoveryStart)
		{
			return TimeLineKind.Recovery;
		}

		return TimeLineKind.Idle;
	}

	private Color ColorFor(TimeLineKind kind)
	{
		return kind switch
		{
			TimeLineKind.Startup => StartupColor,
			TimeLineKind.HitBox => HitBoxColor,
			TimeLineKind.Recovery => RecoveryColor,
			TimeLineKind.FollowUp => FollowUpColor,
			_ => IdleColor
		};
	}

	private enum TimeLineKind
	{
		Idle,
		Startup,
		HitBox,
		Recovery,
		FollowUp
	}

	private readonly struct TimeLineSegment
	{
		public readonly TimeLineKind Kind;
		public readonly float Duration;

		public TimeLineSegment(TimeLineKind kind, float duration)
		{
			Kind = kind;
			Duration = duration;
		}
	}
}
