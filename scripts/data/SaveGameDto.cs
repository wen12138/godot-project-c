/// <summary>
/// 可序列化的存档快照。只含持久化所需数据，不含 Node 或玩法逻辑。
/// </summary>
public sealed class SaveGameDto
{
	public int Version { get; set; } = 1;

	/// <summary>占位字段，供后续验证往返；真实字段日后扩展。</summary>
	public string SchemaProbe { get; set; } = "";
}
