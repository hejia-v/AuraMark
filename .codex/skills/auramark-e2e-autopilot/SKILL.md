---
name: auramark-e2e-autopilot
description: AuraMark 端到端自愈开发闭环（实现-编译-测试-启动-截图-日志与UI检查-自动修复-复测），在最大迭代次数内追求全绿或输出阻塞原因。
---

# AuraMark E2E Autopilot

## 何时使用

当用户要求对 `C:\Dev\AuraMark` 做端到端交付，并希望 Codex 自动完成以下闭环时使用本 skill：

- 实现需求（feature/bugfix）
- 自我编译（frontend + .NET）
- 自动化测试（unit/integration/E2E，如存在）
- 自动启动应用并执行 UI 自动化场景
- 自动截图并分析 UI/UX（规则化检查 + 视觉回归）
- 发现问题后自动修复并进入下一轮
- 最终全绿或给出明确的 blocker 报告

常见触发语句（示例）：

- “端到端开发并自动测试”
- “自动截图检查 UI/UX 并修复”
- “自愈式迭代直到通过”
- “build + test + run + fix loop”

## 范围与安全边界（必须遵守）

1. 仅面向仓库 `C:\Dev\AuraMark` 的开发与验证。
2. 绝不无限循环：默认 `max_iterations = 4`（可配置 1-6）。
3. 失败快速退出（fail-fast）场景：
   - 构建工具不可用（`dotnet`/`npm` 等）
   - 应用无法启动或稳定崩溃
   - 关键验收路径阻断且不可自动修复
4. “自动修复”只针对规则明确、可回归验证的问题；禁止为了过测试做投机改动。
5. 任何与当前需求无关的改动都应避免；如果发现工作区存在与本次任务无关的用户改动，不回滚、不覆盖。

## 默认输入与可推断项

优先从用户话术与仓库现状推断以下信息，缺失时用默认值继续推进：

- 需求目标与验收标准（Acceptance Criteria）
- 目标配置：默认 `Debug`
- 运行平台：Windows（WPF + WebView2）
- 测试现状：若仓库没有 test project，则先做 smoke/acceptance，再建议补测试基建

## 执行总流程（闭环）

每次迭代（iteration）遵循相同顺序，确保可复现：

1. 需求拆解：把需求转换为可验证的验收点与自动化检查点（checkpoints）。
2. 最小实现：只改与验收点相关的代码。
3. Build Gate：前端构建 + .NET 构建必须通过。
4. Test Gate：尽可能运行自动测试；若不存在测试，则运行 smoke/acceptance 脚本并补充可执行检查。
5. Run Gate：启动 `AuraMark.App.exe` 并执行 UI 自动化场景（可先覆盖 PRD 6.3）。
6. Evidence：采集 logs + screenshots，并归档到 run folder。
7. Diagnose：解析失败原因（编译/测试/运行时错误/UX 规则/视觉回归）。
8. Auto-fix：对“允许自动修复”的问题执行最小补丁（见 `references/fix-playbook.md`）。
9. Repeat：进入下一轮，直到全绿或达到 `max_iterations`。

## 脚本入口（可执行 Harness）

本 skill 附带 PowerShell 脚本用于“可重复跑”的验证环节；代码修改与自动修复由 Codex 结合报告执行。

主要入口：

- `scripts/run-loop.ps1`: 跑一轮 gates（build/run/log/diff），输出 `reports/summary.json`
- `scripts/seed-baseline.ps1`: 将本次 `screenshots/current` 写入持久 `baseline`（默认只补缺失）

推荐用法（从任意目录运行）：

```powershell
$repo = "C:\Dev\AuraMark"
$skill = "C:\Dev\AuraMark\.codex\skills\auramark-e2e-autopilot"

powershell -ExecutionPolicy Bypass -File "$skill\scripts\run-loop.ps1" -RepoRoot $repo -Configuration Debug
```

第一次建立 baseline（建议人工确认截图正确后再做）：

1. 先运行 `run-loop.ps1` 一次，得到 run folder 路径（脚本会打印 `Run folder:`）。
2. 再执行：

```powershell
$run = "C:\Dev\AuraMark\artifacts\e2e-YYYYMMDD-HHMMSS"
$baseline = "C:\Dev\AuraMark\artifacts\ui-baseline"

powershell -ExecutionPolicy Bypass -File "$skill\scripts\seed-baseline.ps1" -RunRoot $run -BaselineRoot $baseline
```

之后每次 `run-loop.ps1` 会默认用 `artifacts/ui-baseline` 做 diff。

## 通过条件（Success Gates）

全部满足才算 pass：

1. `npm` build 成功（若适用）。
2. `dotnet build` 成功。
3. 所有可运行的自动测试通过（若存在）。
4. 日志无高危错误（见下方 Log Rules）。
5. UI/UX 检查通过（规则检查 + 截图差异在阈值内）。

## 基线命令（AuraMark 当前仓库）

在 repo root 执行：

```powershell
npm.cmd ci --prefix src/AuraMark.Web
npm.cmd run build --prefix src/AuraMark.Web
dotnet build src/AuraMark.sln -c Debug
powershell -ExecutionPolicy Bypass -File .\scripts\acceptance.ps1 -Configuration Debug
```

如果存在测试项目（未来引入 xUnit/NUnit/MSTest/E2E runner 等），追加：

```powershell
dotnet test src/AuraMark.sln -c Debug
```

## Log 规则（高危/中危）

高危（必须修复，否则本轮失败）：

- `Unhandled Exception`
- `NullReferenceException` 等未处理异常
- WebView2 初始化/加载的 fatal 错误
- 启动失败、反复崩溃、卡死

中危（需要解释并尽量修复）：

- 可恢复的保存失败，但没有成功 retry
- 影响 PRD 6.3 交互的重复 warning

## 截图与 UI/UX 规则

规则、阈值、截图命名与检查点在 `references/rules-ui.md`。

## 自动修复策略

允许与禁止的修复类别、以及“症状 -> 代码位点 -> 最小补丁”映射在 `references/fix-playbook.md`。

## 报告输出（每次闭环结束）

输出应包含：

- 本次迭代次数与停止原因（pass / exceeded max_iterations / blocker）
- 变更文件列表与变更要点
- Gate 结果：build/test/run/log/ui
- 失败签名（错误摘要）与对应证据路径（logs/screenshots）
- 残留风险与下一步建议（例如补测试基建、加 AutomationId）

## AuraMark 项目特定注意事项

1. 优先复用 `C:\Dev\AuraMark\scripts\acceptance.ps1` 作为 smoke/acceptance harness。
2. UI 自动化稳定性依赖 WPF 控件的 `AutomationProperties.AutomationId`，在扩展 E2E 覆盖前应先补齐关键控件。
3. PRD 6.3 的关键用例是第一优先级（新建/打开/自动保存/大文件/沉浸模式/外部热更新/保存失败提示）。
