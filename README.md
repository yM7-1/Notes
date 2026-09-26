# Notes — 杀戮尖塔 2 世界线笔记

在战斗中打开一块**本地**笔记画布：把当前手牌里的卡拖进去当图例，连成决策树 / 思维导图，记录你已经
**尝试过 ✔** 和还在 **推测 ?** 的世界线。

- 纯本地、只读：不探测 RNG、不预测牌序、不调用任何游戏命令、不写入任何游戏状态
- `affects_gameplay: false`：联机也可用（笔记不会发给其他玩家，也不会造成不同步）
- 依赖：**RitsuLib ≥ 0.6.2**（Steam 创意工坊 `3747602295`）

## 功能（M1）

- 右上角「笔记」悬浮按钮 / `F8` 开合面板；画布支持拖拽、缩放（0.5–2x）、平移
- 手牌托盘：直接拖到画布生成卡牌图例（费用 / 名称 / 类型色 / 升级状态快照）；也可全卡库搜索
- 文本节点：记录事件、遗物、思路
- 连线：从节点右上圆点拖出分支，可写条件（如「若抽到 X」）
- 状态标记：未标记 / 已尝试 / 推测 / 已确认
- 撤销重做、编辑备注、复制、删除
- 持久化：**本局**笔记随存档保存（读档继续）；**全局**笔记库跨局保留

## 构建

```bash
bash tools/fetch-refs.sh                 # 拉 0.107.1 / 0.111.0 参考程序集（.refs，不入库）
bash tools/check.sh                      # 实机版本 + 最低版本 双构建 + 单元测试
bash tools/check.sh                      # 全绿后交付

# 本地部署到游戏（WSL）
dotnet build Notes.csproj -c Release -p:CopyModOnBuild=true -p:SteamRoot=/mnt/d/Steam \
  -p:Sts2UseLocalGame=true -p:RitsuLibReferenceTarget=0.111.0
```

目标环境：STS2 **v0.107.1 – v0.111.0**（正式服 + 测试服共用同一 DLL）+ RitsuLib 0.6.2。

## 工程结构

```
src/Notes.Core/   纯 C#：节点/边/画板模型、序列化、命令栈（可单测，不引用 Godot）
src/Runtime/      Mod 运行时：入口、持久化、本地化、Godot UI（画布/节点/手牌托盘）
tests/Notes.Tests 单元测试
tools/            fetch-refs / check / publish
packaging/        创意工坊打包物料（上传前须人工确认）
```

`affects_gameplay: false` 是刻意选择：这一版只做「笔记本」，不做战斗求解（对比 Combat Solver 的
全世界线探测），以保护玩家的自行探索体验。
