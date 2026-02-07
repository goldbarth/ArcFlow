using ArcFlow.Features.YouTubePlayer.Models;
using ArcFlow.Features.YouTubePlayer.Service;
using ArcFlow.Features.YouTubePlayer.State;
using Microsoft.JSInterop;

namespace ArcFlow.Features.YouTubePlayer.Store;

public sealed class YouTubePlayerStore
{
    private readonly IPlaylistService _playlistService;
    private readonly IJSRuntime _jsRuntime;
    
    private readonly SemaphoreSlim _gate = new(1, 1);

    public YouTubePlayerState State { get; private set; }
    
    public event Action<YouTubePlayerState>? StateChanged;

    public YouTubePlayerStore(IPlaylistService playlistService, IJSRuntime jsRuntime)
    {
        _playlistService = playlistService;
        _jsRuntime = jsRuntime;
        
        State = new YouTubePlayerState(
            Playlists: new PlaylistsState.Loading(),
            Queue: new QueueState(
                SelectedPlaylistId: null,
                Videos: [],
                CurrentIndex: null
            ),
            Player: new PlayerState.Empty()
        );
    }

    public async Task Dispatch(YtAction action)
    {
        await _gate.WaitAsync();
        try
        {
            State = Reduce(State, action);
        }
        finally
        {
            _gate.Release();
        }
        
        StateChanged?.Invoke(State);
        
        await RunEffects(action);
    }
    
    private static YouTubePlayerState Reduce(YouTubePlayerState s, YtAction a) =>
        a switch
        {
            YtAction.Initialize =>
                s with { Playlists = new PlaylistsState.Loading() },

            YtAction.PlaylistsLoaded pl =>
                s with
                {
                    Playlists = pl.Playlists.Count == 0
                        ? new PlaylistsState.Empty()
                        : new PlaylistsState.Loaded(pl.Playlists)
                },

            YtAction.SelectPlaylist sp =>
                s with
                {
                    Queue = s.Queue with
                    {
                        SelectedPlaylistId = sp.PlaylistId,
                        Videos = [],
                        CurrentIndex = null
                    },
                    Player = new PlayerState.Empty()
                },

            YtAction.PlaylistLoaded pl =>
                s with
                {
                    Queue = s.Queue with
                    {
                        SelectedPlaylistId = pl.Playlist.Id,
                        Videos = pl.Playlist.VideoItems
                            .OrderBy(v => v.Position)
                            .ToList(),
                        CurrentIndex = null
                    }
                },

            YtAction.SelectVideo sv =>
                ReduceSelectVideo(s, sv),

            YtAction.SortChanged sc =>
                ReduceSortChanged(s, sc),

            YtAction.PlayerStateChanged psc =>
                ReducePlayerStateChanged(s, psc),

            YtAction.VideoEnded =>
                s, // Next is done as an effect (dispatch)
            _ => s
        };
    
    private static YouTubePlayerState ReduceSelectVideo(YouTubePlayerState s, YtAction.SelectVideo a)
    {
        if (s.Queue.Videos.Count == 0) return s;
        if (a.Index < 0 || a.Index >= s.Queue.Videos.Count) return s;

        var video = s.Queue.Videos[a.Index];

        return s with
        {
            Queue = s.Queue with { CurrentIndex = a.Index },
            Player = new PlayerState.Loading(video.YouTubeId, a.Autoplay)
        };
    }

    private static YouTubePlayerState ReduceSortChanged(YouTubePlayerState s, YtAction.SortChanged a)
    {
        var videos = s.Queue.Videos;
        if (videos.Count == 0) return s;
        if (a.OldIndex == a.NewIndex) return s;
        if (a.OldIndex < 0 || a.OldIndex >= videos.Count) return s;
        if (a.NewIndex < 0 || a.NewIndex >= videos.Count) return s;

        var list = videos.ToList();
        var moved = list[a.OldIndex];
        list.RemoveAt(a.OldIndex);
        list.Insert(a.NewIndex, moved);

        // Update CurrentIndex
        int? current = s.Queue.CurrentIndex;
        if (current is { } ci)
        {
            if (ci == a.OldIndex) current = a.NewIndex;
            else if (a.OldIndex < ci && a.NewIndex >= ci) current = ci - 1;
            else if (a.OldIndex > ci && a.NewIndex <= ci) current = ci + 1;
        }

        // Reset positions
        for (int i = 0; i < list.Count; i++)
            list[i].Position = i;

        return s with { Queue = s.Queue with { Videos = list, CurrentIndex = current } };
    }

    private static YouTubePlayerState ReducePlayerStateChanged(YouTubePlayerState s, YtAction.PlayerStateChanged a)
    {
        var expected = s.Player switch
        {
            PlayerState.Loading x => x.VideoId,
            PlayerState.Buffering x => x.VideoId,
            PlayerState.Playing x => x.VideoId,
            PlayerState.Paused x => x.VideoId,
            _ => null
        };

        var id = a.VideoId ?? expected;

        if (s.Player is not PlayerState.Loading)
        {
            if (id is null || expected is null || id != expected)
                return s;
        }

        if (s.Player is PlayerState.Loading && id is null)
            return s;

        var newPlayer = a.YtState switch
        {
            3 => new PlayerState.Buffering(id!),// BUFFERING
            1 => new PlayerState.Playing(id!),  // PLAYING
            2 => new PlayerState.Paused(id!),   // PAUSED
            5 => new PlayerState.Paused(id!),   // CUED
            0 => new PlayerState.Paused(id!),   // ENDED
            -1 => s.Player,                     // UNSTARTED - ignore
            _ => s.Player
        };

        return s with { Player = newPlayer };
    }
    
    // Effects (interop + service)
    private async Task RunEffects(YtAction action)
    {
        switch (action)
        {
            case YtAction.Initialize:
            {
                var playlists = await _playlistService.GetAllPlaylistsAsync();
                await Dispatch(new YtAction.PlaylistsLoaded(playlists));

                if (playlists.Count > 0)
                    await Dispatch(new YtAction.SelectPlaylist(playlists[0].Id));

                break;
            }

            case YtAction.SelectPlaylist sp:
            {
                var playlist = await _playlistService.GetPlaylistByIdAsync(sp.PlaylistId);
                
                await Dispatch(new YtAction.PlaylistLoaded(playlist!));

                if (playlist?.VideoItems.Count != 0)
                    await Dispatch(new YtAction.SelectVideo(0, Autoplay: false));

                break;
            }

            case YtAction.SelectVideo sv:
            {
                var videos = State.Queue.Videos;
                if (sv.Index < 0 || sv.Index >= videos.Count) break;

                var video = videos[sv.Index];
                await _jsRuntime.InvokeVoidAsync("YouTubePlayerInterop.loadVideo", video.YouTubeId, sv.Autoplay);
                break;
            }

            case YtAction.SortChanged:
            {
                var pid = State.Queue.SelectedPlaylistId;
                if (pid is null) break;
                await _playlistService.UpdateVideoPositionsAsync(pid.Value, State.Queue.Videos.ToList());
                break;
            }

            case YtAction.VideoEnded:
            {
                if (State.Queue.CurrentIndex is { } i && i < State.Queue.Videos.Count - 1)
                    await Dispatch(new YtAction.SelectVideo(i + 1, Autoplay: true));
                break;
            }
            
            case YtAction.AddVideo av:
            {
                var youtubeId = ExtractYouTubeId(av.Url);
                if (string.IsNullOrWhiteSpace(youtubeId))
                {
                    // goldbarth: Dispatch OperationFailed
                    break;
                }

                var video = new VideoItem
                {
                    Id = Guid.NewGuid(),
                    YouTubeId = youtubeId,
                    Title = av.Title,
                    ThumbnailUrl = $"https://img.youtube.com/vi/{youtubeId}/mqdefault.jpg"
                };

                await _playlistService.AddVideoToPlaylistAsync(av.PlaylistId, video);

                var playlist = await _playlistService.GetPlaylistByIdAsync(av.PlaylistId);
                await Dispatch(new YtAction.PlaylistLoaded(playlist!));
                
                var idx = playlist!.VideoItems
                    .OrderBy(v => v.Position)
                    .ToList()
                    .FindIndex(v => v.Id == video.Id);

                if (idx >= 0)
                    await Dispatch(new YtAction.SelectVideo(idx, Autoplay: false));

                break;
            }
            
            case YtAction.CreatePlaylist cp:
            {
                var playlist = new Playlist
                {
                    Id = Guid.NewGuid(),
                    Name = cp.Name,
                    Description = cp.Description ?? string.Empty,
                    VideoItems = []
                };

                await _playlistService.CreatePlaylistAsync(playlist);

                var playlists = await _playlistService.GetAllPlaylistsAsync();
                await Dispatch(new YtAction.PlaylistsLoaded(playlists));

                await Dispatch(new YtAction.SelectPlaylist(playlist.Id));
                break;
            }
        }
    }
    
    private string ExtractYouTubeId(string url)
    {
        // goldbarth: Easy extraction - can be expanded
        var uri = new Uri(url);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        return query["v"] ?? string.Empty;
    }
}