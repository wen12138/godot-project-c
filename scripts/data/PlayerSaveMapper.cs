/// <summary>
/// PlayerRuntimeState 与 SaveGameDto 之间的唯一桥接。
/// </summary>
public static class PlayerSaveMapper
{
	public const int CurrentVersion = 1;

	public static SaveGameDto ToDto(PlayerRuntimeState runtime)
	{
		return new SaveGameDto
		{
			Version = CurrentVersion,
			SchemaProbe = runtime.SchemaProbe,
		};
	}

	public static void ApplyTo(SaveGameDto dto, PlayerRuntimeState runtime)
	{
		runtime.SchemaProbe = dto.SchemaProbe ?? "";
	}
}
