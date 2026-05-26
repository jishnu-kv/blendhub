# Project Page

The Project Page is your comprehensive workspace for managing all Blender projects within BlendHub. Create, organize, search, and manage your projects with powerful tools and an intuitive interface.

![Project Page](../src/Assets/screenshots/project.png)

## Page Layout

### Header Section
At the top of the page, you'll find the main controls for project management:

- **Project Management Title** - Clear page header
- **Create Project Button** - Primary action button to create new projects

### Progress and Feedback
- **Creation Progress Bar** - Shows progress when creating new projects
- **Success InfoBar** - Displays confirmation when projects are created successfully, with an "Open Project" action button

### Search and Controls Bar
Below the header, you'll find powerful tools for managing your project list:

#### Left Side Controls:
- **Search Box** - Search projects by name or path with real-time filtering
- **Refresh Button** - Reload the project list to show changes

#### Right Side Controls:
- **Sort Button** - Dropdown menu with sorting options:
  - Date Created (Newest) - Default
  - Date Created (Oldest)
  - Name (A-Z)
  - Name (Z-A)

### Drag and Drop Zone
A prominent drop zone for importing existing projects:

#### Features:
- **Visual Drop Area** - Dashed border indicating drop zone
- **Drag Instructions** - Clear text guidance for users
- **Browse Button** - Alternative to drag-and-drop for folder selection
- **Automatic Detection** - Detects .blend files in dropped folders

### Project List
The main area displays your projects as cards, each showing key project information.

#### Empty States:
- **No Projects Message** - Appears when you have no projects yet
- **No Search Results** - Shows when search returns no matches

## How to Use

### Creating New Projects
1. **Click "Create Project"** - Opens the project creation dialog
2. **Fill Project Details** - Enter name, location, and Blender version
3. **Configure Settings** - Set up project preferences
4. **Create** - Confirm to create your new project

![Create Project Dialog](../src/Assets/screenshots/project_create.png)

### Importing Existing Projects
#### Method 1: Drag and Drop
1. **Select Folder** - Drag a folder containing .blend files to the drop zone
2. **Drop** - Release to automatically import the project
3. **Confirm** - Review detected project details

#### Method 2: Browse
1. **Click "Browse for project folder"** - Opens folder picker
2. **Navigate** - Find and select your project folder
3. **Import** - Confirm to add the project to BlendHub

### Managing Projects
#### Search and Filter
1. **Type in Search Box** - Real-time filtering as you type
2. **Search by Name** - Find projects by project name
3. **Search by Path** - Find projects by folder location
4. **Clear Search** - Delete text to show all projects

#### Sort Projects
1. **Click Sort Button** - Opens sorting menu
2. **Choose Option** - Select preferred sorting method
3. **Automatic Update** - List updates immediately

#### Refresh Projects
1. **Click Refresh Button** - Reloads project list
2. **Detect Changes** - Shows any projects added/removed outside BlendHub

## Project Cards
Each project in the list appears as a card with:
- **Project Name** - Clear project title
- **Project Path** - File system location
- **Creation Date** - When the project was created
- **Blender Version** - Associated Blender version
- **Action Buttons** - Launch, edit, and manage options

## Tips for Beginners

### Getting Started
- **Create First Project** - Use the "Create Project" button to get started
- **Import Existing Work** - Drag and drop folders with existing .blend files
- **Use Search** - Quickly find projects as your collection grows
- **Organize with Sorting** - Keep projects organized with different sort options

### Project Organization
- **Descriptive Names** - Use clear, descriptive project names
- **Consistent Locations** - Keep projects in organized folder structures
- **Regular Refresh** - Use refresh to detect external changes
- **Search Efficiency** - Use partial names or path fragments in search

### Import Best Practices
- **Clean Folders** - Drop folders that primarily contain .blend files
- **Single Project** - Import one project folder at a time
- **Verify Detection** - Check that BlendHub correctly identifies your projects
- **Backup First** - Backup important projects before importing

## Advanced Features

### Real-time Search
- **Instant Filtering** - Results update as you type
- **Multiple Fields** - Searches both project names and file paths
- **Case Insensitive** - Search works regardless of capitalization
- **Partial Matching** - Finds projects containing your search term

### Flexible Sorting
- **Date Sorting** - Organize by when projects were created
- **Alphabetical** - Sort by project name A-Z or Z-A
- **Persistent Choice** - Your sort preference is remembered
- **Quick Switch** - Change sorting with one click

### Drag and Drop Import
- **Visual Feedback** - See the drop zone highlight when dragging
- **Automatic Detection** - BlendHub finds .blend files automatically
- **Multiple Files** - Handles folders with multiple .blend files
- **Error Handling** - Clear feedback if import fails

## Troubleshooting

### Common Issues
- **Project Not Found in Search** - Try different search terms or partial names
- **Import Fails** - Ensure the folder contains .blend files
- **Projects Not Refreshing** - Click the refresh button to update the list
- **Sort Not Working** - Try selecting a different sort option

### Solutions
- **Check File Paths** - Verify projects are in accessible locations
- **Clear Search** - Remove search text to see all projects
- **Restart Page** - Navigate away and back to reset the page
- **Check Permissions** - Ensure BlendHub can access project folders

## Keyboard Shortcuts
- **Search Focus** - Click in search box or use Tab to navigate
- **Clear Search** - Select all text in search box and delete
- **Sort Menu** - Use arrow keys in sort menu
- **Create Project** - Use Tab to navigate to Create button

## Related Pages
- [Home Page](./HomePage.md) - Recent projects and quick access
- [Project Detail Page](./ProjectDetailPage.md) - Detailed project management
- [Download Page](./DownloadPage.md) - Manage Blender versions for projects
