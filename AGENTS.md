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

## 交接状态（2026-09-26，新会话从这里开始）

### 当前进度
- **版本 v0.8.0，已上架创意工坊**（ID `3808343573`，线上 changelog 已验证 v0.8.0）。
  仓库 HEAD = `2e873b2`（第二轮 5-8 轮 + v0.8.0），前一批 = `e57c4e6`，均已 push。
- 两轮自动迭代共 18 轮全部完成、0 受阻：v0.7.0（UX/UI 大波 + 链接/数据修复，10 轮）、
  v0.8.0（安全/数据完整性 + 新玩家引导 + 性能/结构，8 轮）。
- 单测 45 个；`bash tools/check.sh` 现在会校验三处版本号 + VDF changenote 含版本号。
- **游戏当前跑的是工坊订阅版**；本地 `mods/Notes` 已删除（避免 DUPLICATE_ID），
  要实机验证必须更新工坊（先问用户）或临时本地部署并退订。

### 未完成（backlog，2026-09-27 清理后）
1. 图鉴行整表重建（已去抖未复用）：搜索时仍会重建 ≤120 行，未做行复用
2. 手柄导航未实测（Tab 键盘可用）
3. 实机验收 / 工坊更新：本地 `mods/Notes` 已删除，实机验证需更新工坊（先问用户）
4. 长期方向：卡面缩略图 atlas、设置页 RitsuLib `[ModSettingsPage]`

### 手柄导航验收清单（静态已实现，实机待验）
1. 手柄连接时窗口打开：焦点落在画板选择器；方向键移动焦点，A 确认，B/Esc 返回
2. 工具栏按钮 → 托盘行 → 节点：焦点环可见；A/Enter 在节点上打开编辑器
3. 节点获得焦点即选中；`1/2/3/0` 标记、`Delete` 删除仍可用
4. 链接模式 / 多选时：B/Esc 先取消，再关闭窗口；折叠把手 A 展开
5. 焦点顺序不理想时检查 `FocusMode`（Button 默认 All，NodeControl=Click）与容器布局

### 2026-09-27 遗留清理（版本仍 v0.8.0，未上传工坊）
- 已完成原清单 1–9：Normalize 画板去重 + 幂等修复、提示 8s 过期、
  `publish.sh` 版本标签、注释协议 `AnnotationProtocol`、世界线创建走命令栈（Ctrl+Z 可撤）、
  `CardCatalog` 失败不缓存、删板确认框快照目标板、折叠箭头 tooltip、`BoardCanvas.Menus.cs` 拆分
- 单测 45 → 52；`bash tools/check.sh` 全绿（双版本构建 0 warning）
- 工坊 DLL 未重建（代码改动未发布）；发布时先问用户再走 `steam-workshop-upload` skill

### 关键注意事项（本轮踩过的坑）
- 工坊上传：`/mnt/d/steamcmd/steamcmd.exe +login lwh4646 +workshop_build_item 'D:\0_git\Notes\packaging\workshop\Notes_workshop.vdf' +quit`；
  超时一次属常见，直接重试；上传后等 ~15s 再用 `curl.exe -x http://127.0.0.1:7897` 验证 changelog。
- 发布物料：改版本号后 `check.sh` 会强制校验 VDF changenote 含 `v<版本>`；上传前把
  `.godot/mono/temp/bin/Release/Notes.dll` 拷到 `packaging/workshop/content/Notes/Notes.dll`。
- autopilot 提交器会把暂存中的工坊 DLL 报成 "Secret-like content"（二进制不可扫描），
  提交时需加 `--allow-secrets`（该 DLL 是本仓库构建产物，已确认安全）。
- 存档：`/mnt/c/Users/Admin/AppData/Roaming/SlayTheSpire2/Notes/run_notes.json`；
  损坏/新版本会被隔离为 `run_notes.json.corrupt-<ts>` 并新建空档（状态栏提示）。
- 全局库：RitsuLib key `boards` / file `notes_boards`（Profile scope，`syncToCloud:false`），
  经 `ModDataStoreCache` 读取（档案切换自动重载）。
- 迭代工具状态：`.autopilot/`（config/state/backlog/reports 均在此，已 gitignore）。
  恢复方式：`config-set --max-rounds N` 后 `init --force`（会保留 backlog，轮次重新计数），
  再按 skill 的 `check → begin-round → …` 循环；报告：`report --output …` / `retrospective`。

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

## 当前状态（2026-09-26，v0.8.0）

- v0.8.0：安全/数据修复（怪物行动保护、损坏存档隔离、纯本地、防串档）+ 首次引导/
  悬浮反馈/只读标识/键盘可达 + 撤销作用域修复 + 捕获合并与悬停优化 + 死代码清理；
  单测 45 个；check.sh 增加版本一致性校验
- v0.7.0：UX/UI 大波优化 + 关键修复（10 轮自我迭代）——两行工具栏/窗口可拖动/深色统一/
  节点投影/空板引导/缩放指示；链接流程修复（点击式连线、幽灵拖动、Esc 取消）、
  录入自动复制、清空日志同步清理 chip/快照、撤销导入恢复快照/HP、保存失败重试；
  单测 42 个
- v0.6.2：修复「记本回合」追加不覆盖（改为整体重建该回合，含手放节点）；
  修复结构化节点拖到区域内未命中槽位时误脱离成自由节点（新画板无后续槽位）；
  节点控件转发拖放事件（悬停节点上也显示槽位/可落入）
- v0.6.1：修复放大后区域框/回合间文字被 Godot 按控件矩形剔除（自定义裁剪矩形）+
  节点控件复用 + 最大缩放 3x
- v0.6.0（第三轮验收）：回合间旁注条（不再重叠）+ 敌人回合战损旁注 + 总览战损按
  回合末 HP 统计；画板模型改为「自由总览 + 只读当前世界线（自动记录）」，
  「复制→新世界线」深拷贝为可交互板（双版本构建 + 35 单测）
- v0.5.1：修复收拢后小箭头点不到（显式最小尺寸）
- v0.5.0（第二轮验收）：弃牌并入触发操作详情 + 无归因落回合旁注；快照显示操作前手牌；
  画板=世界线（世界线N 命名、录战斗→新线建新板、默认自由总览板可点击跳转、
  每场战斗开始清空画板且同场 SL 保留）；悬浮按钮快捷「记本回合/记战斗→新线」、
  可收拢为屏幕边缘小箭头；笔记窗口可自由拉伸（尺寸持久化）（双版本构建 + 30 单测）
- v0.4.0（本轮验收修复）：消耗并入触发操作的详情（含打出的卡自身）+ 无归因落回合旁注；
  「录本回合」覆盖重建（命令级单测）；操作对象/伤害/击杀记录；同名敌人左→右编号；
  敌人塞牌边界旁注【敌人】将【卡*N】加入到【牌堆】
- v0.3.6：旁注改为悬停详情预览 + 节点数量徽章；消耗旁注格式化「A」、「B」被消耗
- M2 已实现：结构化世界线/回合区域/槽位连线（schema v2）、操作流水自动编入、遗物与抽弃旁注、
  手牌/卡组/百科三来源面板、版本 0.3.0（0.107.1–0.111.0 双版本构建 + 23 单测）
- M1：画布/节点/连线/状态标记/撤销重做/本局+全局持久化/i18n
- v0.2.0：节点拖动跟手修复（全局鼠标坐标）、「连线」模式、贝塞尔分支 + 点阵网格 +
  圆角节点美化、右侧居中可拖动开关把手（位置持久化 `NotesGlobalData.ButtonX/Y`）
- 待办：见上方「交接状态 → 未完成」清单（实机验收、卡面缩略图 atlas、设置页
  RitsuLib `[ModSettingsPage]` 仍为长期方向）
