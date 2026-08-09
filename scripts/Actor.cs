using Godot;
using System;

public partial class Actor : Node2D
{
	private TransformComponent m_TransformComponent;
	private MovementComponent m_MovementComponent;
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		m_TransformComponent = GetNode<TransformComponent>("TransformComponent");
		m_MovementComponent = GetNode<MovementComponent>("MovementComponent");
	}

	public override void _PhysicsProcess(double delta)
	{
		
	}
}
