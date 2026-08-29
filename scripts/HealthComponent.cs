using Godot;

public partial class HealthComponent : Node
{
	[Signal]
	public delegate void HealthChangedEventHandler(int oldValue, int newValue);

	[Signal]
	public delegate void DiedEventHandler();

	private Actor m_Actor;

	public int CurrentHealth { get; private set; }

	public bool IsDead => CurrentHealth <= 0;

	public void InitializeFromActor()
	{
		m_Actor = GetParentOrNull<Actor>();
		if (m_Actor == null)
		{
			GD.PushError($"{GetPath()}: parent is not Actor");
			CurrentHealth = 0;
			return;
		}

		var maxHealth = m_Actor.GetMaxHealth();
		CurrentHealth = maxHealth <= 0 ? 0 : maxHealth;
	}

	public void TakeDamage(int amount)
	{
		if (amount <= 0 || IsDead)
		{
			return;
		}

		var oldValue = CurrentHealth;
		CurrentHealth = Mathf.Max(0, CurrentHealth - amount);
		EmitSignal(SignalName.HealthChanged, oldValue, CurrentHealth);
		if (CurrentHealth == 0)
		{
			EmitSignal(SignalName.Died);
		}
	}

	public void Heal(int amount)
	{
		if (amount <= 0 || IsDead || m_Actor == null)
		{
			return;
		}

		var oldValue = CurrentHealth;
		var maxHealth = m_Actor.GetMaxHealth();
		CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + amount);
		if (CurrentHealth != oldValue)
		{
			EmitSignal(SignalName.HealthChanged, oldValue, CurrentHealth);
		}
	}
}
