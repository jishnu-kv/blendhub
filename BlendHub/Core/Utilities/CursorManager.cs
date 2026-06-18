using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using System.Reflection;

namespace src.Core
{
    public static class CursorManager
    {
        public static void SetCursor(UIElement element, InputCursor cursor)
        {
            if (element == null || cursor == null) return;

            // Use reflection to set ProtectedCursor
            var prop = typeof(UIElement).GetProperty("ProtectedCursor",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (prop != null)
            {
                prop.SetValue(element, cursor);
            }
        }
    }
}
