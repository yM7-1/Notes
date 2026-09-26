namespace Notes.Core.Services;

/// <summary>Short, sortable-ish opaque ids for boards, nodes and edges.</summary>
public static class IdFactory
{
    public static string NewBoardId() => New("b");

    public static string NewNodeId() => New("n");

    public static string NewEdgeId() => New("e");

    private static string New(string prefix) =>
        prefix + Guid.NewGuid().ToString("N")[..10];
}
