using Godot;

/// <summary>
/// 调试绘制逻辑 AABB。贴地占位表达巷深（含朝屏幕上方的里巷），双立面表达高度，避免 Depth 与 Z 在屏幕 Y 上混成一块外接矩形。
/// </summary>
public static class CombatDebugDraw
{
	public static void DrawVolume(CanvasItem canvas, LogicAabb aabb, float actorLogicX, float actorLogicDepth, Color color)
	{
		var footprint = aabb.ToGroundFootprintRect(actorLogicX, actorLogicDepth);
		var backDepth = aabb.Center.Y - aabb.HalfExtents.Y;
		var frontDepth = aabb.Center.Y + aabb.HalfExtents.Y;
		var backFace = aabb.ToHeightFaceRect(actorLogicX, actorLogicDepth, backDepth);
		var frontFace = aabb.ToHeightFaceRect(actorLogicX, actorLogicDepth, frontDepth);

		var fill = new Color(color.R, color.G, color.B, 0.22f);
		var footprintFill = new Color(color.R, color.G, color.B, 0.4f);
		var backFill = new Color(color.R, color.G, color.B, 0.06f);

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

		canvas.DrawRect(backFace, backFill, filled: true);
		canvas.DrawRect(backFace, color, filled: false, width: 1f);
		canvas.DrawRect(frontFace, fill, filled: true);
		canvas.DrawRect(frontFace, color, filled: false, width: 2f);

		DrawFaceConnectors(canvas, backFace, frontFace, new Color(color.R, color.G, color.B, 0.85f));
	}

	private static void DrawFaceConnectors(CanvasItem canvas, Rect2 backFace, Rect2 frontFace, Color color)
	{
		var backTopLeft = backFace.Position;
		var backTopRight = backFace.Position + new Vector2(backFace.Size.X, 0f);
		var backBottomLeft = backFace.Position + new Vector2(0f, backFace.Size.Y);
		var backBottomRight = backFace.Position + backFace.Size;

		var frontTopLeft = frontFace.Position;
		var frontTopRight = frontFace.Position + new Vector2(frontFace.Size.X, 0f);
		var frontBottomLeft = frontFace.Position + new Vector2(0f, frontFace.Size.Y);
		var frontBottomRight = frontFace.Position + frontFace.Size;

		canvas.DrawLine(backTopLeft, frontTopLeft, color, width: 1.5f);
		canvas.DrawLine(backTopRight, frontTopRight, color, width: 1.5f);
		canvas.DrawLine(backBottomLeft, frontBottomLeft, color, width: 1.5f);
		canvas.DrawLine(backBottomRight, frontBottomRight, color, width: 1.5f);
	}
}
