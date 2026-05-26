# Restore Page

The Restore Page allows you to recover your Blender settings and configurations from previously created backups. Select a backup file, choose target version, and specify what to restore.

![Restore Page](../src/Assets/screenshots/restore.png)

## Page Layout

### Header Section
At the top of the page, you'll find the main controls and status indicators:

- **Restore Settings Title** - Clear page header
- **Status Text** - Shows current restore status or progress information
- **Progress Bar** - Visual progress indicator during restore operations
- **Start Restore Button** - Primary action to begin restore process

### Info Bars
Below the header, you'll find status information bars:

- **Warning InfoBar** - Shows warnings and important messages
- **Error InfoBar** - Displays error messages if restore fails
- **Success InfoBar** - Confirms successful restore completion

### Main Content Area
The main section contains all restore configuration options in organized cards:

## Restore Configuration

### Select Backup
Choose which backup file to restore from:

- **Backup ComboBox** - Dropdown list of available backup files
- **Placeholder Text** - "Select backup..." when no backup is selected
- **Selection Change** - Automatically updates available items when backup is selected

### Restore Destination
Choose which Blender installation to restore settings to:

- **Target Version ComboBox** - List of installed Blender versions
- **Version Display** - Shows selected version name
- **Display Member Path** - Uses DisplayName property for clear identification

### Items to Include
Select which parts of configuration to restore from the backup:

#### Available Items:
- **User Preferences** - General Blender settings and configurations
- **Add-ons** - Installed add-ons and their settings
- **Key Configurations** - Custom keyboard shortcuts and input settings
- **UI Layouts** - Window layouts and workspace configurations
- **Themes** - Color themes and appearance settings
- **Scripts** - Custom scripts and plugins
- **Startup File** - Default startup blend file

#### Item Selection:
- **Checkboxes** - Individual selection for each restore item
- **Tooltips** - Hover information about each item
- **Existence Status** - Shows if item exists in backup file
- **Grid Layout** - Organized in responsive grid format
- **Two-Way Binding** - Changes are immediately reflected

## How to Use

### Restoring from Backup
1. **Select Backup** - Choose backup file from the dropdown list
2. **Choose Target** - Select Blender version to restore to
3. **Select Items** - Choose what to include in the restore
4. **Start Restore** - Click "Start Restore" to begin process
5. **Monitor Progress** - Watch progress bar and status messages

### Managing Restore Options
1. **Review Backup Contents** - Check what items are available in selected backup
2. **Target Selection** - Choose appropriate Blender version for restore
3. **Selective Restore** - Only restore needed items to avoid conflicts
4. **Validation** - Ensure all selections are valid before starting

## Tips for Beginners

### Before Restoring
- **Close Blender** - Ensure Blender is not running during restore
- **Backup Current** - Create backup of current settings before restore
- **Check Compatibility** - Verify backup is compatible with target version
- **Free Space** - Ensure sufficient disk space for restore process

### Backup Selection
- **Recent Backups** - Choose the most recent backup for current settings
- **Version Matching** - Select backup created from similar Blender version
- **Contents Review** - Check what's included in backup before restore
- **Test Restore** - Try restoring to a test version first

### Item Selection
- **Essential Items** - Always restore preferences and key configurations
- **Add-ons Carefully** - Only restore add-ons that are compatible
- **Custom Settings** - Include personal customizations and shortcuts
- **Avoid Conflicts** - Don't restore items that might conflict with current setup

## Advanced Features

### Smart Validation
- **Automatic Checking** - Validates selections before allowing restore
- **Compatibility Detection** - Checks backup and target version compatibility
- **Existence Verification** - Ensures selected items exist in backup
- **Real-time Feedback** - Immediate validation results

### Progress Tracking
- **Real-time Updates** - Live progress during restore process
- **Status Messages** - Detailed information about restore progress
- **Error Handling** - Clear error reporting and recovery options
- **Completion Notification** - Success confirmation with restore details

### Responsive Design
- **Adaptive Layout** - Interface adjusts to window size
- **Mobile Support** - Works on different screen sizes
- **Touch-Friendly** - Large touch targets for mobile devices
- **Keyboard Navigation** - Full keyboard accessibility support

## Troubleshooting

### Common Issues
- **No Backups Found** - Check if backup files exist in default location
- **Restore Fails** - Verify file permissions and disk space
- **Items Missing** - Some items may not exist in selected backup
- **Version Conflicts** - Target version may be incompatible with backup

### Solutions
- **Check Backup Location** - Verify backup files are accessible
- **Run as Administrator** - Ensure sufficient permissions for system files
- **Verify Compatibility** - Check if backup matches target Blender version
- **Selective Restore** - Try restoring fewer items if process fails

### Error Recovery
- **Partial Restores** - Handle incomplete restore scenarios
- **Retry Options** - Automatic retry mechanisms for failed operations
- **Rollback Support** - Ability to undo failed restore attempts
- **Data Integrity** - Verification of restored data completeness

## Best Practices

### Before Restoring
- **Test Environment** - Test restore in non-production environment first
- **Document Current** - Note current settings before restore
- **Version Planning** - Plan which version to restore to
- **Backup Strategy** - Have multiple restore options available

### After Restoring
- **Verify Functionality** - Test that restored settings work correctly
- **Check Add-ons** - Ensure restored add-ons function properly
- **Validate Preferences** - Confirm all preferences are applied correctly
- **Restart Blender** - Restart Blender to apply all restored settings

## Keyboard Shortcuts
- **Tab Navigation** - Move between form controls
- **Space Selection** - Toggle checkboxes with spacebar
- **Enter Confirmation** - Start restore with Enter key
- **Escape Cancellation** - Cancel restore process with Escape
