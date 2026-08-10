using System.Text.Json;
using Godot;

/// <summary>
/// 存档 Autoload：负责 SaveGameDto 与 user:// JSON 文件的读写。
/// 不持有玩家运行时实例。
/// </summary>
public partial class SaveService : Node
{
	private const string SaveDir = "user://saves";
	private const int CurrentVersion = 1;

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true,
	};

	public Error Save(int slot, SaveGameDto dto)
	{
		var dirErr = DirAccess.MakeDirRecursiveAbsolute(SaveDir);
		if (dirErr != Error.Ok && dirErr != Error.AlreadyExists)
		{
			GD.PushError($"SaveService: 无法创建存档目录 '{SaveDir}' — {dirErr}");
			return dirErr;
		}

		var path = GetSlotPath(slot);
		string json;
		try
		{
			json = JsonSerializer.Serialize(dto, JsonOptions);
		}
		catch (JsonException ex)
		{
			GD.PushError($"SaveService: 序列化失败 — {ex.Message}");
			return Error.InvalidData;
		}

		using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
		if (file == null)
		{
			var openErr = FileAccess.GetOpenError();
			GD.PushError($"SaveService: 无法写入 '{path}' — {openErr}");
			return openErr;
		}

		file.StoreString(json);
		return Error.Ok;
	}

	public SaveGameDto Load(int slot)
	{
		var path = GetSlotPath(slot);
		if (!FileAccess.FileExists(path))
		{
			GD.PushError($"SaveService: 存档不存在 '{path}'");
			return null;
		}

		using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
		if (file == null)
		{
			GD.PushError($"SaveService: 无法读取 '{path}' — {FileAccess.GetOpenError()}");
			return null;
		}

		var text = file.GetAsText();
		SaveGameDto dto;
		try
		{
			dto = JsonSerializer.Deserialize<SaveGameDto>(text, JsonOptions);
		}
		catch (JsonException ex)
		{
			GD.PushError($"SaveService: JSON 解析失败 '{path}' — {ex.Message}");
			return null;
		}

		if (dto == null)
		{
			GD.PushError($"SaveService: 反序列化结果为空 '{path}'");
			return null;
		}

		if (dto.Version != CurrentVersion)
		{
			GD.PushError($"SaveService: 不支持的存档版本 {dto.Version}（当前 {CurrentVersion}）");
			return null;
		}

		return dto;
	}

	public bool Exists(int slot)
	{
		return FileAccess.FileExists(GetSlotPath(slot));
	}

	public Error Delete(int slot)
	{
		var path = GetSlotPath(slot);
		if (!FileAccess.FileExists(path))
		{
			return Error.FileNotFound;
		}

		var err = DirAccess.RemoveAbsolute(path);
		if (err != Error.Ok)
		{
			GD.PushError($"SaveService: 删除失败 '{path}' — {err}");
		}

		return err;
	}

	private static string GetSlotPath(int slot)
	{
		return $"{SaveDir}/slot_{slot}.json";
	}

	/// <summary>
	/// 版本迁移钩子。本轮不调用；日后在 Load 中于版本不匹配时接入。
	/// </summary>
	private SaveGameDto Migrate(SaveGameDto dto)
	{
		return dto;
	}
}
