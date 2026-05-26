using BlendHub.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Collections.Generic;
using System.Linq;

namespace BlendHub.Dialogs
{
    public class PriorityOption
    {
        public string Name { get; set; } = string.Empty;
        public TodoPriority Value { get; set; }
        public SolidColorBrush Color { get; set; } = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    public class StatusOption
    {
        public string Name { get; set; } = string.Empty;
        public TodoStatus Value { get; set; }
    }

    public sealed class ProjectItemDialog : ContentDialog
    {
        private ProjectItemType _itemType;
        private List<PriorityOption> _priorityOptions;
        private List<StatusOption> _statusOptions;

        private ComboBox PriorityBox;
        private ComboBox StatusBox;
        private TextBox HeadingTextBox;
        private TextBox InputTextBox;
        private DatePicker DueDatePicker;

        public ProjectItemDialog(ProjectItemType itemType, string initialHeading = "", string initialContent = "", TodoPriority? initialPriority = null, TodoStatus? initialStatus = null, System.DateTime? initialDueDate = null, bool isEdit = false)
        {
            this.Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style;
            this.CloseButtonText = "Cancel";

            _itemType = itemType;

            _priorityOptions = new List<PriorityOption>
            {
                new PriorityOption { Name = "None", Value = TodoPriority.None, Color = (SolidColorBrush)Application.Current.Resources["TextFillColorTertiaryBrush"] },
                new PriorityOption { Name = "Low", Value = TodoPriority.Low, Color = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 48, 209, 88)) },
                new PriorityOption { Name = "Medium", Value = TodoPriority.Medium, Color = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 255, 159, 10)) },
                new PriorityOption { Name = "High", Value = TodoPriority.High, Color = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 255, 69, 58)) }
            };

            _statusOptions = new List<StatusOption>
            {
                new StatusOption { Name = "To-do", Value = TodoStatus.Todo },
                new StatusOption { Name = "In Progress", Value = TodoStatus.InProgress },
                new StatusOption { Name = "Completed", Value = TodoStatus.Completed }
            };

            var panel = new StackPanel { Spacing = 12, MinWidth = 400 };

            PriorityBox = new ComboBox
            {
                Header = "Priority",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Visibility = itemType == ProjectItemType.Todo ? Visibility.Visible : Visibility.Collapsed,
                ItemsSource = _priorityOptions
            };

            string templateXaml = @"
                <DataTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"">
                    <StackPanel Orientation=""Horizontal"" Spacing=""8"">
                        <Ellipse Width=""10"" Height=""10"" Fill=""{Binding Color}"" VerticalAlignment=""Center"" />
                        <TextBlock Text=""{Binding Name}"" VerticalAlignment=""Center"" />
                    </StackPanel>
                </DataTemplate>";
            PriorityBox.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(templateXaml);

            StatusBox = new ComboBox
            {
                Header = "Status",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Visibility = itemType == ProjectItemType.Todo ? Visibility.Visible : Visibility.Collapsed,
                ItemsSource = _statusOptions,
                DisplayMemberPath = "Name"
            };

            HeadingTextBox = new TextBox
            {
                Header = "Title",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            InputTextBox = new TextBox
            {
                Header = "Details",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 60
            };

            DueDatePicker = new DatePicker
            {
                Header = "Due Date",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Visibility = itemType == ProjectItemType.Todo ? Visibility.Visible : Visibility.Collapsed,
                DayFormat = "{day.integer} ({dayofweek.abbreviated})",
                YearVisible = false
            };

            panel.Children.Add(HeadingTextBox);
            panel.Children.Add(InputTextBox);
            panel.Children.Add(StatusBox);
            panel.Children.Add(PriorityBox);
            panel.Children.Add(DueDatePicker);

            this.Content = panel;

            Title = isEdit ? "Edit Item" : "Add Item";
            PrimaryButtonText = isEdit ? "Save" : "Add";
            HeadingTextBox.Text = initialHeading;
            InputTextBox.Text = initialContent;
            
            if (initialDueDate.HasValue)
            {
                DueDatePicker.Date = initialDueDate.Value;
            }
            
            if (!isEdit)
            {
                HeadingTextBox.PlaceholderText = "Enter title...";
                InputTextBox.PlaceholderText = "Enter details...";
            }

            if (initialPriority.HasValue)
            {
                var option = _priorityOptions.FirstOrDefault(p => p.Value == initialPriority.Value);
                PriorityBox.SelectedItem = option ?? _priorityOptions[0];
            }
            else
            {
                PriorityBox.SelectedIndex = 0; // Default to None
            }

            if (initialStatus.HasValue)
            {
                var option = _statusOptions.FirstOrDefault(s => s.Value == initialStatus.Value);
                StatusBox.SelectedItem = option ?? _statusOptions[0];
            }
            else
            {
                StatusBox.SelectedIndex = 0; // Default to Todo
            }
        }

        public string HeadingText => HeadingTextBox.Text;
        public string ContentText => InputTextBox.Text;
        public System.DateTime? SelectedDueDate => DueDatePicker.Visibility == Visibility.Visible ? DueDatePicker.Date.Date : null;

        public ProjectItemType SelectedType => _itemType;

        public TodoPriority SelectedPriority
        {
            get
            {
                if (_itemType == ProjectItemType.Todo && PriorityBox.SelectedItem is PriorityOption option)
                {
                    return option.Value;
                }
                return TodoPriority.None;
            }
        }

        public TodoStatus SelectedStatus
        {
            get
            {
                if (_itemType == ProjectItemType.Todo && StatusBox.SelectedItem is StatusOption option)
                {
                    return option.Value;
                }
                return TodoStatus.Todo;
            }
        }
    }
}
