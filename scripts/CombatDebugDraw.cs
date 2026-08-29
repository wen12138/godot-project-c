using Godot;

/// <summary>
/// 调试绘制逻辑 AABB。贴地占位只表达巷深；XZ 立面只表达高度，画在盒子中心深度，避免 Depth 再次叠进立面的上下沿。
/// </summary>
public static class CombatDebugDraw
{
	public static void DrawVolume(CanvasItem canvas, LogicAabb aabb, float actorLogicX, float actorLogicDepth, Color color)
	{
		var footprint = aabb.ToGroundFootprintRect(actorLogicX, actorLogicDepth);
		var heightFace = aabb.ToHeightFaceRect(actorLogicX, actorLogicDepth, aabb.Center.Y);

		var fill = new Color(color.R, color.G, color.B, 0.22f);
		var footprintFill = new Color(color.R, color.G, color.B, 0.4f);

		canvas.DrawRect(footprint, footprintFill, filled: true);
		canvas.DrawRect(footprint, color, filled: false, width: 2f);

		var backLeft = footprint.Position;
		var backRight = footprint.Position + new Vector2(footprint.Size.X, 0f);
		canvas.DrawLine(backLeft, backRight, color, width: 4f);

		var mid = (backLeft + backRight) * 0.5f;
		canvas.DrawColoredPolygon(
			new Vector2[]
			{
				mid + new Vector2(0f, -10f),
				mid + new Vector2(6f, 0f),
				mid + new Vector2(-6f, 0f)
			},
			color);

		canvas.DrawRect(heightFace, fill, filled: true);
		canvas.DrawRect(heightFace, color, filled: false, width: 2f);
	}
}
