# AuraMark WebView2 移除 — 剩余 TODO

> 创建时间：2026-03-16 20:56
> 背景：已完成大部分 WebView2 代码清理，但编译未通过，以下为剩余工作

## 已完成 ✅

1. SourceEditorHost 设为默认可见，WebViewHost 设为 Collapsed
2. SourceModeToggleButton.IsEnabled = true
3. TryExecuteSourceEditorAction 完整实现（接入 MarkdownEditorReducer）
4. UpdateSourceEditorActionStates 完整实现（接入 SourceEditorActionStateFactory）
5. MainWindow.xaml.cs 中 WebView2 相关代码删除（~619 行）
6. MainWindow.xaml 中 WebView2 引用清理（wv2 命名空间、WebView2 控件仍保留但 Collapsed）
7. csproj 中 WebView2 PackageReference 已移除，BuildFrontend/CopyFrontend MSBuild Target 已移除
8. IpcContracts.cs 已删除
9. AuraMark.Web 目录已删除
10. wpftmp 临时文件已清理
11. ErrorCodes 引用已替换为直接字符串
12. RestoreProjectStyle=None 已移除
13. 删除了 src/AuraMark.App/obj 和 bin 目录

## 未完成 ❌

### 1. 编译错误：Microsoft.CodeAnalysis.Common NuGet 包还原失败

**问题**：csproj 新增了 `<PackageReference Include="Microsoft.CodeAnalysis.Common" Version="4.11.0" />`，但 Codex 沙箱无法写 obj 目录，导致 `dotnet restore` 失败（NU1301 + Access denied）。

**解决方案**：
```powershell
# 在非沙箱环境（直接 PowerShell）执行：
cd C:\Dev\AuraMark
dotnet restore src/AuraMark.sln
dotnet build src/AuraMark.sln
```

如果 restore 仍有网络问题，检查是否有代理（127.0.0.1:9 被拒绝）。

### 2. 决策：是否需要 CodeAnalysis 依赖

Core 层（AuraMark.Core.csproj）和 App 层都依赖 `Microsoft.CodeAnalysis.Text`（提供 `TextSpan`、`SourceText` 类型）。

**两个选择**：
- **方案 A（快速）**：保留 CodeAnalysis.Common 包引用，让它正常 restore/build
- **方案 B（长期）**：在 Core 层定义自己的 TextSpan/SourceText 类型，完全移除 CodeAnalysis 依赖

### 3. MainWindow.xaml 中 WebView2 控件的最终处理

当前状态：`WebViewHost` Grid 和 `<wv2:WebView2 x:Name="MainWebView" />` 仍然存在于 XAML 中，只是 `Visibility="Collapsed"`。

**待做**：
- 从 XAML 中删除 `WebViewHost` Grid 和 `MainWebView` 控件
- 删除 `xmlns:wv2="clr-namespace:Microsoft.Web.WebView2.Wpf;assembly=Microsoft.Web.WebView2.Wpf"` 命名空间声明
- 注意：删除前确保 MainWindow.xaml.cs 中没有任何引用 MainWebView 的残留代码

### 4. 从 AuraMark.sln 中移除 AuraMark.Web 项目引用

检查 `src/AuraMark.sln` 是否包含 AuraMark.Web 的 SolutionItem/Project 引用，如果有需要删除。

### 5. EditorActions/EditorActionModels.cs 清理

Codex 删除了该文件中 11 行与 WebView2 相关的代码（如 WebActionState 等），需确认是否干净。

### 6. 编译和测试验证

```powershell
dotnet build src/AuraMark.sln       # 应 0 error
dotnet test src/AuraMark.Core.Tests # 应 36/36 passed
```

### 7. Codex + Gemini 双重审核

编译通过后，启动 Codex 和 Gemini 同时检查：
- WebView2 是否彻底移除（全局搜索 `WebView2|wv2:|Microsoft.Web.WebView2`）
- IpcContracts 是否彻底移除
- AuraMark.Web 目录是否彻底移除
- 编译和测试是否通过
- SourceEditorHost 是否为默认编辑器

### 8. Git Commit

确认全部完成后，使用 git-commit-workflow 技能提交：
```
refactor: 移除 WebView2 依赖，WPF 原生 Surface 成为主编辑器
```

## 注意事项

- Codex 沙箱不允许删除文件（Remove-Item 被拦截），需要在宿主 PowerShell 直接执行
- Codex 沙箱无法写 obj/bin 目录，NuGet restore 必须在宿主执行
- 中文括号 `（）` 会导致 `codex exec` 解析失败，提示中必须用英文括号 `()`
