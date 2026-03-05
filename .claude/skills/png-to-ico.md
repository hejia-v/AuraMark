---
name: png-to-ico
description: 将指定 PNG 转换为 Windows 多尺寸 ICO 格式，并将其配置为 AuraMark WPF 应用的程序图标（.exe 图标 + 窗口/任务栏图标）。
---

# PNG → ICO 图标转换

## 何时使用

当用户提供一个 PNG 文件，希望将其设置为 AuraMark 应用图标时使用。转换后会自动完成：

- 生成标准多尺寸 ICO（16×16、32×32、48×48、256×256）
- 配置 `.csproj` 的 `<ApplicationIcon>`（Windows 资源管理器中 .exe 文件的图标）
- 配置 `MainWindow.xaml` 的 `Icon` 属性（窗口标题栏 + 任务栏图标）

## 输入

- `$InputPng`：用户指定的 PNG 文件路径（绝对路径或相对于仓库根的路径）

## 执行步骤

### 1. 转换 PNG → ICO（PowerShell + System.Drawing）

运行以下 PowerShell 脚本，输出路径固定为 `src/AuraMark.App/app.ico`：

```powershell
Add-Type -AssemblyName System.Drawing

$inputPng  = "<USER_PROVIDED_PNG>"          # 替换为实际路径
$outputIco = "C:\Dev\AuraMark\src\AuraMark.App\app.ico"
$sizes     = @(16, 32, 48, 256)

$srcBitmap    = [System.Drawing.Bitmap]::new($inputPng)
$imageStreams  = [System.Collections.Generic.List[byte[]]]::new()

foreach ($size in $sizes) {
    $bmp = [System.Drawing.Bitmap]::new($size, $size)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.DrawImage($srcBitmap, 0, 0, $size, $size)
    $g.Dispose()

    $pngStream = [System.IO.MemoryStream]::new()
    $bmp.Save($pngStream, [System.Drawing.Imaging.ImageFormat]::Png)
    $imageStreams.Add($pngStream.ToArray())
    $pngStream.Dispose()
    $bmp.Dispose()
}
$srcBitmap.Dispose()

$ms     = [System.IO.MemoryStream]::new()
$writer = [System.IO.BinaryWriter]::new($ms)

# ICO 文件头（6 字节）
$writer.Write([uint16]0)             # Reserved
$writer.Write([uint16]1)             # Type: ICO
$writer.Write([uint16]$sizes.Count)  # 图像数量

# 目录条目（每条 16 字节），先计算各条目的偏移
$dataOffset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz        = $sizes[$i]
    $displaySz = if ($sz -ge 256) { 0 } else { $sz }   # 256 在 ICO 格式中用 0 表示
    $writer.Write([byte]$displaySz)                      # Width
    $writer.Write([byte]$displaySz)                      # Height
    $writer.Write([byte]0)                               # ColorCount
    $writer.Write([byte]0)                               # Reserved
    $writer.Write([uint16]1)                             # Planes
    $writer.Write([uint16]32)                            # BitCount
    $writer.Write([uint32]$imageStreams[$i].Length)      # SizeInBytes
    $writer.Write([uint32]$dataOffset)                   # Offset
    $dataOffset += $imageStreams[$i].Length
}

foreach ($data in $imageStreams) { $writer.Write($data) }
$writer.Flush()
[System.IO.File]::WriteAllBytes($outputIco, $ms.ToArray())
$writer.Dispose(); $ms.Dispose()

Write-Host "Done: $outputIco"
```

### 2. 更新 AuraMark.App.csproj

在 `<PropertyGroup>` 中添加：
```xml
<ApplicationIcon>app.ico</ApplicationIcon>
```

添加新的 `<ItemGroup>`（让 WPF 能以 Pack URI 引用该图标）：
```xml
<ItemGroup>
  <Resource Include="app.ico" />
</ItemGroup>
```

### 3. 更新 MainWindow.xaml

在 `<Window ...>` 元素上添加属性：
```xml
Icon="app.ico"
```

### 4. 验证

```powershell
dotnet build C:\Dev\AuraMark\src\AuraMark.App -c Debug --no-restore
```

构建成功即完成。

## 注意事项

- `System.Drawing` 在 .NET 8 Windows 上可用，无需额外安装。
- 256×256 尺寸存储为 PNG 流（ICO 规范 Vista+ 扩展），兼容 Windows 7+。
- 如需更换图标，重新运行步骤 1 覆盖 `app.ico` 后重新构建即可。
