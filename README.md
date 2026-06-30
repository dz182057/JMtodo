# JMtodo

Windows 11 桌面 JMtodo 浮窗应用，基于 WPF 和 SQLite。

## 功能

- 管理界面：新增、编辑、完成、恢复未完成、普通删除、查看已删除、恢复删除、彻底删除。
- 搜索筛选：关键字、状态、起始日期、计划完成日期、创建时间、更新时间、不限期任务。
- 桌面浮窗：只显示当前需要处理的未完成任务，支持快速新增和勾选完成。
- 本地持久化：任务保存到 SQLite，浮窗位置和尺寸保存到 JSON。
- 系统托盘：打开管理界面、显示/隐藏浮窗、新增任务、退出应用。

## 运行

需要 Windows 11 和 .NET 8 SDK，或安装包含 .NET 桌面开发工作负载的 Visual Studio。

```powershell
cd TodoDesktopApp
dotnet restore
dotnet run --project .\src\TodoDesktopApp\TodoDesktopApp.csproj
```

数据文件默认保存在：

```text
%LOCALAPPDATA%\TodoDesktopApp\todos.db
%LOCALAPPDATA%\TodoDesktopApp\settings.json
```
