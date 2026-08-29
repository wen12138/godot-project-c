using Godot;

public partial class Actor : Node2D
{
	[Export]
	public ActorDefinition Definition { get; set; }

	private CombatAttributes m_RuntimeAttributes;
	private TransformComponent m_TransformComponent;
	private MovementComponent m_MovementComponent;
	private HealthComponent m_HealthComponent;
	private CombatComponent m_CombatComponent;
	private HitboxComponent m_HitboxComponent;
	private HurtboxComponent m_HurtboxComponent;

	public MovementComponent Movement => m_MovementComponent;

	public HealthComponent Health => m_HealthComponent;

	public CombatComponent Combat => m_CombatComponent;

	public int GetMaxHealth()
	{
		return m_RuntimeAttributes != null ? m_RuntimeAttributes.BaseHealth : 0;
	}

	public int GetAttackPower()
	{
		return m_RuntimeAttributes != null ? m_RuntimeAttributes.BaseAttack : 0;
	}

	public override void _Ready()
	{
		var attributesOk = TryDuplicateAttributes();
		var jobOk = ValidateJobForLocomotion();
		if (attributesOk && jobOk)
		{
			TrySpawnLocomotion();
		}

		m_TransformComponent = FindDirectChild<TransformComponent>();
		if (m_TransformComponent == null)
		{
			GD.PushError($"{GetPath()}: missing child TransformComponent");
		}

		m_MovementComponent = FindDirectChild<MovementComponent>();
		m_HealthComponent = FindDirectChild<HealthComponent>();
		m_HurtboxComponent = FindDirectChild<HurtboxComponent>();
		m_CombatComponent = FindDirectChild<CombatComponent>();
		m_HitboxComponent = FindDirectChild<HitboxComponent>();

		if (m_HurtboxComponent == null)
		{
			GD.PushError($"{GetPath()}: missing child HurtboxComponent");
		}

		if (m_HealthComponent == null)
		{
			GD.PushError($"{GetPath()}: missing child HealthComponent");
		}
		else
		{
			m_HealthComponent.InitializeFromActor();
			m_HealthComponent.Died += OnHealthDied;
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		m_MovementComponent?.PhysicsTick(delta);
		m_HitboxComponent?.PhysicsTick(delta);
		m_CombatComponent?.PhysicsTick(delta);
		m_HurtboxComponent?.RedrawDebug();
	}

	private bool TryDuplicateAttributes()
	{
		if (Definition == null)
		{
			GD.PushError($"{GetPath()}: Definition is null");
			return false;
		}

		if (Definition.Attributes == null)
		{
			GD.PushError($"{GetPath()}: Definition.Attributes is null (Id={Definition.Id})");
			return false;
		}

		m_RuntimeAttributes = Definition.Attributes.Duplicate() as CombatAttributes;
		if (m_RuntimeAttributes == null)
		{
			GD.PushError($"{GetPath()}: failed to Duplicate Definition.Attributes (Id={Definition.Id})");
			return false;
		}

		return true;
	}

	private bool ValidateJobForLocomotion()
	{
		if (Definition == null)
		{
			return false;
		}

		if (Definition.Job == null)
		{
			GD.PushError($"{GetPath()}: Definition.Job is null (Id={Definition.Id})");
			return false;
		}

		if (Definition.Job.Locomotion == null)
		{
			GD.PushError($"{GetPath()}: Definition.Job.Locomotion is null (Id={Definition.Id})");
			return false;
		}

		if (Definition.Job.Movement == null)
		{
			GD.PushError($"{GetPath()}: Definition.Job.Movement is null (Id={Definition.Id})");
			return false;
		}

		return true;
	}

	private void TrySpawnLocomotion()
	{
		if (FindDirectChild<MovementComponent>() != null)
		{
			GD.PushError($"{GetPath()}: unexpected static MovementComponent; skip Job.Locomotion instantiate");
			return;
		}

		var instance = Definition.Job.Locomotion.Instantiate();
		if (instance is not MovementComponent)
		{
			GD.PushError($"{GetPath()}: Job.Locomotion root is not MovementComponent");
			instance.QueueFree();
			return;
		}

		AddChild(instance);
	}

	private T FindDirectChild<T>() where T : Node
	{
		foreach (var child in GetChildren())
		{
			if (child is T match)
			{
				return match;
			}
		}

		return null;
	}

	private void OnHealthDied()
	{
		if (this is Player)
		{
			GD.Print($"{GetPath()}: player died");
			return;
		}

		QueueFree();
	}
}
