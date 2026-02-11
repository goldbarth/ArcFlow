using System.Collections.Immutable;
using ArcFlow.Features.YouTubePlayer.Models;

namespace ArcFlow.Features.YouTubePlayer.State;

public record QueueState
{
    public const int HistoryLimit = 30;

    public Guid? SelectedPlaylistId { get; init; }
    public ImmutableList<VideoItem> Videos { get; init; } = ImmutableList<VideoItem>.Empty;
    public int? CurrentIndex { get; init; }

    public ImmutableList<QueueSnapshot> Past { get; init; } = ImmutableList<QueueSnapshot>.Empty;
    public ImmutableList<QueueSnapshot> Future { get; init; } = ImmutableList<QueueSnapshot>.Empty;

    public bool CanUndo => !Past.IsEmpty;
    public bool CanRedo => !Future.IsEmpty;

    public bool HasSelection => SelectedPlaylistId is not null;
    public bool HasVideo  => CurrentIndex is not null;

    /// <summary>
    /// Validates the current state of the queue by ensuring the current index is within the bounds
    /// of the video list and is only set when there are videos available.
    /// </summary>
    /// <returns>
    /// A new instance of <see cref="QueueState"/> with the <c>CurrentIndex</c> reset to null
    /// if it is out of bounds or if the video list is empty, otherwise returns the current state.
    /// </returns>
    public QueueState Validate()
    {
        if (CurrentIndex.HasValue && CurrentIndex.Value >= Videos.Count 
            || Videos.Count == 0 && CurrentIndex.HasValue)
        {
            return this with { CurrentIndex = null };
        }

        return this;
    }
}