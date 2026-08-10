# 游戏数据管理框架设计

日期：2026-08-10  
项目：ProjectC（Godot 4.6 / C#）  
状态：已定稿

## 目标

搭建一套最小可用的游戏数据框架，将**持久化存档数据**与**可注入的玩家运行时状态**分离，并通过 Mapper 桥接。暂不定义具体玩法字段，只验证结构，供后续开发扩展。框架自证与自动化测试不在本轮范围；由开发者在后续开发中自行检验。

## 约束

- 存档访问：Autoload 全局（`SaveService`）
- 玩家运行时数据：可注入的纯 C# 对象（非 Autoload）
- 本轮只交付 `scripts/data/`，不接入 `Actor` 或组件
- 不做 UI、加密、压缩、云存档、版本迁移实现
- `SaveService` 不做 smoke / 自动测试

## 架构

```text
scripts/data/
  SaveGameDto.cs          // 可序列化快照
  PlayerRuntimeState.cs   // 会话状态，可注入
  PlayerSaveMapper.cs     // Runtime ↔ Dto
  SaveService.cs          // Autoload：文件读写
```

```text
新开局 → PlayerRuntimeState
              ↕ PlayerSaveMapper
           SaveGameDto  ↔  SaveService  ↔  user:// JSON
```

| 类型 | 职责 | 禁止 |
|------|------|------|
| `SaveGameDto` | 可 JSON 序列化的快照；含 `Version` 与占位字段 | 持有 Node 或玩法逻辑 |
| `PlayerRuntimeState` | 内存中的玩家会话状态 | 读写磁盘 |
| `PlayerSaveMapper` | `ToDto` / `ApplyTo` | 接触文件 |
| `SaveService` | 在 `user://` 下按槽位存/读/判断存在/删除 | 持有玩家运行时实例 |

## Autoload

在 `project.godot` 中：

```text
SaveService = *res://scripts/data/SaveService.cs
```

## API 表面

### `SaveGameDto`

- `int Version`
- `string SchemaProbe` — 占位字段，便于后续验证往返；真实字段日后替换/扩展

### `PlayerRuntimeState`

- `string SchemaProbe`
- `void Reset()` — 新开局默认值

### `PlayerSaveMapper`

- `SaveGameDto ToDto(PlayerRuntimeState runtime)`
- `void ApplyTo(SaveGameDto dto, PlayerRuntimeState runtime)`

### `SaveService`（Autoload）

- `Error Save(int slot, SaveGameDto dto)`
- `SaveGameDto Load(int slot)` — 失败时返回 `null` 并 `GD.PushError`
- `bool Exists(int slot)`
- `Error Delete(int slot)`

## 约定

- 路径：`user://saves/slot_{n}.json`
- 当前 schema 版本：`1`
- 写入前用 `DirAccess.MakeDirRecursiveAbsolute` 确保目录存在
- 加载时若 `Version != 1`：返回 `null` 并打日志；预留空的 `Migrate` 钩子（本轮不调用）
- 使用 System.Text.Json 或 Godot 友好的 JSON，序列化为纯 DTO（现阶段仅标量/字符串）
- `PlayerRuntimeState` 由后续玩法代码创建并持有；本轮只提供类型

## 错误处理

| 情况 | 行为 |
|------|------|
| 存档目录不存在 | Save 时递归创建 |
| 读写失败 | 返回 `Error`，并 `GD.PushError` |
| JSON 解析失败 | `Load` 返回 `null` |
| 不支持的版本 | `Load` 返回 `null` |

## 不在本轮范围

- Actor / 组件注入接线
- 框架自证、smoke、自动化测试
- 多槽位 UI
- 存档加密 / 压缩
- 完整版本迁移实现
- 使用 `.tres` / `.res` 作为玩家存档

## 后续扩展

- 向 DTO 与 Runtime 同步增加真实字段；始终只通过 Mapper 桥接
- 提升 `Version` 时实现增量 `Migrate(dto)`
- 玩法需要时，将 `PlayerRuntimeState` 注入 `Actor` 与组件
- 若大量世界实体需持久化，可再引入 Saveable 注册表
