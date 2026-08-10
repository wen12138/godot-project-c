using Godot;

public static class MapCoordinates
{
	public const float DepthToScreenY = 0.5f;
	public const float HeightToScreenY = 1.0f;

	public static Vector2 LogicToWorld(Vector2 origin, float logicX, float logicDepth, float virtualZ = 0f)
	{
		return origin + new Vector2(
			logicX,
			logicDepth * DepthToScreenY - virtualZ * HeightToScreenY);
	}

	public static void WorldToLogicGround(Vector2 origin, Vector2 world, out float logicX, out float logicDepth)
	{
		var local = world - origin;
		logicX = local.X;
		logicDepth = local.Y / DepthToScreenY;
	}

	public static Vector2 VirtualZScreenOffset(float virtualZ)
	{
		return new Vector2(0f, -virtualZ * HeightToScreenY);
	}
}
