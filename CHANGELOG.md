# Changelog

## v0.1.0 — 2026-09-26

- 初始版本（M1）
- 战斗内笔记画布：卡牌图例（手牌拖入 / 全卡搜索）、文本节点、分支连线（可写条件）
- 状态标记：未标记 / 已尝试 / 推测 / 已确认
- 撤销重做、编辑备注、复制、删除；画布缩放/平移
- 持久化：本局笔记随存档（RitsuLib `RunSavedDataStore`）；全局笔记库跨局（`ModDataStore` Profile）
- 中英文本地化、`affects_gameplay: false`
