
用 WPF + Milkdown 复刻  typora 。

# 纯粹 · 沉浸式 Markdown 编辑器 (代号: AuraMark)
**文档版本**: v1.1 (Detailed Engineering Edition)
**负责人**: 贺佳
**更新日期**: 2026-03-04

---

## 第一部分：产品需求文档 (PRD)

### 1. 核心交互流与状态机设计
为了保证极其纯粹的输入体验，应用需严格管理以下状态：

* **空闲状态 (Idle)**：未加载文件。主界面显示极简的快捷键提示（如 `Ctrl+N` 新建，`Ctrl+O` 打开）。侧边栏折叠。
* **加载状态 (Loading)**：当读取大型 `.md` 文件（> 5MB）时，前端显示极其轻量的 CSS 骨架屏（Skeleton），不阻塞 UI 线程。
* **编辑状态 (Editing)**：进入“所见即所得”模式。
  * **沉浸模式触发**：当检测到持续键盘输入超过 3 秒，自动隐去侧边栏和顶部自定义状态栏，直到鼠标大范围移动。
* **脏值状态 (Dirty/Saving)**：文本变更触发，右下角状态栏显示极其微弱的 `●` 圆点，500ms 无输入后自动触发静默保存，圆点消失。

### 2. 视觉规范：精美日式卡通风 (Aesthetic Guidelines)
UI 呈现上抛弃传统的工业风，采用高通透感、低对比度、轻盈的视觉语言。

* **色彩系统 (Color Palette)**：
  * **主背景 (Surface)**：`#F9FAFB` (极浅的冷霜白，降低屏幕刺眼感)。
  * **侧边栏 (Sidebar)**：`#F3F4F6` (略带磨砂质感的浅灰，区分层级)。
  * **强调色 (Primary)**：`#81A1C1` (莫兰迪蓝，用于高亮当前文件、选中文本)。
  * **文本色 (Text)**：`#434C5E` (深空灰，避免纯黑 `#000000` 带来的高视觉疲劳)。
* **版式与图形 (Typography & Shapes)**：
  * **全局圆角**：窗体边框 `12px`，内部图片与代码块 `8px`。
  * **字体栈**：中文优先使用 `Noto Sans SC` (思源黑体)，代码块强制使用 `Fira Code` (支持连字特性，如 `=>`, `!=`)。
  * **阴影 (Shadows)**：弃用生硬的 Drop-Shadow，采用多层极柔和的外发光模拟真实纸张悬浮感：`box-shadow: 0 4px 20px rgba(129, 161, 193, 0.08);`

### 3. 快捷键矩阵 (Keymaps)
| 操作 | 快捷键 (Windows) | 触发层级 |
| :--- | :--- | :--- |
| **打开侧边栏** | `Ctrl + Shift + L` | WPF 宿主层拦截 |
| **源码/预览切换** | `Ctrl + /` | WPF 拦截后通过 IPC 发送至前端 |
| **插入代码块** | ``Ctrl + Shift + K`` | 前端 Milkdown 捕获 |
| **加粗 / 斜体** | `Ctrl + B` / `Ctrl + I` | 前端 Milkdown 捕获 |

---

## 第二部分：技术设计文档 (TDD)



### 1. 核心架构：逻辑层与渲染层分离
本项目架构设计类似于现代游戏引擎：
* **WPF (C#)** 作为**主逻辑循环 (Main Logic Loop)**，接管操作系统资源（文件 I/O、窗口句柄、内存分配）。
* **WebView2 + Milkdown** 作为**渲染管线 (Render Pipeline)**，仅负责将传入的 Markdown 字符串转化为 DOM 树并绘制上屏。
两者之间绝对禁止直接内存访问，一切状态同步必须通过异步消息总线（IPC）完成。

### 2. WPF 宿主层详细设计

#### 2.1 无边框沉浸式窗口实现 (`WindowChrome`)
弃用 `WindowStyle="None"` 的粗暴做法（会导致丢失 Windows 原生贴边停靠和动画）。
* **方案**：使用 `System.Windows.Shell.WindowChrome`。
* **实现逻辑**：保留操作系统的 DWM 边框阴影，将 `CaptionHeight` 设置为 0。在 XAML 中顶部放置一个高 `32px` 的透明 `Grid`，设置 `WindowChrome.IsHitTestVisibleInChrome="True"` 作为自定义拖拽热区。

#### 2.2 本地文件系统守护进程 (`FileSystemWatcher`)
为了应对用户使用外部编辑器（如 VS Code）同时修改了该 `.md` 文件：
* 在 C# 端为当前打开的文件挂载 `FileSystemWatcher`。
* 当触发 `Changed` 事件时，暂停前端 WebView2 的输入响应，读取新内容并静默推送到前端执行 `editor.action(replace)`，实现外部修改的热重载。

### 3. 跨进程通信 (IPC) 协议设计


避免传递无结构的纯文本。定义标准的 JSON Payload 契约：

**C# 定义数据契约 (`MessageModels.cs`)：**
```csharp
public class WebMessagePayload
{
    public string Type { get; set; } // 枚举: "Init", "Update", "Command"
    public string Content { get; set; } // Markdown 文本或命令参数
    public long Timestamp { get; set; } 
}

```

**通信时序与节流阀 (Debounce) 控制：**

1. **前端高频输入**：用户连续敲击，Milkdown 产生多个 Change 事件。
2. **JS 拦截**：前端不做拦截，直接将序列化后的 JSON 推送给 `window.chrome.webview.postMessage`。
3. **C# 节流接收**：C# 订阅 `WebMessageReceived`。引入 `System.Reactive` (Rx.NET)：
```csharp
// 使用 Rx.NET 优雅实现 500ms 防抖
Observable.FromEventPattern<CoreWebView2WebMessageReceivedEventArgs>(
    h => MainWebView.CoreWebView2.WebMessageReceived += h,
    h => MainWebView.CoreWebView2.WebMessageReceived -= h)
    .Select(e => e.EventArgs.TryGetAsString())
    .Throttle(TimeSpan.FromMilliseconds(500)) // 停止输入 500ms 后才放行
    .ObserveOnDispatcher() // 切回主 UI 线程
    .Subscribe(SaveToFileToDisk);

```



### 4. WebView2 资源拦截与内存分配

#### 4.1 自定义图片协议底层的 MIME 推断

在 `WebResourceRequested` 拦截本地图片时，必须严格返回正确的 `Content-Type`，否则 Chromium 渲染引擎会拒收。

```csharp
// 简单的基于后缀名的 MIME 推断字典
private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
{
    { ".png", "image/png" },
    { ".jpg", "image/jpeg" },
    { ".jpeg", "image/jpeg" },
    { ".gif", "image/gif" },
    { ".svg", "image/svg+xml" },
    { ".webp", "image/webp" }
};

// ... 在拦截器中应用
string extension = Path.GetExtension(localFilePath);
string mimeType = MimeTypes.TryGetValue(extension, out var mime) ? mime : "application/octet-stream";

```

#### 4.2 内存泄漏阻断机制 (Garbage Collection)

WebView2 控件本质上是对底层 COM 对象的包装。

* **规则**：当在侧边栏关闭或切换大型 Markdown 文件时，必须在 C# 端显式调用 `MainWebView.CoreWebView2.Stop()` 中止当前渲染，随后再加载新文本。
* 如果用户进行了大量图片拖拽操作，WebView2 进程内存会飙升。可通过调用 `MainWebView.CoreWebView2.Profile.ClearBrowsingDataAsync()` 定期清理 Chromium 的图片内存缓存。

### 5. 部署与工程化 (CI/CD)

为确保 C# 与 TypeScript 两套技术栈的顺畅协同：

1. **统一构建入口**：修改 WPF 的 `.csproj` 文件，利用 `<Target>` 在 MSBuild 编译前执行 NPM 脚本。
```xml
<Target Name="BuildFrontend" BeforeTargets="BeforeBuild">
  <Exec Command="npm install" WorkingDirectory="..\AuraMark.Frontend" />
  <Exec Command="npm run build" WorkingDirectory="..\AuraMark.Frontend" />
  <Exec Command="xcopy /y /e /i ..\AuraMark.Frontend\dist $(OutDir)EditorView" />
</Target>

```


2. **发布产物 (Publish)**：采用 `.NET 8.0` 的 `PublishSingleFile`（单文件发布）模式，并将 WebView2 Runtime 设置为 `Evergreen` (依赖系统自带)，最终打包出的 `.exe` 预计体积可控制在 15MB 左右，实现极其轻量的分发。

