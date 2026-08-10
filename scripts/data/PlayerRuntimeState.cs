/// <summary>
/// 玩家会话运行时状态。由玩法代码创建并注入，不读写磁盘。
/// </summary>
public sealed class PlayerRuntimeState
{
	/// <summary>占位字段，供后续验证往返；真实字段日后扩展。</summary>
	public string SchemaProbe { get; set; } = "";

	public void Reset()
	{
		SchemaProbe = "";
	}
}
