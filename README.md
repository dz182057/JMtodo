# JMtodo

JMtodo 是一个基于 WPF 的 Windows 桌面待办工具，面向需要在桌面上持续跟踪当前任务的人。它提供完整的任务管理窗口、轻量桌面浮窗、系统托盘入口、任务分组、子任务、本地 SQLite 持久化和多条件筛选。

项目不绑定某个单一 Windows 发行版。当前最低支持版本为 Windows 10 1809（build 17763）或更高版本；推荐使用仍处于 Microsoft 支持周期内的 Windows 版本。

## 功能特性

- 任务管理：新增、编辑、完成、恢复未完成、软删除、查看已删除任务、恢复删除和彻底删除。
- 子任务层级：主任务下可创建子任务，管理界面和浮窗都会按层级展示。
- 任务分组：支持创建、编辑、删除任务组，并为任务组选择图标。
- 桌面浮窗：只展示已经开始且仍未完成的当前任务，支持快速完成、重新打开、编辑、创建子任务和拖拽排序。
- 系统托盘：左键打开管理界面，右键打开托盘菜单，可快速显示/隐藏浮窗、新增任务或退出应用。
- 搜索筛选：支持关键字、任务状态、任务组、无分组、开始日期、计划完成日期、创建时间、更新时间和不限期任务筛选。
- 本地存储：任务数据保存在 SQLite 数据库中，浮窗位置和尺寸等设置保存在 JSON 文件中。
- 桌面体验：包含现代化日期选择器、任务组图标选择器、表格选中态、滚动条样式和浮窗贴边交互。

## 技术栈

- .NET 8
- WPF
- Windows Forms NotifyIcon（用于系统托盘）
- Microsoft.Data.Sqlite
- MVVM 风格的 ViewModel 与命令绑定

## 系统要求

运行源码或开发项目需要：

- Windows 10 1809（build 17763）或更高版本
- .NET 8 SDK
- 可选：Visual Studio 2022，并安装“.NET 桌面开发”工作负载

运行发布后的 self-contained 版本时，目标机器不需要预装 .NET Runtime；如果发布为 framework-dependent 版本，则目标机器需要安装 .NET 8 Desktop Runtime。

## 快速开始

```powershell
git clone git@github.com:dz182057/JMtodo.git
cd JMtodo
dotnet restore
dotnet run --project .\src\JMtodo\JMtodo.csproj
```

也可以直接用 Visual Studio 打开 `JMtodo.sln`，将 `JMtodo` 设为启动项目后运行。

## 构建

```powershell
dotnet build .\JMtodo.sln -c Debug
```

Release 构建：

```powershell
dotnet build .\JMtodo.sln -c Release
```

## 发布

生成 win-x64 self-contained 发布目录：

```powershell
dotnet publish .\src\JMtodo\JMtodo.csproj -c Release -r win-x64 --self-contained true -o .\artifacts\JMtodo-win-x64
```

生成依赖本机 .NET 8 Desktop Runtime 的发布目录：

```powershell
dotnet publish .\src\JMtodo\JMtodo.csproj -c Release -r win-x64 --self-contained false -o .\artifacts\JMtodo-win-x64-runtime
```

## 数据位置

JMtodo 默认将本地数据写入当前用户目录：

```text
%LOCALAPPDATA%\JMtodo\todos.db
%LOCALAPPDATA%\JMtodo\settings.json
```

其中 `todos.db` 保存任务、子任务和任务组；`settings.json` 保存浮窗位置、尺寸和置顶等界面设置。

如果你曾运行过旧目录名版本，旧数据可能位于：

```text
%LOCALAPPDATA%\TodoDesktopApp\todos.db
%LOCALAPPDATA%\TodoDesktopApp\settings.json
```

当前版本不会自动迁移旧数据。如需保留旧数据，请先退出应用，再手动将旧目录中的文件复制到 `%LOCALAPPDATA%\JMtodo\`。

## 目录结构

```text
JMtodo/
├─ JMtodo.sln
├─ README.md
└─ src/
   └─ JMtodo/
      ├─ Assets/        应用图标资源
      ├─ Controls/      自定义控件
      ├─ Data/          SQLite 仓储
      ├─ Dialogs/       对话框窗口
      ├─ Models/        任务、分组和筛选模型
      ├─ Services/      任务服务、设置服务、托盘服务
      ├─ Styles/        WPF 样式资源
      ├─ ViewModels/    主窗口、浮窗和编辑窗口 ViewModel
      ├─ Views/         任务编辑、任务组管理和托盘菜单窗口
      ├─ App.xaml
      ├─ MainWindow.xaml
      ├─ FloatingTaskWindow.xaml
      └─ JMtodo.csproj
```

## 常见问题

### 浮窗为什么没有显示？

浮窗只展示“开始日期不晚于今天”的未完成任务。如果没有符合条件的任务，浮窗会自动隐藏。

### 删除任务后还能恢复吗？

普通删除会把任务标记为已删除，可以在已删除视图中恢复；彻底删除会直接从 SQLite 数据库中移除。

### 数据会上传到云端吗？

不会。JMtodo 当前只使用本机 SQLite 和 JSON 文件保存数据。

## 贡献

欢迎提交 issue 和 pull request。建议在提交前执行：

```powershell
dotnet restore
dotnet build .\JMtodo.sln -c Release
```

请保持改动聚焦，避免把功能修改、格式化和无关重构混在同一次提交中。
