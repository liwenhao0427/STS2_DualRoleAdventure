# Changelog

本文件记录 `LocalMultiControl` 的重要版本与关键变更。

## [Unreleased]

## [v0.1.9] - 2026-03-15

### Fixed
- 修复休息区偶发“未手动选牌即随机升级并结束”的问题，恢复战斗外手动选牌。
- 休息区改为固定串行：每位角色各选择一次后才结束，不再遗漏后续角色。
- 事件中的升级/删牌等分支强制对所有未完成角色按同一选项逐次处理。
- 宝箱切人后自动恢复鼠标可见，减少切人后鼠标丢失问题。

## [v0.1.3] - 2026-03-15

### Changed
- 战斗界面切人按钮改为纯图标风格，支持放大与右侧镜像。
- 选人界面右下 2x2 按钮组重排，统一与战斗界面交互样式。
- 多轮截图驱动微调：优化箭头位置、边距、横向间距与可读性。

### Notes
- 对应发布标签：`v0.1.3`
- 对应发布页：<https://github.com/liwenhao0427/STS2_DualRoleAdventure/releases/tag/v0.1.3>

## [v0.1.2] - 2026-03-14

### Changed
- 调整发版流程，支持“快速发版”（可直接基于项目根已有产物发布）。

## [v0.1.1-clearable] - 2026-03-14

### Fixed
- 修复入战斗顶部栏显示异常。
- 修复共享事件自动代投相关卡流程问题。

## [v0.1.0-initial-usable] - 2026-03-13

### Added
- 建立本地多控最小可用闭环。
- 基础输入切换、关键同步链路与核心补丁框架落地。

[Unreleased]: https://github.com/liwenhao0427/STS2_DualRoleAdventure/compare/v0.1.9...HEAD
[v0.1.9]: https://github.com/liwenhao0427/STS2_DualRoleAdventure/releases/tag/v0.1.9
[v0.1.3]: https://github.com/liwenhao0427/STS2_DualRoleAdventure/releases/tag/v0.1.3
[v0.1.2]: https://github.com/liwenhao0427/STS2_DualRoleAdventure/releases/tag/v0.1.2
[v0.1.1-clearable]: https://github.com/liwenhao0427/STS2_DualRoleAdventure/releases/tag/v0.1.1-clearable
[v0.1.0-initial-usable]: https://github.com/liwenhao0427/STS2_DualRoleAdventure/releases/tag/v0.1.0-initial-usable
