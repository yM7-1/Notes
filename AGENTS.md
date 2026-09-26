# AGENTS.md — Notes 协作备忘

## 项目定位（铁律）

- Notes 是**纯本地笔记 UI**：不探测 RNG、不预测牌序、不自动出牌。
- 代码中**禁止**调用游戏 Command（`DamageCmd/CardPileCmd/...`）、禁止读改 `Rng`、禁止写 `CombatState`。
- 允许只读：`RunManager.Instance.State`、`Player.PlayerCombatState.Hand`、`ModelDb.AllCards`。
- manifest 固定 `affects_gameplay: false`（真实不影响玩法；联机保持可用且不发网络包）。

## 发布规则

- 日常改动：`commit` + `push` 到 GitHub（origin = `yM7-1/Notes`）。
- **不要自动上传/更新创意工坊**：任何工坊更新前必须先询问用户。上传流程见 `steam-workshop-upload` skill。
- 三处版本号需同步：`Notes.csproj`、`Notes.json`、`packaging/workshop/content/Notes/Notes.json`。

## 常用命令

- 引用程序集（首次/换版本）：`bash tools/fetch-refs.sh`（NuGet `Book.StS2.RefLib` → `.refs/sts2-refs/`，不入库）
- 一键验证：`bash tools/check.sh`（installed 0.111.0 构建 + Core 单测 + min 0.107.1 编译）
- 本地部署：`dotnet build Notes.csproj -c Release -p:CopyModOnBuild=true -p:SteamRoot=/mnt/d/Steam -p:Sts2UseLocalGame=true -p:RitsuLibReferenceTarget=0.111.0`
- 目标环境：WSL；目标游戏 **STS2 v0.107.1 – v0.111.0** + **RitsuLib 0.6.2**
- 依赖引用走 `$(SteamRoot)/steamapps/workshop/content/2868840/3747602295/RitsuLib.References.props`；
  `sts2/GodotSharp/0Harmony` 走 `.refs/sts2-refs/<ver>` 或实机 `data_sts2_*`
- steamcommunity 页面验证：WSL 直连不通，用 Windows `curl.exe -x http://127.0.0.1:7897`

## 参考

- 反编译研读笔记：`D:\0-task\0-sts2report`（01 源码地图 / 02 BaseLib / 03 RitsuLib）
- 反编译源码（只读）：`/mnt/d/0_git/BrainFog/.refs/{sts2-v0.111.0,ritsulib-0.6.2}`
- 同类工程：`/mnt/d/0_git/BrainFog`（RitsuLib + 工程化模板）、`/mnt/d/0_git/CombatSolver`

## 当前状态（2026-09-26，v0.3.2）

- M2 已实现：结构化世界线/回合区域/槽位连线（schema v2）、操作流水自动编入、遗物与抽弃旁注、
  手牌/卡组/百科三来源面板、版本 0.3.0（0.107.1–0.111.0 双版本构建 + 23 单测）
- M1：画布/节点/连线/状态标记/撤销重做/本局+全局持久化/i18n
- v0.2.0：节点拖动跟手修复（全局鼠标坐标）、「连线」模式、贝塞尔分支 + 点阵网格 +
  圆角节点美化、右侧居中可拖动开关把手（位置持久化 `NotesGlobalData.ButtonX/Y`）
- 待办：实机验收、卡面缩略图（atlas）、工坊发布物料、设置页（RitsuLib `[ModSettingsPage]`）
