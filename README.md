# JMtodo - Windows Desktop Todo App / 桌面待办工具

JMtodo is a lightweight Windows desktop todo app built with WPF and .NET 8. It focuses on local-first task management, a floating task panel, system tray access, task groups, subtasks, attachments, SQLite storage, and fast filtering.

JMtodo 是一个基于 WPF 和 .NET 8 的 Windows 桌面待办工具，适合需要在桌面持续跟踪当前任务、快速处理待办事项、并希望数据保存在本机的用户。

## Highlights / 功能特性

- Internationalized UI: supports Simplified Chinese and English, with language preference saved locally.
- Task management: create, edit, complete, reopen, soft delete, restore, and permanently delete tasks.
- Subtasks: create one level of subtasks under a main task.
- Task groups: create, edit, delete, and choose icons for task groups.
- Floating task panel: shows current active tasks on the desktop, supports quick completion, editing, subtask creation, attachments, and drag sorting.
- System tray menu: open manager, show or hide the floating panel, add a task, or exit.
- Advanced filters: keyword, status, group, ungrouped tasks, start date, due date, created time, updated time, and no-due-date tasks.
- Local storage: tasks are saved in SQLite; UI settings are saved in JSON.
- Desktop polish: custom date pickers, group icon picker, modern DataGrid selection, scrollbars, and edge snapping for the floating panel.

## Search Keywords / 多语言搜索关键词

Windows todo app, desktop todo list, task manager app, productivity app, WPF todo, .NET 8 WPF app, local SQLite todo, floating task window, system tray todo, offline task manager, GTD desktop app, task groups, subtasks, todo attachments.

中文：桌面待办、Windows 待办软件、任务管理工具、悬浮待办窗口、本地待办、SQLite 待办、任务分组、子任务、系统托盘待办。

Español: lista de tareas, aplicación de tareas para Windows, gestor de tareas de escritorio, tareas locales.  
Français: application de tâches Windows, liste de tâches bureau, gestionnaire de tâches local.  
Deutsch: Aufgabenverwaltung Windows, Desktop To-do-App, lokale Aufgabenliste.  
日本語: Windows タスク管理, デスクトップ ToDo アプリ, ローカル ToDo リスト.  
한국어: Windows 할 일 앱, 데스크톱 작업 관리, 로컬 할 일 목록.  
Português: aplicativo de tarefas Windows, lista de tarefas desktop, gerenciador de tarefas local.

## Tech Stack / 技术栈

- .NET 8
- WPF
- Windows Forms NotifyIcon for the system tray
- Microsoft.Data.Sqlite
- MVVM-style ViewModels and command binding

## Requirements / 系统要求

For development:

- Windows 10 1809, build 17763, or later
- .NET 8 SDK
- Optional: Visual Studio 2022 with the ".NET desktop development" workload

For published self-contained builds, the target machine does not need a preinstalled .NET Runtime. Framework-dependent builds require the .NET 8 Desktop Runtime.

## Quick Start / 快速开始

```powershell
git clone git@github.com:dz182057/JMtodo.git
cd JMtodo
dotnet restore
dotnet run --project .\src\JMtodo\JMtodo.csproj
```

You can also open `JMtodo.sln` in Visual Studio, set `JMtodo` as the startup project, and run it.

## Build / 构建

```powershell
dotnet build .\JMtodo.sln -c Debug
dotnet build .\JMtodo.sln -c Release
```

## Publish / 发布

Self-contained win-x64 build:

```powershell
dotnet publish .\src\JMtodo\JMtodo.csproj -c Release -r win-x64 --self-contained true -o .\artifacts\JMtodo-win-x64
```

Framework-dependent win-x64 build:

```powershell
dotnet publish .\src\JMtodo\JMtodo.csproj -c Release -r win-x64 --self-contained false -o .\artifacts\JMtodo-win-x64-runtime
```

## Data Location / 数据位置

JMtodo stores data under the current user's local app data folder:

```text
%LOCALAPPDATA%\JMtodo\todos.db
%LOCALAPPDATA%\JMtodo\settings.json
%LOCALAPPDATA%\JMtodo\attachments\
```

`todos.db` stores tasks, subtasks, groups, and attachment metadata. `settings.json` stores floating window size, position, and language preference.

If you used an older build with the previous app directory name, old data may be under:

```text
%LOCALAPPDATA%\TodoDesktopApp\todos.db
%LOCALAPPDATA%\TodoDesktopApp\settings.json
```

JMtodo does not automatically migrate this legacy directory. To keep old data, exit the app first, then copy the old files into `%LOCALAPPDATA%\JMtodo\`.

## Project Structure / 目录结构

```text
JMtodo/
├─ JMtodo.sln
├─ README.md
└─ src/
   └─ JMtodo/
      ├─ Assets/        app icons
      ├─ Controls/      custom WPF controls
      ├─ Data/          SQLite repository
      ├─ Dialogs/       dialog windows
      ├─ Localization/  UI string resources
      ├─ Models/        task, group, filter, and attachment models
      ├─ Services/      todo, settings, tray, window-level, and localization services
      ├─ Styles/        WPF resource dictionaries
      ├─ ViewModels/    main, floating, editor, and group ViewModels
      ├─ Views/         editor, group manager, and tray menu windows
      ├─ App.xaml
      ├─ MainWindow.xaml
      ├─ FloatingTaskWindow.xaml
      └─ JMtodo.csproj
```

## FAQ / 常见问题

### Why is the floating panel hidden? / 浮窗为什么没有显示？

The floating panel only shows active tasks whose start date is today or earlier. If there are no matching tasks, it hides automatically.

### Can deleted tasks be restored? / 删除任务后还能恢复吗？

Yes. Normal deletion marks a task as deleted, and it can be restored from the deleted view. Permanent deletion removes it from the SQLite database.

### Is any data uploaded to the cloud? / 数据会上传到云端吗？

No. JMtodo stores data locally with SQLite, JSON, and the local attachments folder.

## Contributing / 贡献

Issues and pull requests are welcome. Before submitting changes, run:

```powershell
dotnet restore
dotnet build .\JMtodo.sln -c Release
```

Please keep changes focused and avoid mixing feature work, formatting-only changes, and unrelated refactors in one pull request.
