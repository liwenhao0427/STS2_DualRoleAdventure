# DualRoleAdventure

DualRoleAdventure 是一个《杀戮尖塔2》Mod 分支，目标是实现单人双角色冒险模式。

当前阶段已接入 baselib，并实现本地双人多控的基础补丁（输入切换、奖励路由、商店与药水归属等）。

## baselib 使用状态

- 已使用 baselib（MegaCrit.Sts2.Core.* 命名空间），当前运行时逻辑与 Harmony 补丁均基于其 API。

## 目录结构

```text
Mods/LocalMultiControl/
├── Scripts/
│   ├── Entry.cs
│   └── Patch/
│       └── LocalMultiControlPatch.cs
├── AGENTS.md
├── LocalMultiControl.csproj
├── mod_manifest.json
└── README.md
```

## 相关知识

知识库位于 `../../知识库/Mod 开发指南/`，包含从 [SlayTheSpire2ModdingTutorials](https://github.com/GlitchedReme/SlayTheSpire2ModdingTutorials) 克隆的 Mod 开发教程。

- `Basics/` - 基础开发知识
- `Visuals/` - 视觉相关开发
- `images/` - 教程配图
- `README.md` - 教程索引

## 构建

在 `src/Mods/LocalMultiControl` 目录执行：

```powershell
dotnet restore LocalMultiControl.csproj
dotnet build LocalMultiControl.csproj -c Debug
```

## 格式检查

```powershell
dotnet format LocalMultiControl.csproj --verify-no-changes
```

## 部署

- DLL：构建后由 `LocalMultiControl.csproj` 的 `Copy Mod` 目标自动复制到游戏 `mods` 目录。
- PCK：先在 Godot 导出 `LocalMultiControl.pck`，再执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\copy_pck_to_game.ps1
```

