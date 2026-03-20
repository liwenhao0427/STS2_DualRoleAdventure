# Skill: feishu-bitable-sync

用于在 `LocalMultiControl` 项目中，按统一规范将“任务完成情况”同步到飞书多维表格。

## 1. 何时使用

- 用户提到“同步飞书”“更新多维表格”“回写任务台账”“更新任务状态”时。
- 会话完成有效成果并已提交 commit 后，按项目规范必须执行回写。

## 2. 依赖与环境变量

执行前必须存在以下用户级环境变量：

- `FEISHU_APP_ID`
- `FEISHU_APP_SECRET`
- `FEISHU_WIKI_NODE_TOKEN`
- `FEISHU_TABLE_ID`（默认任务表 `tblsr2MTjWeiuNZ8`）

当前链接（项目约定）：

- 任务管理表：`https://my.feishu.cn/wiki/B17OwI4Ymi8E7skp339cFiRHnnf?table=tblsr2MTjWeiuNZ8&view=vewXxBNTOK`
- 玩家反馈结果：`https://my.feishu.cn/wiki/B17OwI4Ymi8E7skp339cFiRHnnf?table=tblosEdrnEI0L77n&view=vewjPpNXxV`
- 玩家问卷入口：`https://my.feishu.cn/share/base/form/shrcn5DNr4nXrBDHMkLrHaEaVyh`

## 3. 标准执行命令

在项目根目录执行（UTF-8）：

```powershell
powershell -ExecutionPolicy Bypass -File .\Scripts\Tools\SyncFeishuTask.ps1 `
  -TaskDescription "本次任务标题" `
  -LatestNote "本次完成内容摘要（面向玩家/开发可读）" `
  -Progress "已完成" `
  -DevStatus "已完成" `
  -Category "Bug修复" `
  -Source "AI整理"
```

### 常用可选参数

- `-CommitRef "abcd123 修复xxx"`：不传时默认取 `git log -1`。
- `-Importance "重要紧急"` 或 `重要不紧急`。
- `-StartDate/-ExpectedDate/-ActualDate`：可显式指定日期。
- `-DryRun`：只校验和打印，不落表。

## 4. 字段映射标准

脚本会写入以下字段（必须保持）：

- `任务描述` ← `-TaskDescription`
- `进展` ← `-Progress`
- `开发状态` ← `-DevStatus`
- `任务分类` ← `-Category`
- `最新进展记录` ← `-LatestNote`
- `提交参考` ← `-CommitRef`
- `数据来源` ← `-Source`
- 日期字段：`开始日期` / `预计完成日期` / `实际完成日期`
- 其他：`是否延期`、`重要紧急程度`

## 5. Upsert 规则

- 按 `任务描述` 在任务表查重：
  - 已存在：更新该记录。
  - 不存在：创建新记录。

## 6. 失败回退

- 若 API 返回权限错误（如 `Forbidden`）：
  - 先检查应用权限和表格授权是否仍有效。
  - 暂时将同等内容写入 `Mod玩家更新与使用说明.md`，并在会话说明中标记“待补回写”。

## 7. 会话收尾检查

- 回写后应确认命令输出包含 `CREATED|...` 或 `UPDATED|...`。
- 在最终回复中给出：
  - 是否写入成功
  - 对应记录 ID
  - 本次使用的任务链接
