using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Nodalis.App.Commands;

/// <summary>
/// Registers an explicit Inbox capture gesture in Windows only for the lifetime
/// of the running Nodalis window. Never monitors keys or clipboard content.
/// </summary>
public sealed class GlobalInboxHotkey : IDisposable
{
    public const string PreferenceKey = "inbox.capture.global";
    public const string DefaultGesture = "Ctrl+Shift+I";

    private const int HotkeyMessage = 0x0312;
    private const int FirstHotkeyId = 0x4E11;
    private const int SecondHotkeyId = 0x4E12;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    private readonly IntPtr _windowHandle;
    private readonly HwndSource _source;
    private int _activeId;
    private bool _disposed;

    /// <summary>Creates the native message hook for a realized WPF window.</summary>
    /// <param name="owner">Window whose HWND receives WM_HOTKEY.</param>
    public GlobalInboxHotkey(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _windowHandle = new WindowInteropHelper(owner).Handle;
        if (_windowHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "Le raccourci global doit être enregistré après l'initialisation de la fenêtre.");
        }

        _source = HwndSource.FromHwnd(_windowHandle) ??
            throw new InvalidOperationException("Aucune source de messages Windows.");
        _source.AddHook(WindowMessageHook);
    }

    public event EventHandler? Triggered;

    public string ActiveGesture { get; private set; } = string.Empty;

    /// <summary>
    /// Rebinds without losing the old shortcut when the new gesture is unavailable.
    /// The old WM_HOTKEY is unregistered only after the new one succeeds.
    /// </summary>
    /// <param name="gesture">User-configured gesture such as Ctrl+Shift+I.</param>
    public void Register(string gesture)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!TryValidateGesture(gesture, out uint modifiers,
                out uint virtualKey, out string error))
        {
            throw new ArgumentException(error, nameof(gesture));
        }

        if (string.Equals(ActiveGesture, gesture.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        int newId = _activeId == FirstHotkeyId ? SecondHotkeyId : FirstHotkeyId;
        if (!RegisterHotKey(
                _windowHandle, newId, modifiers | ModNoRepeat, virtualKey))
        {
            int nativeError = Marshal.GetLastWin32Error();
            throw new Win32Exception(nativeError,
                "Windows refuse ce raccourci global. Choisissez une autre combinaison.");
        }

        if (_activeId != 0)
        {
            UnregisterHotKey(_windowHandle, _activeId);
        }

        _activeId = newId;
        ActiveGesture = gesture.Trim();
    }

    /// <summary>Validates syntax and collisions with Nodalis local shortcuts.</summary>
    /// <param name="gesture">Combination written with plus-separated modifiers and one key.</param>
    /// <param name="modifiers">Win32 modifier bitmask when valid.</param>
    /// <param name="virtualKey">Win32 virtual key code when valid.</param>
    /// <param name="error">User-facing reason when invalid.</param>
    /// <returns>Whether the combination can be attempted with RegisterHotKey.</returns>
    public static bool TryValidateGesture(
            string? gesture,
            out uint modifiers,
            out uint virtualKey,
            out string error)
    {
        modifiers = 0;
        virtualKey = 0;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(gesture))
        {
            error = "Saisissez un raccourci du type Ctrl+Shift+I.";
            return false;
        }

        string[] parts = gesture.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length < 3 || parts.Any(string.IsNullOrWhiteSpace))
        {
            error = "Utilisez au moins deux modificateurs et une touche (ex. Ctrl+Shift+I).";
            return false;
        }

        for (int index = 0; index < parts.Length - 1; index++)
        {
            uint bit = parts[index].ToLowerInvariant() switch
            {
                "ctrl" or "control" => ModControl,
                "alt" => ModAlt,
                "shift" => ModShift,
                "win" or "windows" => ModWin,
                _ => 0
            };
            if (bit == 0 || (modifiers & bit) != 0)
            {
                error = "Modificateur invalide ou dupliqué.";
                return false;
            }

            modifiers |= bit;
        }

        if (System.Numerics.BitOperations.PopCount(modifiers) < 2 ||
            !Enum.TryParse(parts[^1], true, out Key key) ||
            !((key >= Key.A && key <= Key.Z) ||
              (key >= Key.F1 && key <= Key.F12) ||
              (key >= Key.D0 && key <= Key.D9) ||
              key == Key.Space))
        {
            error = "Choisissez deux modificateurs et une lettre, un chiffre, F1-F12 ou Espace.";
            return false;
        }

        virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        ModifierKeys wpfModifiers = ModifierKeys.None;
        if ((modifiers & ModControl) != 0) wpfModifiers |= ModifierKeys.Control;
        if ((modifiers & ModAlt) != 0) wpfModifiers |= ModifierKeys.Alt;
        if ((modifiers & ModShift) != 0) wpfModifiers |= ModifierKeys.Shift;
        if ((modifiers & ModWin) != 0) wpfModifiers |= ModifierKeys.Windows;

        uint candidateKey = virtualKey;
        if (ShortcutCatalog.All.Any(shortcut =>
                shortcut.Modifiers == wpfModifiers &&
                (uint)KeyInterop.VirtualKeyFromKey(shortcut.Key) == candidateKey))
        {
            error = "Ce raccourci est déjà utilisé dans Nodalis (Ctrl+Alt+N reste réservé aux notes rapides).";
            return false;
        }

        return true;
    }

    /// <summary>Receives only the registered WM_HOTKEY ID from Windows.</summary>
    /// <param name="hwnd">Window handle.</param>
    /// <param name="message">Message ID.</param>
    /// <param name="wParam">Message parameter containing the registration ID.</param>
    /// <param name="lParam">Additional message data.</param>
    /// <param name="handled">Whether the message was consumed.</param>
    /// <returns>Unused return value.</returns>
    private IntPtr WindowMessageHook(
            IntPtr hwnd,
            int message,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
    {
        if (!_disposed && message == HotkeyMessage &&
            wParam.ToInt32() == _activeId && _activeId != 0)
        {
            handled = true;
            Triggered?.Invoke(this, EventArgs.Empty);
        }

        return IntPtr.Zero;
    }

    /// <summary>Unregisters Win32 resources when the Nodalis window closes.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_activeId != 0)
        {
            UnregisterHotKey(_windowHandle, _activeId);
            _activeId = 0;
        }

        _source.RemoveHook(WindowMessageHook);
        GC.SuppressFinalize(this);
    }

    /// <summary>Registers a window-scoped Windows global hotkey.</summary>
    /// <param name="windowHandle">Native owner handle.</param>
    /// <param name="id">Hotkey identifier.</param>
    /// <param name="modifiers">Win32 modifier flags.</param>
    /// <param name="virtualKey">Win32 key code.</param>
    /// <returns>True when Windows accepts the combination.</returns>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(
        IntPtr windowHandle, int id, uint modifiers, uint virtualKey);

    /// <summary>Unregisters the window-scoped Windows global hotkey.</summary>
    /// <param name="windowHandle">Native owner handle.</param>
    /// <param name="id">Registered identifier.</param>
    /// <returns>True if it was unregistered.</returns>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);
}
