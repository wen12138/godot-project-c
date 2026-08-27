using Godot;

/// <summary>
/// 逻辑空间轴对齐盒。Vector3 含义：X=LogicX，Y=LogicDepth，Z=VirtualZ。
/// 重叠核对（半伸展已写出）：
/// 1. 攻击 (48,0,36)±(36,14,36) vs 受击 (80,0,36)±(18,12,36) → 命中
/// 2. 受击改为 (80,40,36)±(18,12,36) → 深度失败
/// 3. 受击改为 (80,0,117)±(18,12,36) → 高度失败
/// </summary>
public readonly struct LogicAabb
{
	public readonly Vector3 Center;
	public readonly Vector3 HalfExtents;

	public LogicAabb(Vector3 center, Vector3 halfExtents)
	{
		Center = center;
		HalfExtents = halfExtents;
	}

	public static LogicAabb FromCenterSize(Vector3 center, Vector3 size)
	{
		if (size.X <= 0f || size.Y <= 0f || size.Z <= 0f)
		{
			return new LogicAabb(center, Vector3.Zero);
		}

		return new LogicAabb(center, size * 0.5f);
	}

	public bool HasVolume => HalfExtents.X > 0f && HalfExtents.Y > 0f && HalfExtents.Z > 0f;

	public bool Overlaps(in LogicAabb other)
	{
		return Mathf.Abs(Center.X - other.Center.X) <= HalfExtents.X + other.HalfExtents.X
			&& Mathf.Abs(Center.Y - other.Center.Y) <= HalfExtents.Y + other.HalfExtents.Y
			&& Mathf.Abs(Center.Z - other.Center.Z) <= HalfExtents.Z + other.HalfExtents.Z;
	}

	public static Vector3 ApplyFacingOffset(Vector3 offset, ActorFacing facing)
	{
		return facing == ActorFacing.Left
			? new Vector3(-offset.X, offset.Y, offset.Z)
			: offset;
	}

	public Rect2 ToActorLocalRect(float actorLogicX, float actorLogicDepth)
	{
		var minX = Center.X - HalfExtents.X - actorLogicX;
		var maxX = Center.X + HalfExtents.X - actorLogicX;
		var minY = (Center.Y - HalfExtents.Y - actorLogicDepth) * MapCoordinates.DepthToScreenY
			- (Center.Z + HalfExtents.Z) * MapCoordinates.HeightToScreenY;
		var maxY = (Center.Y + HalfExtents.Y - actorLogicDepth) * MapCoordinates.DepthToScreenY
			- (Center.Z - HalfExtents.Z) * MapCoordinates.HeightToScreenY;
		return new Rect2(minX, minY, maxX - minX, maxY - minY);
	}

	/// <summary>
	/// 贴地占位：只投影 X 与 LogicDepth，不含高度。minY 是朝屏幕上方（里巷）的深度边。
	/// </summary>
	public Rect2 ToGroundFootprintRect(float actorLogicX, float actorLogicDepth)
	{
		var minX = Center.X - HalfExtents.X - actorLogicX;
		var maxX = Center.X + HalfExtents.X - actorLogicX;
		var minY = (Center.Y - HalfExtents.Y - actorLogicDepth) * MapCoordinates.DepthToScreenY;
		var maxY = (Center.Y + HalfExtents.Y - actorLogicDepth) * MapCoordinates.DepthToScreenY;
		return new Rect2(minX, minY, maxX - minX, maxY - minY);
	}

	/// <summary>
	/// 指定逻辑深度处的 X-Z 立面（侧视高度盒）。
	/// </summary>
	public Rect2 ToHeightFaceRect(float actorLogicX, float actorLogicDepth, float logicDepth)
	{
		var minX = Center.X - HalfExtents.X - actorLogicX;
		var maxX = Center.X + HalfExtents.X - actorLogicX;
		var screenY = (logicDepth - actorLogicDepth) * MapCoordinates.DepthToScreenY;
		var minY = screenY - (Center.Z + HalfExtents.Z) * MapCoordinates.HeightToScreenY;
		var maxY = screenY - (Center.Z - HalfExtents.Z) * MapCoordinates.HeightToScreenY;
		return new Rect2(minX, minY, maxX - minX, maxY - minY);
	}
}
