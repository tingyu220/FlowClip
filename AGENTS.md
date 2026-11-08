# AGENTS.md — FlowClip 代理开发约定

本文件供 AI 代理与后续开发者遵循。修改代码前请先阅读并遵守。

## 项目定位

Windows 原生剪贴板管理器。当前处于 **M1-A（首轮）已完成** 状态，按 Phase 阶段制推进，禁止跨阶段实现（M2 才做 SQLite，M3 才做真实截图/OCR）。

## 技术约束（不可违反）

1. **技术栈固定**：C# + .NET 10 LTS + WPF/XAML + Win32 API（P/Invoke）。不引入第三方运行时依赖（当前 0 NuGet 依赖，xUnit 仅限测试项目）。
2. **架构分层**：
   - `FlowClip.Core` — 纯逻辑（模型 / 服务 / 模拟数据），无 WPF / Win32 依赖
   - `FlowClip.Windows` — Win32 原生能力，**不得引用 WPF/WinForms**（保持零依赖）
   - `FlowClip.App` — WPF 表现层，唯一可引用两者
   - 分层引用方向 `App → Windows → Core`，禁止反向
3. **禁止过度抽象**：不为"架构完整性"创建无用接口/工厂/基类。三处以下重复可直接复制。
4. **轻量 MVVM**：手写 INotifyPropertyChanged + RelayCommand，不引入 MVVM 框架。

## 关键实现细节（改动前必读）

- **消息窗口**：`MessageWindow` 用 `RegisterClassEx`/`CreateWindowEx` 创建 message-only 窗口（parent = HWND_MESSAGE）。**必须显式 `CharSet = CharSet.Unicode`**（Win32.cs 中已注释），否则 ANSI/Unicode 混用导致 1407「找不到窗口类」。hInstance 必须传 `GetModuleHandle(null)` 与注册时一致。
- **线程模型**：MessageWindow 必须在 UI 线程创建，由 WPF Dispatcher 泵消息；热键 / 剪贴板 / 托盘回调都挂它的 WndProc。
- **剪贴板监听**：`AddClipboardFormatListener` + `WM_CLIPBOARDUPDATE`，事件驱动，**禁止轮询**。验证日志只记格式摘要，不落敏感内容。
- **热键**：只用 `Ctrl+Shift+V`；**绝不注册 Ctrl+C / Ctrl+V**，必须带 `MOD_NOREPEAT`。
- **托盘图标**：嵌入资源解压到临时文件后 `LoadImage` 读取，退出时清理临时文件与 NOTIFYICONDATA。
- **亚克力**：`DwmExtendFrameIntoClientArea` + `SetWindowCompositionAttribute`；调用失败必须回退半透明纯色，不得崩溃。

## 代码风格

- 注释：**简体中文**
- 标识符（类型 / 变量 / 方法）：**英文**
- 命名空间：`FlowClip.<层>.<子域>`
- XAML 资源统一放 `Resources/DesignSystem.xaml`（设计令牌 + 控件样式）与 `Resources/ThemeLight|Dark.xaml`（主题画刷，运行时用 DynamicResource 引用）
- **样式 TargetType 必须与使用元素一致**（ToggleButton 样式不能套 Button，反之亦然）

## 构建与验证

```powershell
# .NET 10 SDK 独立安装于 F:/Projects/.dotnet（勿假设 PATH 中有 dotnet）
F:/Projects/.dotnet/dotnet.exe build FlowClip.slnx -c Debug   # 必须保持 0 错误
F:/Projects/.dotnet/dotnet.exe test  FlowClip.slnx -c Debug   # 4 个 Core 层测试必须全过
```

- 运行时验证用 `--selftest`（结果写 `%LOCALAPPDATA%/FlowClip/logs/selftest.result`）
- 截图用 `--capture`（离屏渲染，输出 docs/screenshots/*.png）

## Git 约定

- **未经用户确认，禁止 commit / push / merge**。
- 建议分支名：`feat/m1-bootstrap`（M1 首轮）。
- 提交信息格式：`<type>: <简述>`，如 `feat: M1-A 剪贴板监听与历史面板骨架`。

## 阶段边界

- M1-A 已完成：骨架 / UI / 模拟历史面板 / 剪贴板监听验证 / 截图预览原型
- M1-B（建议下一轮）：真实剪贴板内容入列、面板与监听打通、Mutex 单实例
- M2：SQLite 持久化（才开始建库表）
- **禁止**：OCR、翻译、付费、云同步（需求明确不做）
