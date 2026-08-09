using System;
using Godot;

public partial class TransformComponent : Node
{
	protected float m_VisualX;
	protected float m_VisualY;
	protected float m_LogicX;
	protected float m_LogicDepth;
	protected float m_VirtualZ;

	private const float DepthToScreenY = 0.5f;
	private const float HeightToScreenY = 1.0f;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	public override void _PhysicsProcess(double delta)
	{

	}


	public virtual void SetLogicDepth(float depth)
	{
		m_LogicDepth = depth;
		UpdateVisualPosition();
	}

	public virtual void SetLogicX(float x)
	{
		m_LogicX = x;
		UpdateVisualPosition();
	}

	public virtual void SetVirtualZ(float height)
	{
		m_VirtualZ = height;
		UpdateVisualPosition();
	}

	protected virtual void UpdateVisualPosition()
	{
		m_VisualX = m_LogicX;
		m_VisualY = m_LogicDepth * DepthToScreenY - m_VirtualZ * HeightToScreenY;
	}

	public virtual float GetVisualX()
	{
		return m_VisualX;
	}

	public virtual float GetVisualY()
	{
		return m_VisualY;
	}

	public virtual float GetLogicDepth()
	{
		return m_LogicDepth;
	}
}
