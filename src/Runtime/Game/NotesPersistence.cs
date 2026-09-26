using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Runs;
using Notes.Core.Documents;
using STS2RitsuLib.Data;
using STS2RitsuLib.RunData;
using STS2RitsuLib.Utils.Persistence;

namespace Notes.Game;

/// <summary>
/// Storage for the two note libraries:
/// - Run library: RitsuLib <c>RunSavedDataStore</c>, travels with the save file.
/// - Global library: RitsuLib <c>ModDataStore</c> (Profile scope), survives runs.
/// Both are local-only; nothing is sent over the network.
/// </summary>
internal static class NotesPersistence
{
    private const string RunKey = "boards";
    private const string GlobalKey = "boards";
    private const string GlobalFile = "notes_boards";

    private static RunSavedData<NotesRunData>? _runSlot;
    private static ModDataStore? _globalStore;

    public static void Register()
    {
        _runSlot ??= RunSavedDataStore.For(Entry.ModId).Register<NotesRunData>(RunKey);
        try
        {
            var store = ModDataStore.For(Entry.ModId);
            store.Register<NotesGlobalData>(
                key: GlobalKey,
                fileName: GlobalFile,
                scope: SaveScope.Profile,
                defaultFactory: () => new NotesGlobalData(),
                autoCreateIfMissing: true);
            _globalStore = store;
        }
        catch (Exception ex)
        {
            Log.Error("[Notes] global store register failed: " + ex);
        }

        RunManager.Instance.RunStarted += OnRunStarted;
    }

    private static void OnRunStarted(RunState state)
    {
        try
        {
            GameContext.CurrentRun = state;
            NotesRuntime.OnRunContextChanged();
        }
        catch (Exception ex)
        {
            Log.Error("[Notes] run-started handling failed: " + ex);
        }
    }

    public static NotesDocument LoadRun(RunState state)
    {
        try
        {
            if (_runSlot != null && _runSlot.TryGet(state, out var data) && data?.Document != null)
            {
                return data.Document;
            }
        }
        catch (Exception ex)
        {
            Log.Error("[Notes] run data load failed: " + ex);
        }
        return new NotesDocument();
    }

    public static void SaveRun(RunState state, NotesDocument document)
    {
        try
        {
            _runSlot?.Modify(state, data => data.Document = document);
        }
        catch (Exception ex)
        {
            Log.Error("[Notes] run data save failed: " + ex);
        }
    }

    /// <summary>False while profile services are not ready yet; the caller retries.</summary>
    public static bool TryLoadGlobal(out NotesDocument document)
    {
        try
        {
            if (_globalStore != null)
            {
                var data = _globalStore.Get<NotesGlobalData>(GlobalKey);
                if (data == null)
                {
                    data = new NotesGlobalData();
                    _globalStore.Modify<NotesGlobalData>(GlobalKey, d => d.Document = data.Document);
                }
                document = data.Document;
                return true;
            }
        }
        catch (Exception ex)
        {
            Log.Error("[Notes] global data load failed: " + ex);
        }
        document = new NotesDocument();
        return false;
    }

    public static void SaveGlobal(NotesDocument document)
    {
        try
        {
            _globalStore?.Modify<NotesGlobalData>(GlobalKey, data => data.Document = document);
            _globalStore?.Save(GlobalKey);
        }
        catch (Exception ex)
        {
            Log.Error("[Notes] global data save failed: " + ex);
        }
    }

    /// <summary>Live global data for UI preferences (button position).</summary>
    public static bool TryGetGlobalData(out NotesGlobalData data)
    {
        try
        {
            if (_globalStore != null)
            {
                var live = _globalStore.Get<NotesGlobalData>(GlobalKey);
                if (live != null)
                {
                    data = live;
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("[Notes] global data read failed: " + ex);
        }
        data = new NotesGlobalData();
        return false;
    }

    public static void SaveGlobalNow()
    {
        try
        {
            _globalStore?.Save(GlobalKey);
        }
        catch (Exception ex)
        {
            Log.Error("[Notes] global data flush failed: " + ex);
        }
    }
}
