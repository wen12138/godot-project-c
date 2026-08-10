using Godot;

public partial class MapContext : Node
{
	public static MapContext Instance { get; private set; }

	private Node2D m_Origin;

	public bool HasOrigin => m_Origin != null && GodotObject.IsInstanceValid(m_Origin);

	public Node2D Origin
	{
		get
		{
			if (!HasOrigin)
			{
				GD.PushError("MapContext: Origin 尚未注册");
				return null;
			}

			return m_Origin;
		}
	}

	public override void _EnterTree()
	{
		Instance = this;
	}

	public override void _ExitTree()
	{
		if (Instance == this)
		{
			Instance = null;
		}
	}

	public void RegisterOrigin(Node2D origin)
	{
		if (origin == null)
		{
			GD.PushError("MapContext.RegisterOrigin: origin 为 null");
			return;
		}

		m_Origin = origin;
	}

	public void ClearOrigin()
	{
		m_Origin = null;
	}
}
