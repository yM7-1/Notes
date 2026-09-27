using System.IO;
using Godot;
using Notes.Core.Services;
using Notes.Game;

namespace Notes.UI;

/// <summary>NotesWindow partial: export the active board as Markdown plus a PNG
/// crop of the canvas, saved under user://notes-export (purely local).</summary>
public partial class NotesWindow
{
    private void ExportBoard()
    {
        var board = NotesRuntime.ActiveBoard;
        var labels = new NotesExportLabels(
            ModLocalization.T("export_turn", "回合"),
            "HP",
            ModLocalization.T("export_free", "自由节点"),
            ModLocalization.T("export_note", "备注"));
        var markdown = NotesExport.ToMarkdown(board, labels);
        var dir = ProjectSettings.GlobalizePath("user://notes-export");
        try
        {
            DirAccess.MakeDirRecursiveAbsolute(dir);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var safeName = string.Join("_", board.Name.Split(
                Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            if (safeName.Length == 0)
            {
                safeName = "board";
            }
            File.WriteAllText(Path.Combine(dir, $"{safeName}-{stamp}.md"), markdown);
            TrySaveCanvasPng(Path.Combine(dir, $"{safeName}-{stamp}.png"));
            NotesRuntime.SetImportMessage(ModLocalization.T("export_done", "已导出") + ": " + dir);
        }
        catch (Exception ex)
        {
            MegaCrit.Sts2.Core.Logging.Log.Error("[Notes] export failed: " + ex);
            NotesRuntime.SetImportMessage(ModLocalization.T("export_failed", "导出失败（见游戏日志）"));
        }
        NotesRuntime.Raise();
    }

    /// <summary>Best-effort screenshot: crop the viewport texture to the canvas
    /// rect so the export is just the board, not the whole game UI.</summary>
    private void TrySaveCanvasPng(string path)
    {
        try
        {
            var viewport = GetViewport();
            var image = viewport.GetTexture().GetImage();
            var visible = viewport.GetVisibleRect().Size;
            if (visible.X > 0 && image.GetWidth() > 0)
            {
                var scale = image.GetWidth() / visible.X;
                var rect = new Rect2I(
                    new Vector2I(
                        (int)(_canvas.GlobalPosition.X * scale),
                        (int)(_canvas.GlobalPosition.Y * scale)),
                    new Vector2I(
                        (int)(_canvas.Size.X * scale),
                        (int)(_canvas.Size.Y * scale)));
                rect = rect.Intersection(new Rect2I(0, 0, image.GetWidth(), image.GetHeight()));
                if (rect.Size.X > 8 && rect.Size.Y > 8)
                {
                    image = image.GetRegion(rect);
                }
            }
            image.SavePng(path);
        }
        catch (Exception ex)
        {
            MegaCrit.Sts2.Core.Logging.Log.Error("[Notes] canvas PNG export failed: " + ex);
        }
    }
}
