using Godot;

public static class InputActions
{
	public const string MoveUp = "move_up";
	public const string MoveDown = "move_down";
	public const string MoveLeft = "move_left";
	public const string MoveRight = "move_right";
	public const string Jump = "jump";

	public static Vector2 GetMoveVector()
	{
		return Input.GetVector(MoveLeft, MoveRight, MoveUp, MoveDown);
	}

	public static bool IsJumpJustPressed()
	{
		return Input.IsActionJustPressed(Jump);
	}
}
