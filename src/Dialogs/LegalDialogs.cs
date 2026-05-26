using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;

namespace BlendHub.Dialogs
{
    public static class LegalDialogs
    {
        public static async Task ShowPrivacyPolicyAsync(XamlRoot xamlRoot)
        {
            string content = await ReadAssetFileAsync("PrivacyPolicy.md");
            await ShowDialogAsync("Privacy Policy", content, xamlRoot);
        }

        public static async Task ShowTermsOfServiceAsync(XamlRoot xamlRoot)
        {
            string content = await ReadAssetFileAsync("TermsOfService.md");
            await ShowDialogAsync("Terms of Use", content, xamlRoot);
        }

        private static async Task ShowDialogAsync(string title, string content, XamlRoot xamlRoot)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = xamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                RequestedTheme = (App.MainWindow.Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default,
                Content = new ScrollViewer
                {
                    MaxHeight = 400,
                    Padding = new Microsoft.UI.Xaml.Thickness(0, 8, 0, 8),
                    Content = CreateMarkdownContent(content)
                }
            };
            await dialog.ShowAsync();
        }

        private static UIElement CreateMarkdownContent(string text)
        {
            var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 24 };
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var line = lines[lineIndex];
                string processLine = line;

                // Handle bullet list
                if (processLine.TrimStart().StartsWith("* "))
                {
                    processLine = "  • " + processLine.TrimStart().Substring(2);
                }

                // Handle bold
                var parts = processLine.Split("**");
                for (int i = 0; i < parts.Length; i++)
                {
                    if (i % 2 == 1 && parts.Length > i)
                    {
                        tb.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = parts[i], FontWeight = Microsoft.UI.Text.FontWeights.Bold });
                    }
                    else if (!string.IsNullOrEmpty(parts[i]))
                    {
                        tb.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = parts[i] });
                    }
                }

                if (lineIndex < lines.Length - 1)
                {
                    tb.Inlines.Add(new Microsoft.UI.Xaml.Documents.LineBreak());
                }
            }
            return tb;
        }

        private static async Task<string> ReadAssetFileAsync(string fileName)
        {
            try
            {
                string path = System.IO.Path.Combine(System.AppContext.BaseDirectory, "Assets", fileName);
                if (System.IO.File.Exists(path))
                {
                    return await System.IO.File.ReadAllTextAsync(path);
                }

                // Fallback for development if BaseDirectory is different
                path = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "src", "Assets", fileName);
                if (System.IO.File.Exists(path))
                {
                    return await System.IO.File.ReadAllTextAsync(path);
                }

                return "Error: Could not load " + fileName;
            }
            catch (Exception ex)
            {
                return "Error loading content: " + ex.Message;
            }
        }
    }
}
