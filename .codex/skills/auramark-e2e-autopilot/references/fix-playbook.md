# 自动修复手册（AuraMark）

本文件把常见失败“症状 -> 诊断 -> 最小修复 -> 复测点”标准化，供 E2E 自愈循环使用。

核心原则：

1. 修复必须可验证：每个补丁都要能通过同一套 gates（build/test/run/log/ui）。
2. 最小改动优先：先改配置/样式/边界条件，后改结构。
3. 只修复“允许自动修复”的类别；禁止为了通过检查做投机调整。

## 目录

1. 允许/禁止的修复范围
2. 诊断优先级
3. 症状到修复映射（XAML / C# / CSS / TS）
4. 复测策略

## 1. 允许/禁止的修复范围

允许自动修复（Allowed）：

- UI layout：padding/margin/width/height/opacity/visibility、overflow、z-order
- state 同步：保存/加载/错误提示的显示逻辑与状态机小问题
- 稳定性：空引用保护、边界条件处理、避免 UI 卡死
- E2E 稳定性：等待条件（waits/timeouts）、更稳定的定位（AutomationId）

禁止自动修复（Disallowed，除非用户明确授权）：

- 大范围视觉重设计（theme 全换、交互大改）
- 与需求无关的架构重构
- 删除功能/弱化校验来“让测试通过”
- 将失败截图直接更新为 baseline（必须人工确认）

## 2. 诊断优先级

按以下顺序定位根因，避免在表象上来回：

1. `build` 失败：先修编译与依赖。
2. `crash/startup`：先修启动崩溃与 fatal exception。
3. `log high severity`：未处理异常、编辑器初始化失败等。
4. `state rule`：Loading/Error/Saved 反馈缺失。
5. `layout/legibility`：遮挡、裁剪、对比度、字体可读性。
6. `visual diff`：在规则通过时再评估是否为可接受变化。

## 3. 症状到修复映射

### A. Loading overlay 不出现或被遮挡（S001 / L002）

症状：

- 大文件加载时未展示 `LoadingOverlay`，或 overlay 出现但不可见/被内容盖住。

诊断：

- 查 WPF 控件可见性与 Z-order：`MainWindow.xaml` 里 `LoadingOverlay` 是否在编辑区之上。
- 查触发逻辑：加载大文件时是否 `ShowLoading(true, ...)`。

最小修复方向：

- 确保 `LoadingOverlay` 在视觉树后添加且 `Panel.ZIndex` 高于编辑区域。
- 确保 overlay 的 `Visibility` 与状态机一致，动画结束后再截图。

复测点：

- Case2：`largefile_loading`、`largefile_loaded`

### B. 保存失败 toast 不出现/不可读/Retry 无效（S003 / G003）

症状：

- 写只读文件后没有 toast；或 toast 文案看不清；或 Retry 点击无反应。

诊断：

- 查 `ErrorToast`：`Visibility`、`Opacity`、动画是否结束。
- 查 `OnRetrySaveClicked` 是否能触发 `SavePendingChangesAsync(force: true)`。
- 查保存失败逻辑：是否将失败内容写入 `_pendingSaveRetryContent`。

最小修复方向：

- toast：提高可见性（背景/边框/文字对比）与层级（ZIndex）。
- retry：确保保存失败后内容被保留；重试时恢复 `_pendingMarkdown`。

复测点：

- Case5：`save_error_toast_shown`、`save_error_retry_clicked`

### C. 输入冻结或 UI 卡死（crash/startup 或 log 高危）

症状：

- 触发保存/加载后 UI 不响应；日志出现死锁/长时间阻塞迹象。

诊断：

- 查同步 IO：是否在 UI thread 做了大文件读取/写入。
- 查编辑器输入与保存事件：是否导致 re-entrancy 或频繁重复刷新。

最小修复方向：

- 将大 IO 放到 async/await，确保 UI thread 不阻塞。
- 为重复触发加 debounce；为 re-render 加保护（例如 suppressOutbound）。

复测点：

- Case2、Case1（输入与手动保存）

### D. 进入/退出沉浸模式闪烁或无法恢复（S004）

症状：

- 输入 3s 后未进入沉浸；或鼠标移动后不退出；或 topbar/sidebar 闪烁。

诊断：

- 查阈值：`ImmersiveTypingThresholdMilliseconds` 与 typing 事件记录。
- 查 mouse wake：`MouseWakeDistance` 与 `_lastMousePoint` 更新逻辑。
- 查动画：`Opacity` 与 `Visibility` 切换是否一致。

最小修复方向：

- 状态机：避免重复进入/退出触发。
- 动画：统一使用 opacity 动画并在结束时设置 Visibility，减少闪烁。

复测点：

- Case3：`immersive_entered`、`immersive_exited`

### E. 侧边栏布局压缩导致编辑区不可用（L004 / L001）

症状：

- sidebar 展开后 editor 区域太窄或被遮挡；按钮无法点击。

诊断：

- 查 `SidebarContainer` 的宽度动画与 `Grid` 列定义。
- 查最小窗口约束：`MinWidth/MinHeight` 与 margin。

最小修复方向：

- 限制 sidebar 展开宽度与 editor 最小宽度。
- 调整 grid 列策略：sidebar Auto + editor *，并确保 margin 不叠加造成裁剪。

复测点：

- Case1（菜单/编辑）、Case3（沉浸切换）

### F. 视觉 diff 轻微超阈但规则通过

症状：

- baseline diff 超阈，但 Layout/State/Legibility 检查都通过。

诊断：

- 判断是否属于预期改动（例如新增按钮、文案变化）。

最小修复方向：

- 如果是非预期：回滚导致差异的样式/布局改动。
- 如果是预期：不要自动更新 baseline；改为输出报告并请求用户确认后再更新。

复测点：

- 对应 checkpoint 重新截图

## 4. 复测策略

每个修复至少复测：

- 直接相关 case/checkpoint
- 至少 1 个相邻高风险 case（例如修保存相关要跑 Case1/Case5）

如果修复涉及 `MainWindow.xaml` 或 `MainWindow.xaml.cs`：

- 必跑 Case1、Case2、Case5（覆盖保存、加载、错误提示）
