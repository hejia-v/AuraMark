# UI/UX 规则与截图检查（AuraMark）

本文件定义“可自动判定”的 UI/UX 规则集合，用于 E2E 运行时的 screenshot-based checks 与回归。

术语说明：

- `checkpoint`: 一个可复现的 UI 状态点（例如“打开文件完成后”）。
- `baseline`: 参考截图（golden image）。
- `diff`: 当前截图与 baseline 的差异度量（像素差 / 结构差）。
- `severity`: 问题等级，影响是否允许进入下一轮或直接失败。

## 目录

1. 截图策略
2. 命名与目录结构
3. 必选 checkpoints（PRD 6.3 优先）
4. 规则列表（Rule Set）
5. 视觉回归（baseline/diff）策略
6. 失败判定与阈值

## 1. 截图策略

原则：少而精、可复现、可定位。

- 只在“状态稳定”时截图：加载完成、动画结束、toast 展示完成后。
- 固定窗口大小截图：建议 `1280x800`（与 MainWindow 默认一致）。
- 每个 checkpoint 至少 1 张截图；若有 transient 状态（Loading/Error toast），需要额外截图。
- 截图同时保存一份 metadata（JSON），包含时间戳、窗口大小、当前文件路径、当前 case 名称等。

## 2. 命名与目录结构

每次运行创建一个 run folder（例如 `artifacts/e2e-YYYYMMDD-HHMMSS`），结构建议：

- `logs/`
- `screenshots/`
- `screenshots/baseline/`（可选：临时 baseline；推荐使用“持久 baseline 目录”）
- `screenshots/current/`
- `screenshots/diff/`
- `reports/`

截图文件名建议：

`{caseId}__{checkpoint}__{w}x{h}__{seq}.png`

示例：

- `case1__app_ready__1280x800__001.png`
- `case5__save_error_toast__1280x800__002.png`

## 3. 必选 checkpoints（PRD 6.3）

覆盖 `C:\Dev\AuraMark\docs\ACCEPTANCE_CHECKLIST.md` 里 PRD 6.3 的 5 个用例，建议 checkpoint 如下：

- Case1 新建/输入/自动保存/重启
  - `app_ready`（启动后可编辑状态）
  - `after_typing`（输入后 1-2 秒，保存点亮/状态变化）
- `after_save`（手动触发 Save 后，保存完成）
  - `after_restart_restored`（重启后内容恢复）

- Case2 大文件加载
  - `largefile_loading`（Loading overlay 可见）
  - `largefile_loaded`（内容可见，UI 不冻结）

- Case3 沉浸模式
  - `immersive_entered`（输入 >=3s 后 topbar/sidebar 隐藏或弱化）
  - `immersive_exited`（大幅鼠标移动后恢复）

- Case4 外部热更新
  - `external_change_detected`（变更后内容更新）

- Case5 保存失败提示
  - `save_error_toast_shown`（toast 可见，文案可读）
  - `save_error_retry_clicked`（点击 Retry 后状态变化）

## 4. 规则列表（Rule Set）

规则分 3 类：`Layout`、`State`、`Legibility`。默认都启用。

### Layout 规则

- `L001 No Clipping`: 关键区域不应被裁剪（topbar、editor stage、toast、loading overlay）。
- `L002 No Overlap`: toast 不应被 topbar 遮挡；loading overlay 不应被 editor 内容穿透。
- `L003 Stable Padding`: 主编辑区域与窗口边缘保持一致 margin（避免贴边）。
- `L004 Sidebar Width`: sidebar 展开后宽度稳定（默认 280），且不会把 editor 压到不可用。

### State 规则

- `S001 Loading Visible`: 大文件加载时必须出现 loading overlay，且文案与进度条可见。
- `S002 Saved Feedback`: 手动保存后应出现“保存完成/状态变更”的可见反馈（状态栏或 saving dot）。
- `S003 Error Feedback`: 保存失败必须出现软错误提示且不冻结输入；Retry 可用。
- `S004 Immersive Toggle`: 进入/退出沉浸模式的 UI 状态切换可见且不闪烁。

### Legibility 规则

这些规则可先以“可见性”替代严格可访问性算法，后续再增强。

- `G001 Text Contrast (heuristic)`: 关键文本（文件名、状态、toast 文案）与背景的对比度不能明显过低。
- `G002 Font Size Floor`: 关键状态文本不应小于 12px 等效大小（避免肉眼不可读）。
- `G003 Toast Readable`: error toast 背景与文字组合必须清晰，按钮可辨识。

## 5. 视觉回归（baseline/diff）策略

优先级：先稳定，再精细。

持久 baseline 目录建议：

- 默认：`C:\Dev\AuraMark\artifacts\ui-baseline`
- 由 `scripts/run-loop.ps1` 的 `-BaselineRoot` 控制
- baseline 写入用 `scripts/seed-baseline.ps1`，且必须人工确认后执行（避免把 bug 录成 baseline）

阶段 A（项目初期，推荐默认）：

- 只对关键 checkpoints 做 baseline/diff。
- diff 失败时优先结合日志与 UI 树判断，避免误报。
- baseline 更新需人工确认（避免“把 bug 录成 baseline”）。

阶段 B（覆盖更全面后）：

- 扩展 checkpoints 覆盖更多交互与主题变化。
- 引入更严格的区域级 diff（例如只比较 editor 区域）。

## 6. 失败判定与阈值（默认建议）

这些阈值是“工程可用”的默认值，后续应根据误报/漏报调整。

- `Hard Fail`:
  - 应用崩溃或无法进入 `app_ready`
  - `S001/S003` 失败（Loading/Error 反馈缺失）
  - `L001/L002` 关键元素被遮挡或裁剪

- `Soft Fail`（允许自动修复进入下一轮）：
  - 轻微对齐/间距问题（`L003`）
  - 轻微 diff 超阈（但 UI 树检查通过）

diff 阈值建议（像素差比例 `diff_ratio`）：

- 关键 checkpoints：`diff_ratio <= 0.003`（0.3%）
- 非关键 checkpoints：`diff_ratio <= 0.01`（1%）

如果没有 baseline：

- 只做规则检查（Layout/State/Legibility），并将截图作为 evidence 存档。
