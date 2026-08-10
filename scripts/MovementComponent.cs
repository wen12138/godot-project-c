using Godot;
using System;

public partial class MovementComponent : Node
{
	private TransformComponent m_TransformComponent;
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		m_TransformComponent = GetNode<TransformComponent>("TransformComponent");
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	public void SetMoveInput(Vector2 direction)
	{
	}

	public void Jump()
	{
	}
}
