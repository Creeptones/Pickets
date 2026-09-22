using System;
using System.Windows.Input;

namespace Pickets;

internal readonly record struct FocusShortcut(uint Modifiers, uint VirtualKey, string Text)
{
    internal static bool TryParse(string? text, out FocusShortcut shortcut)
    {
        shortcut = default;
        if (string.Equals(text?.Trim(), "None", StringComparison.OrdinalIgnoreCase))
        { shortcut = new(0, 0, "None"); return true; }
        try
        {
            if (new KeyGestureConverter().ConvertFromInvariantString(text ?? "") is not KeyGesture gesture) return false;
            if ((gesture.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == 0 ||
                gesture.Modifiers.HasFlag(ModifierKeys.Windows) ||
                !(gesture.Key is >= Key.A and <= Key.Z or >= Key.F1 and <= Key.F24 or >= Key.D0 and <= Key.D9)) return false;
            uint modifiers = 0;
            if (gesture.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= WindowInterop.MOD_CONTROL;
            if (gesture.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= WindowInterop.MOD_ALT;
            if (gesture.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= WindowInterop.MOD_SHIFT;
            shortcut = new(modifiers, (uint)KeyInterop.VirtualKeyFromKey(gesture.Key),
                new KeyGestureConverter().ConvertToInvariantString(gesture) ?? text!);
            return shortcut.VirtualKey != 0;
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException or FormatException) { return false; }
    }
}
