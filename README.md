# LocalMultiControl

LocalMultiControl 是一个《杀戮尖塔2》Mod 框架，目标是把联机控制体验适配为本地多控操作。

当前阶段仅初始化工程骨架，尚未添加具体功能逻辑。

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
