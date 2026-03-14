# LocalMultiControl（DualRoleAdventure）

`LocalMultiControl` 是《Slay the Spire 2》的本地多人控制 Mod。
目标是把联机控制链路复用到单机环境，实现 `2~4 人本地多控`（支持重复选角），并尽量保持原联机流程的一致性。

## 当前能力

- 本地多人组队：支持 2~4 人。
- 角色控制切换：支持热键切换与界面按钮切换。
- 战斗/选人/大厅关键界面补丁：提供可视化切人操作。
- 关键流程同步：覆盖奖励、事件、商店、遗物/药水等核心链路。

## baselib 使用状态

- 当前 **已使用 baselib**（`MegaCrit.Sts2.Core.*`）。
- 运行时主逻辑与 Harmony 补丁均基于 baselib API。

## 目录说明

```text
src/Mods/LocalMultiControl/
├─ Scripts/
│  ├─ Entry.cs
│  ├─ Patch/
│  └─ Runtime/
├─ LocalMultiControl.csproj
├─ mod_manifest.json
├─ README.md
└─ CHANGELOG.md
```

## 构建与检查

在 `src/Mods/LocalMultiControl` 目录执行：

```powershell
dotnet restore LocalMultiControl.csproj
dotnet build LocalMultiControl.csproj -c Debug
dotnet format LocalMultiControl.csproj --verify-no-changes
```

## 部署与联调

1. 导出 pck：

```powershell
& "C:\Users\temp\项目\杀戮尖塔2Mod\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64.exe" --path . --export-pack "Windows Desktop" DualRoleAdventure.pck
```

2. 复制到游戏目录：

```powershell
powershell -ExecutionPolicy Bypass -File .\copy_pck_to_game.ps1
```

3. 启动游戏：

```powershell
Start-Process "steam://rungameid/2868840"
```

说明：部署使用项目根产物 `DualRoleAdventure.dll` 与 `DualRoleAdventure.pck`。

## 发布说明

- 默认支持快速发版：若项目根已有最新 `DualRoleAdventure.dll` 与 `DualRoleAdventure.pck`，可直接创建 tag + GitHub Release。
- 仅在用户明确要求重编译/重导出或产物缺失时，执行完整构建与导出流程。

## 修改日志（重要里程碑）

详细记录见 [CHANGELOG.md](./CHANGELOG.md)。

- `v0.1.3`（2026-03-15）：切人与加减按钮样式重做（图标化、镜像、间距与位置微调），提升战斗/选人 UI 一致性。
- `v0.1.2`（2026-03-14）：发版规范升级为快速发布流程。
- `v0.1.1-clearable`（2026-03-14）：修复入战斗 UI 显示与共享事件自动代投关键问题。
- `v0.1.0-initial-usable`（2026-03-13）：形成可用最小闭环，打通本地多控基础流程。

## 知识库参考

开发参考知识库位于：`../../知识库/Mod 开发指南/`（基于 SlayTheSpire2ModdingTutorials）。
