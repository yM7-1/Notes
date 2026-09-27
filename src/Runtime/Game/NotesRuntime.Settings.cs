using Notes.Core.Documents;

namespace Notes.Game;

/// <summary>NotesRuntime partial: status-bar feedback and persisted settings.</summary>
internal static partial class NotesRuntime
{
    /// <summary>Shows a status-bar message and restarts its expiry timer.</summary>
    public static void SetImportMessage(string message)
    {
        LastImportMessage = message;
        _importMessageAge = 0;
    }
    /// <summary>Drops the status-bar feedback once it has been visible long
    /// enough; the UI refreshes through <see cref="Changed"/>.</summary>
    private static void ExpireImportMessage(double delta)
    {
        if (LastImportMessage.Length == 0 || _importMessageLifetime <= 0)
        {
            return;
        }
        _importMessageAge += delta;
        if (_importMessageAge < _importMessageLifetime)
        {
            return;
        }
        _importMessageAge = 0;
        LastImportMessage = "";
        Changed?.Invoke();
    }
    /// <summary>Applies the persisted global settings to the runtime.</summary>
    public static void ApplySettings(NotesGlobalData data)
    {
        AutoRecord = data.AutoRecordOps;
        _importMessageLifetime = Math.Max(0, data.MessageLifetimeSeconds);
        _codexLimit = Math.Clamp(data.CodexLimit, 10, 500);
        _quickActionsMask = data.QuickActionsMask;
        Commands.Limit = Math.Clamp(data.UndoLimit, 10, 2000);
    }
    /// <summary>Mutates + persists one setting and applies it immediately.</summary>
    public static void UpdateSettings(Action<NotesGlobalData> mutate)
    {
        if (!NotesPersistence.TryGetGlobalData(out var data))
        {
            return;
        }
        mutate(data);
        NotesPersistence.SaveGlobalNow();
        ApplySettings(data);
        Changed?.Invoke();
    }
}
