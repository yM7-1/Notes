using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using Notes.Game;

namespace Notes;

[ModInitializer(nameof(Initialize))]
public static class Entry
{
    public const string ModId = "Notes";

    public static void Initialize()
    {
        Log.Info("[Notes] initializing");
        try
        {
            ModLocalization.Initialize();
        }
        catch (Exception ex)
        {
            Log.Error("[Notes] localization init failed: " + ex);
        }

        try
        {
            NotesPersistence.Register();
        }
        catch (Exception ex)
        {
            Log.Error("[Notes] persistence register failed: " + ex);
        }

        try
        {
            NotesRuntime.Initialize();
        }
        catch (Exception ex)
        {
            Log.Error("[Notes] runtime init failed: " + ex);
        }

        try
        {
            Notes.Game.NotesOpLog.Initialize();
        }
        catch (Exception ex)
        {
            Log.Error("[Notes] op log init failed: " + ex);
        }

        try
        {
            var harmony = new HarmonyLib.Harmony("Notes");
            harmony.PatchAll(typeof(Entry).Assembly);
        }
        catch (Exception ex)
        {
            Log.Error("[Notes] harmony patches failed: " + ex);
        }

        try
        {
            AttachLayer();
        }
        catch (Exception ex)
        {
            Log.Error("[Notes] layer attach failed: " + ex);
        }
    }

    private static void AttachLayer()
    {
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
        {
            Log.Error("[Notes] scene tree not ready; notes layer not attached");
            return;
        }
        tree.Root.CallDeferred(Node.MethodName.AddChild, new UI.NotesLayer { Name = "NotesLayer" });
    }
}
