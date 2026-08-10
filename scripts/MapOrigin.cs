using Godot;

public partial class MapOrigin : Marker2D
{
	public override void _EnterTree()
	{
		if (MapContext.Instance == null)
		{
			GD.PushError($"{GetPath()}: MapContext.Instance 为空，无法注册原点");
			return;
		}

		MapContext.Instance.RegisterOrigin(this);
	}

	public override void _ExitTree()
	{
		if (MapContext.Instance == null)
		{
			return;
		}

		if (MapContext.Instance.HasOrigin && MapContext.Instance.Origin == this)
		{
			MapContext.Instance.ClearOrigin();
		}
	}
}
