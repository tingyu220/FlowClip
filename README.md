# FlowClip

Windows 剪贴板管理器 — 原生 WPF 应用，Apple/macOS 风格界面，快速检索与复用剪贴板历史。

> 当前状态：**M1-B 第二轮 UI 与交互修复完成**（窗口圆角/缩放/最小化 + 视觉优化 + 截图预览重设计 + 粘贴行为）。历史数据为模拟数据，持久化（SQLite）计划于 M2 实现。

## 技术栈

| 项目 | 说明 |
|------|------|
| 框架 | C# / .NET 10 (LTS) |
| UI | WPF + XAML（WindowChrome + DWM 原生圆角，不使用分层窗口） |
| 系统能力 | Win32 API（P/Invoke，无第三方依赖） |
| 架构 | 轻量 MVVM（无 MVVM 框架） |
| 存储 | 暂无（M2 引入 SQLite） |

## 项目结构

```
FlowClip/
├── FlowClip.slnx              # 解决文件（.slnx 新格式）
├── src/
│   ├── FlowClip.App/          # WPF 应用层：窗口、视图、ViewModel、转换器、主题、设置
│   ├── FlowClip.Core/         # 核心层：模型（ClipItem）、剪贴板历史服务、模拟数据与素材
│   └── FlowClip.Windows/      # Win32 层：消息窗口、热键、托盘、剪贴板监听、亚克力、粘贴、主题（无 WPF 依赖）
├── tests/
│   └── FlowClip.Tests/        # xUnit 单元测试（Core 层）
├── docs/
│   ├── screenshots/           # 运行截图（浅色/深色、真实桌面捕获）
│   └── perf/                  # 性能实测结果（benchmark.txt / corner-check.txt）
└── tools/
    └── make_icon.py           # 图标生成脚本（Pillow）
```

## 构建与运行

### 环境

- Windows 10 19041+ / Windows 11
- .NET 10 SDK（本项目使用独立安装于 `F:/Projects/.dotnet`，未污染系统 PATH）

### 命令

```powershell
# 还原 + 构建
F:/Projects/.dotnet/dotnet.exe build FlowClip.slnx -c Debug

# 运行单元测试
F:/Projects/.dotnet/dotnet.exe test FlowClip.slnx -c Debug

# 常规启动（后台托盘运行，Ctrl+Shift+V 唤出面板）
F:/Projects/.dotnet/dotnet.exe run --project src/FlowClip.App/FlowClip.App.csproj -c Debug

# 启动并直接显示主面板（调试 / 真实截图用）
F:/Projects/.dotnet/dotnet.exe run --project src/FlowClip.App/FlowClip.App.csproj -c Debug -- --show

# 自检模式（验证剪贴板监听 + 热键注册 + 内存占用，结果写入 %LOCALAPPDATA%/FlowClip/logs/selftest.result）
F:/Projects/.dotnet/dotnet.exe run --project src/FlowClip.App/FlowClip.App.csproj -c Debug -- --selftest

# 离屏截图（浅色/深色主面板、分类视图与预览窗，输出 docs/screenshots/）
F:/Projects/.dotnet/dotnet.exe run --project src/FlowClip.App/FlowClip.App.csproj -c Debug -- --capture

# 真实桌面截图 + 四角黑边数值检测（BitBlt 屏幕 DC，含深浅双主题）
F:/Projects/.dotnet/dotnet.exe run --project src/FlowClip.App/FlowClip.App.csproj -c Debug -- --realcapture

# 拖动行为验证（SendInput 模拟真实鼠标：标题栏空白可拖 / 按钮不误拖）
F:/Projects/.dotnet/dotnet.exe run --project src/FlowClip.App/FlowClip.App.csproj -c Debug -- --dragtest

# 托盘图标验证（截取任务栏托盘区并统计徽标配色像素）
F:/Projects/.dotnet/dotnet.exe run --project src/FlowClip.App/FlowClip.App.csproj -c Debug -- --trayshot

# 性能实测（空闲内存 / 100 次开关面板的唤出耗时与内存曲线，输出 docs/perf/）
F:/Projects/.dotnet/dotnet.exe run --project src/FlowClip.App/FlowClip.App.csproj -c Debug -- --bench
```

### 使用方式

1. 启动后应用驻留系统托盘。
2. 任意应用中复制内容（静默，无任何弹窗打断）。
3. 按 `Ctrl+Shift+V` 唤出历史面板；再按或按 `Esc` 隐藏（隐藏后焦点自动还给之前的窗口）。
4. 面板内：`↑/↓` 选择 · `Enter` 复制并尝试自动粘贴 · `P` 置顶 · `Delete` 删除。
   - 自动粘贴仅在目标窗口确定有效且可激活时执行；失败时降级为「已复制」托盘提示，手动 `Ctrl+V` 即可。
5. 窗口可拖边缘缩放（默认 600×480，最小 420×320），尺寸自动记忆；`—` 最小化到任务栏，`✕`/Esc 仅隐藏，托盘「退出」才结束进程。
   - **拖动**：整条标题栏（含空白处）与底部提示条都可拖动；标题栏三个按钮上的按压不会被当作拖动。
6. 托盘右键菜单：打开面板 / 截图预览 / 切换主题 / 退出。

## 第二轮（M1-B）已实现

- ✅ **窗口本体**：WindowChrome + DWM 原生圆角（`DWMWA_WINDOW_CORNER_PREFERENCE`），内容圆角与系统裁剪半径对齐，浅/深主题下四角均无黑色矩形残留（附数值检测）；`DWMWA_BORDER_COLOR` 去除深色模式默认黑边；Acrylic AccentFlags 声明四边绘制
- ✅ **窗口行为**：自由缩放（记忆尺寸）+ 最小化（保留任务栏恢复）+ 关闭=隐藏 + 托盘退出；三种操作语义分离
- ✅ **视觉**：600×480 紧凑默认尺寸、头部/底部提示降权、选中卡片改为低饱和底色+淡边框+左侧强调线、去除系统虚线焦点框、细滚动条（Hover 增强）
- ✅ **文件展示**：按扩展名取系统真实图标（SHGetFileInfo + 注册表 DefaultIcon 双策略）、图片真实缩略图（按目标尺寸异步解码 + 有界缓存）、多文件数量角标、源文件失效红色标记、长路径省略 + ToolTip 查看完整路径
- ✅ **截图预览重设计**：右上角缩略图（≤280px、等比限高、20px 边距）、4 秒自动收起 + Hover 暂停、位移动画 + 淡出、不抢焦点；按钮仅保留 复制/贴屏/关闭，「贴屏」未实现显式禁用；来源标注区分自发起截图与第三方图片
- ✅ **交互**：Enter = 复制 → 收起 → 焦点恢复 → 尝试自动粘贴（严格校验目标窗口，失败降级托盘气泡提示）；失效条目拒绝写入剪贴板
- ✅ **性能**：列表虚拟化 + 容器回收、卡片去独立阴影、后台无常驻动画、剪贴板事件通知（无轮询）
- ✅ **托盘图标**：嵌入资源 `Assets/icon.ico` → 临时文件 → LoadImage(32×32)，ExtractIconEx(16×16) 兜底；首次运行弹一次气泡提示（Windows 常把新图标收进「隐藏的图标」区）；诊断细节写入 `%LOCALAPPDATA%/FlowClip/logs/tray.log`
- ✅ **拖动**：整条标题栏 + 底部提示条可拖动（Grid 需显式 `Background="Transparent"` 才会参与命中测试），按钮按压已排除

## 自检与实测结果（真实运行）

自检：

```
clipboard_detected=True      # 剪贴板变化被真实捕获
hotkey_registered=True       # 全局热键注册成功
memory_workingset_mb=85.66   # 启动后工作集内存
dark_mode=True               # 跟随系统深色
mock_items=13                # 13 条模拟历史数据（含失效对照）
```

性能实测（`--bench`，Debug 构建）：

```
后台空闲：私有 45.3 MB / 工作集 84.0 MB
唤出耗时（100 次）：平均 15.1 ms · P95 17.3 ms · 最大 334.6 ms（首次 JIT）
连续开关 100 次（GC 后采样）：私有内存 86.2 → 85.5 MB，无泄漏趋势（增量属 JIT/缓存预热）
```

四角黑边检测（`--realcapture`，BitBlt 真实屏幕）：浅/深主题四角近黑像素占比均 ≈0（仅剩 1px 抗锯齿过渡）。

截图见 `docs/screenshots/`：`real-desktop-dark/light.png`（真实桌面捕获）、`main-dark/light.png`、`files-light.png`（系统文件图标）、`multi-light.png`（数量角标 + 失效标记）、`images-light.png`（真实缩略图）、`preview-dark/light.png`（预览窗）。

## 已知边界（诚实声明）

- **历史列表为模拟数据**：指向 `%LOCALAPPDATA%/FlowClip/mock/` 下真实生成的占位文件，图标/缩略图/大小/失效判定均走真实逻辑；与真实剪贴板监听相互独立。
- **自动粘贴依赖窗口激活**：跨进程 `SetForegroundWindow` 受系统前台锁定策略限制，个别场景会降级为「已复制」提示（属预期行为，不误输入）。
- **截图预览内容为本地素材**：预览管线真实（缩略图解码、自动收起、动画），但真实截图捕获功能在 M3。
- **未做 DPI 多档实测**：100% 下已验证；125%/150% 需在不同显示设置下人工回归（列入待办）。
- **无持久化**：M2 引入 SQLite 后历史才能真正留存。
- 未实现（按需求刻意不做）：OCR、翻译、付费、云同步、「贴屏」。

## 路线图

- **M1-C（建议）**：真实剪贴板内容入列（文本/图片/文件全格式）、面板与监听打通、单实例互斥（Mutex）、125%/150% DPI 回归
- **M2**：SQLite 持久化、历史去重与容量上限、开机自启
- **M3+**：真实截图捕获与「贴屏」、OCR / 翻译等按需扩展
