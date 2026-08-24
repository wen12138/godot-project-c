using System.Collections.Generic;
using Godot;

public static class HurtboxRegistry
{
	private static readonly HashSet<HurtboxComponent> Boxes = new();

	public static void Register(HurtboxComponent hurtbox)
	{
		if (hurtbox == null)
		{
			GD.PushError("HurtboxRegistry.Register: hurtbox 为 null");
			return;
		}

		Boxes.Add(hurtbox);
	}

	public static void Unregister(HurtboxComponent hurtbox)
	{
		if (hurtbox == null)
		{
			return;
		}

		Boxes.Remove(hurtbox);
	}

	public static List<HurtboxComponent> Snapshot()
	{
		return new List<HurtboxComponent>(Boxes);
	}
}
