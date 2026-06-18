# BlendHub - Premium Blender Launcher & Project Hub

**A professional, high-performance desktop launcher, manager, and Kanban workspace for Blender creators built with WinUI 3 and .NET 8.0**

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![WinUI 3](https://img.shields.io/badge/WinUI-3-0078D4?logo=windows)](https://docs.microsoft.com/windows/apps/winui/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Status](https://img.shields.io/badge/Status-Polish--Phase-yellow)](README.md)

---

## 🎯 Project Status & Roadmap

BlendHub is actively moving towards its first major public release. Below is our roadmap progress:

- **State 1: Foundation** ✅ *Completed*
  - Project initialization, architecture design, MVVM framework setup, and repository structure.
- **State 2: Core UI & Pages** ✅ *Completed*
  - Implementation of primary views including Home, Projects list, Addon center, Backup/Restore, and Settings.
- **State 3: Current Phase** 🚀 *In Progress*
  - Code polishing, UI refinement, and optimization for the stable v1.0.0 release.
- **State 4: Future Expansion** 📅 *Planned*
  - **Reference Board**: Visual moodboards directly within your launcher workspace.
  - **Collaboration & Accounts**: Team synchronization, profiles, and shared workspaces.
  - **Render Manager**: Detailed job queue management and render farm integrations.
  - **Assets Manager**: Local and cloud library organization for 3D assets.

---

## ✨ Feature Highlights

### 📂 Unified Project Dashboard
- **Visual Cards**: View Blender projects with thumbnails, file lists, and metadata.
- **Integrated Kanban Board**: Stay organized with tasks grouped by status (Todo, In Progress, Done) and priority tags.
- **File Launcher Customization**: Configure default and project-level application overrides (e.g., open `.psd` with Photoshop, `.fbx` with a specific engine viewer).

### 🏗️ Blender Version Manager
- **Multi-Version Tracking**: Register, organize, and launch files using different Blender versions.
- **Scraper & Downloader**: Easily list available online versions and download them directly inside the app.

### 🔌 Addon & Extension Center
- **Addon Control**: Browse, install, update, and manage your Blender addons and extensions from a single interface.

### 💾 Backup, Restore & Sync
- **Data Safeguard**: Backup your preferences, project structures, and custom launchers.
- **Sync System**: Sync your settings seamlessly to keep your workspaces consistent.

### 🎨 Premium Fluent Design
- **Modern Aesthetics**: Leverages Windows App SDK and WinUI 3 controls.
- **Theme Support**: Seamless transitions between beautiful Dark and Light modes.
- **Responsive Layout**: Fluid UI designed for maximum screen space utilization.

---

## 📂 Project Structure

```
BlendHub/
├── BlendHub.slnx                # Visual Studio solution file
├── index.html                   # Project documentation landing page
├── scrape_blender_versions.py   # Script to scrape online Blender versions
└── BlendHub/                    # Main application project source
    ├── App.xaml / .cs           # Application entry point & configuration
    ├── MainWindow.xaml / .cs    # Main application shell window
    ├── app.manifest             # App execution properties & permissions
    ├── Package.appxmanifest     # MSIX packaging manifest
    ├── BlendHub.csproj          # MSBuild configuration and dependencies
    ├── blender_versions.json    # Local cache of scraped Blender releases
    ├── extensions.json          # Cached data for Blender addons/extensions
    ├── Core/                    # Core helpers, extensions, & constants
    ├── Models/                  # Data models (Project, Version, Tasks, Addons)
    ├── Services/                # System, Project, Addon, & Download services
    ├── ViewModels/              # ViewModels implementing MVVM pattern
    ├── Views/                   # Reusable Views, custom controls & pages
    │   ├── Controls/            # Custom UI elements
    │   ├── Dialogs/             # Modal content dialogs (Create/Edit Project)
    │   └── Pages/               # Core application pages
    │       ├── Home/            # Home / Dashboard page
    │       ├── Projects/        # Project Cards & Kanban board page
    │       ├── Settings/        # Custom launchers & app configurations
    │       ├── AddonsPage/      # Addons management pages
    │       ├── ReferenceBoard/  # Visual reference board workspace page
    │       ├── BackupPage.xaml  # Settings backup utility
    │       ├── RestorePage.xaml # Settings restore utility
    │       ├── DownloadPage.xaml# Blender version installer/downloader
    │       └── SyncPage.xaml    # Cloud sync page
    └── Assets/                  # Visual assets (branding logos, local files)
        ├── AppIcons/            # Windows Store & packaging tile/logo scale resources
        ├── Fonts/               # Customized Segoe UI & application typography
        ├── Styles/              # Styling resources (Icons.xaml, Navigation.xaml)
        └── Promotional Sizes/   # Graphic design branding sizes
```

---

## 🛠️ Technology Stack

- **Framework**: WinUI 3 (Windows App SDK 1.8)
- **Language**: C# / .NET 8.0
- **Architecture**: MVVM (using `CommunityToolkit.Mvvm`)
- **Libraries**:
  - `CommunityToolkit.WinUI.Controls.SettingsControls`
  - `CommunityToolkit.WinUI.Controls.Segmented`
  - `CommunityToolkit.WinUI.Converters`
  - `CommunityToolkit.WinUI.Collections`

---

## 🚀 Quick Start

### Prerequisites
- Windows 10 (version 1809 or higher) or Windows 11
- .NET 8.0 SDK

### Development Build
1. Clone the repository:
   ```bash
   git clone https://github.com/Gzeu/BlendHub.git
   cd BlendHub
   ```
2. Restore and run:
   ```powershell
   dotnet restore
   dotnet run --project BlendHub/BlendHub.csproj
   ```

### Creating a Production Release
To compile a fully optimized, self-contained build for a specific target architecture, use the corresponding publish command:

```powershell
# 64-bit Windows PC (Standard)
dotnet publish -c Release -p:PublishProfile=win-x64

# ARM64 Windows PC (Snapdragon / Copilot+ PCs)
dotnet publish -c Release -p:PublishProfile=win-arm64

# 32-bit Windows PC (Legacy compatibility)
dotnet publish -c Release -p:PublishProfile=win-x86
```

---

## 📜 License

Licensed under the **MIT License**. See [LICENSE](LICENSE) for more details.

---

*Designed for Blender creators, built for speed.* 🚀
