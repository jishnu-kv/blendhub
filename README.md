# BlendHub

BlendHub is a beautiful, premium, and state-of-the-art desktop launcher and project management utility designed specifically for Blender creators, artists, and studios. Organize your `.blend` files, manage project notes and tasks via an integrated Kanban system, and map custom third-party asset/editing software directly to your files.

---

## Key Features

- 📂 **Project Cards**: A unified view of your Blender projects, showing thumbnails, file lists, and creation statuses.
- 🚀 **Default & Project-Specific File Launchers**: Define custom file launchers (e.g., opening `.psd` files with Photoshop, `.fbx` with an external viewer) globally in settings or override them on a per-project basis.
- 📋 **Integrated Kanban & Notes System**: Keep track of what needs to be done directly inside your project workspace. Add notes and manage planned, in-progress, or completed tasks with elegant priority tags.
- 🏗️ **Multi-Version Blender Support**: Easily manage multiple Blender installations and associate files with specific versions.
- 🌓 **Dynamic Modern Aesthetics**: Built on WinUI 3 with rich Fluent design styles, responsive transitions, dark/light theme support, and a stable tabbed interface.

---

## Build & Release Guide

BlendHub targets **.NET 8** and **WinUI 3 (Windows App SDK)**. The repository includes pre-configured **Publish Profiles** for target platforms to compile robust, clean, and self-contained builds.

### Requirements
- .NET 8.0 SDK or later
- Windows 10/11

### Creating a Proper Production Release
To compile a completely fresh, fully optimized, and self-contained build for a specific platform, clean the build directories and run the publish command against the corresponding publish profile:

```powershell
# 1. Clean the build artifacts
dotnet clean

# 2. Build for your target architecture:

# 64-bit Windows PC (Standard)
dotnet publish -c Release -p:PublishProfile=win-x64

# ARM64 Windows PC (Snapdragon / Copilot+ PCs)
dotnet publish -c Release -p:PublishProfile=win-arm64

# 32-bit Windows PC (Legacy compatibility)
dotnet publish -c Release -p:PublishProfile=win-x86
```

The self-contained release directories containing the executable (`BlendHub.exe`) and all runtime assets will be generated in:
`BlendHub\bin\Release\net8.0-windows10.0.19041.0\<win-arch>\publish\`
