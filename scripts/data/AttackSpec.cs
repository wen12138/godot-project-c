using Godot;

[GlobalClass]
public partial class AttackSpec : Resource
{
	[Export]
	public float Startup { get; set; }

	[Export]
	public float Active { get; set; } = 0.2f;

	[Export]
	public float Recovery { get; set; }

	[Export]
	public float CancelOpenAt { get; set; } = -1f;

	[Export]
	public Godot.Collections.Array<HitboxEntry> Hitboxes { get; set; } = new();

	public float TotalDuration => Mathf.Max(0f, Startup) + Mathf.Max(0f, Active) + Mathf.Max(0f, Recovery);

	public bool TryResolveWindow(HitboxEntry entry, out float start, out float end)
	{
		start = 0f;
		end = 0f;
		if (entry == null)
		{
			return false;
		}

		start = entry.Start >= 0f ? entry.Start : Startup;
		end = entry.End >= 0f ? entry.End : Startup + Active;
		return end > start;
	}
}
