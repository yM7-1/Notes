using System.Text.Json;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Runs;
using Notes.Core.Documents;
using Notes.Core.Serialization;
using STS2RitsuLib;
using STS2RitsuLib.Data;
using STS2RitsuLib.Utils.Persistence;

namespace Notes.Game;

/// <summary>
/// Storage for the two note libraries.
/// - Run library: our own JSON file keyed by the run seed, so notes survive
///   save-quit-continue (RitsuLib's RunSavedData slot did not get exported in
///   testing, so we no longer depend on it).
/// - Global library: RitsuLib ModDataStore (Profile scope), survives runs.
/// Both are local-only; nothing is sent over the network.
/// </summary>
internal static class NotesPersistence
{
    private const string GlobalKey = "boards";
    private const string GlobalFile = "notes_boards";

    private static ModDataStore? _globalStore;
    private static string? _runFilePathCache;
    private static string _lastWrittenJson = "";

    private sealed class RunFilePayload
    {
        public string Identity { get; set; } = "";

        /// <summary>Identifies the combat the notes belong to; a different key
        /// at combat start means the boards are reset (SL keeps the same key).</summary>
        public string CombatKey { get; set; } = "";

        public NotesDocument Document { get; set; } = new();

        public List<NotesOpData> Ops { get; set; } = new();
    }

    public static void Register()
    {
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

        RitsuLibFramework.SubscribeLifecycle<RunStartedEvent>(e => OnRunChanged(e.RunState));
        RitsuLibFramework.SubscribeLifecycle<RunLoadedEvent>(e => OnRunChanged(e.RunState));
        RitsuLibFramework.SubscribeLifecycle<RunEndedEvent>(_ => OnRunEnded());
        RitsuLibFramework.SubscribeLifecycle<RunSavingEvent>(_ => NotesRuntime.FlushSave());
    }

    private static void OnRunChanged(RunState state)
    {
        try
        {
            if (ReferenceEquals(GameContext.CurrentRun, state))
            {
                return;
            }
            GameContext.CurrentRun = state;
            Log.Info("[Notes] run context acquired (seed=" + IdentityOf(state) + ")");
            NotesRuntime.OnRunContextChanged();
        }
        catch (Exception ex)
        {
            Log.Error("[Notes] run-started handling failed: " + ex);
        }
    }

    private static void OnRunEnded()
    {
        try
        {
            if (GameContext.CurrentRun == null)
            {
                return;
            }
            GameContext.CurrentRun = null;
            Log.Info("[Notes] run context cleared");
            NotesRuntime.OnRunContextChanged();
        }
        catch (Exception ex)
        {
            Log.Error("[Notes] run-ended handling failed: " + ex);
        }
    }

    // ---- run library (own file) ----------------------------------------------

    private static string RunFilePath =>
        _runFilePathCache ??= Path.Combine(Godot.OS.GetUserDataDir(), "Notes", "run_notes.json");

    private static string IdentityOf(RunState state)
    {
        try
        {
            var seed = state.Rng?.StringSeed;
            if (!string.IsNullOrWhiteSpace(seed))
            {
                return seed;
            }
        }
        catch
        {
            // fall through
        }
        return "unknown";
    }

    public static NotesDocument LoadRun(RunState state, out List<NotesOpData> ops, out string combatKey)
    {
        ops = new List<NotesOpData>();
        combatKey = "";
        try
        {
            var path = RunFilePath;
            if (!File.Exists(path))
            {
                return new NotesDocument();
            }
            var payload = JsonSerializer.Deserialize<RunFilePayload>(File.ReadAllText(path), NotesJson.Options);
            if (payload == null || payload.Identity != IdentityOf(state))
            {
                return new NotesDocument();
            }
            ops = payload.Ops ?? new List<NotesOpData>();
            combatKey = payload.CombatKey ?? "";
            _lastWrittenJson = "";
            return NotesJson.Normalize(payload.Document);
        }
        catch (Exception ex)
        {
            Log.Error("[Notes] run notes load failed: " + ex);
        }
        return new NotesDocument();
    }

    /// <summary>Writes the run notes; false when the write failed and the caller
    /// should keep the data dirty so a later debounce retries it.</summary>
    public static bool SaveRun(
        RunState state,
        NotesDocument document,
        IReadOnlyList<NotesOpData> ops,
        string combatKey)
    {
        try
        {
            var payload = new RunFilePayload
            {
                Identity = IdentityOf(state),
                CombatKey = combatKey,
                Document = document,
                Ops = ops.ToList(),
            };
            var json = JsonSerializer.Serialize(payload, NotesJson.Options);
            if (json == _lastWrittenJson)
            {
                return true; // already on disk
            }
            var path = RunFilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
            _lastWrittenJson = json;
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("[Notes] run notes save failed: " + ex);
            return false;
        }
    }

    /// <summary>Persisted ops of the current run (fallback for import).</summary>
    public static List<NotesOpData> LoadOpsForRun()
    {
        var run = GameContext.CurrentRun;
        if (run == null)
        {
            return new List<NotesOpData>();
        }
        try
        {
            var path = RunFilePath;
            if (!File.Exists(path))
            {
                return new List<NotesOpData>();
            }
            var payload = JsonSerializer.Deserialize<RunFilePayload>(File.ReadAllText(path), NotesJson.Options);
            if (payload != null && payload.Identity == IdentityOf(run))
            {
                return payload.Ops ?? new List<NotesOpData>();
            }
        }
        catch (Exception ex)
        {
            Log.Error("[Notes] run ops load failed: " + ex);
        }
        return new List<NotesOpData>();
    }

    // ---- global library (ModDataStore) ---------------------------------------

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

    /// <summary>Saves the global library; false when the store is not ready or
    /// the write failed (caller keeps it dirty and retries).</summary>
    public static bool SaveGlobal(NotesDocument document)
    {
        try
        {
            if (_globalStore == null)
            {
                return false;
            }
            _globalStore.Modify<NotesGlobalData>(GlobalKey, data => data.Document = document);
            _globalStore.Save(GlobalKey);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("[Notes] global data save failed: " + ex);
            return false;
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
