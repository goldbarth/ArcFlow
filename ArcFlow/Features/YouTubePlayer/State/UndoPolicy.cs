namespace ArcFlow.Features.YouTubePlayer.State;

public static class UndoPolicy
{
    public static bool IsUndoable(YtAction action) => action is
        YtAction.SelectVideo or YtAction.SortChanged;

    public static bool IsBoundary(YtAction action) => action is
        YtAction.PlaylistLoaded or YtAction.SelectPlaylist;
}
