# Architecture

This repository contains a Blazor feature ("YouTube Player") implemented with a strict, store-driven architecture.
The goal is predictable state transitions, a single source of truth, and a clean separation between UI and side effects.

## Principles

### Single Source of Truth
All feature state lives in `YouTubePlayerState` inside the `YouTubePlayerStore`.
The UI never mutates feature data directly.

### Unidirectional Data Flow
User and interop events are translated into **actions**.
Actions are processed by the store in two steps:

1. **Reduce** – pure state transition (no I/O)
2. **Effects** – side effects (DB, JS interop, async calls), optionally dispatching more actions

### UI Is Dispatch-Only
Razor components:
- render based on store state
- dispatch actions on user intent
- hold only local UI state (e.g. drawer open/close)

### Side Effects Live in the Store
Persistence (playlist/video CRUD), JS interop (YouTube iframe), and other asynchronous work is handled in store effects.
Drawers and pages do not write to the database.

---

## Folder Overview (Feature)

Typical layout:

- `Features/YouTubePlayer/Store/`
    - `YouTubePlayerStore.cs` – owns state, dispatch, reduce, and effects
- `Features/YouTubePlayer/Components/`
    - `NotificationPanel.razor` – toast notifications (auto-dismiss, color-coded)
- `Features/YouTubePlayer/State/`
    - `YouTubePlayerState.cs` (root state)
    - sub-states (e.g. `PlaylistsState`, `QueueState`, `PlayerState`)
    - `YtAction.cs` (actions)
    - `Notification.cs` (notification model + severity)
    - `OperationError.cs` (categorized errors + OperationContext)
    - `Result.cs` (Result pattern for expected failures)
- `Features/YouTubePlayer/Models/`
    - domain models (e.g. `Playlist`, `VideoItem`)
- `wwwroot/js/`
    - `youtube-player-interop.js` (YouTube IFrame API interop)
    - `sortable-interop.js` (SortableJS integration)

---

## State Model

### Root State: `YouTubePlayerState`
Contains:
- `Playlists`: loading / empty / loaded (list of playlists)
- `Queue`: selected playlist, ordered list of videos, current index
- `Player`: player status (empty / loading / buffering / playing / paused)
- `Notifications`: `ImmutableList<Notification>` — active notifications for the UI
- `LastError`: `OperationError?` — last encountered error (for debugging)

The UI derives all view decisions from these values.

### Immutability
- State slices are `record` types
- Collections are `ImmutableList<T>`
- Changes produce new instances via `with` expressions

### Queue
- `SelectedPlaylistId`: `Guid?`
- `Videos`: `ImmutableList<VideoItem>` (sorted by `Position`)
- `CurrentIndex`: `int?` (current selection)

### Player
Represents the playback lifecycle and is updated only via actions dispatched from JS interop
(e.g. `PlayerStateChanged`, `VideoEnded`).

---

## Actions

Actions are the only input into the store.

### Commands (Intent)
Triggered by the UI:
- `Initialize`
- `SelectPlaylist(playlistId)`
- `SelectVideo(index, autoplay)`
- `CreatePlaylist(...)`
- `AddVideo(...)`
- `SortChanged(oldIndex, newIndex)`

### Results (Loaded / Derived)
Triggered by effects:
- `PlaylistsLoaded(playlists)`
- `PlaylistLoaded(playlist)`

### Error Handling & Notifications
- `OperationFailed(OperationError)` — categorized error with context
- `ShowNotification(Notification)` — displays notification in the UI
- `DismissNotification(CorrelationId)` — removes notification (manual or auto-dismiss)

### Action Categories
**State-changing actions:**
- Reducer returns new state
- Examples: `SelectVideo`, `PlaylistLoaded`, `SortChanged`

**Effect-only actions:**
- Reducer returns unchanged state (`state`)
- Logic runs exclusively in effects
- Examples: `CreatePlaylist`, `AddVideo`
- Dispatch subsequent result actions

Rule of thumb:
- **Commands** express what the user wants
- **Results** represent completed I/O

---

## Store Pipeline

### Dispatch
`Dispatch(action)` writes actions into a `Channel<YtAction>`.
Processing happens serially in a background task to avoid race conditions.

### Reduce (Pure)
The reducer returns a new immutable `YouTubePlayerState`.
No DB calls, no JS calls, no timing dependencies.

**Exhaustive Pattern Matching:**

```csharp
var newState = action switch 
{ 
    YtAction.SelectVideo sv => HandleSelectVideo(state, sv), 
    YtAction.CreatePlaylist => state, // Effect-only _ => throw new UnreachableException(...) 
};
```

The compiler enforces handling of all actions.

### Effects (Async)
Effects are triggered after reducing.
They may:
- call services (DB, HTTP)
- call JS interop
- dispatch additional actions

### Lifecycle
The store implements `IDisposable`:
- `CancellationToken` stops background processing
- Channel is closed
- Clean shutdown without race conditions

---

## UI Components

### Page (`YouTubePlayer.razor`)
Responsibilities:
- initialize the store after JS interop is ready
- render playlists, queue, and controls from store state
- dispatch actions on clicks
- forward JS callbacks into store actions

Allowed local UI state:
- drawer open flags
- SortableJS initialization flags

### NotificationPanel (`NotificationPanel.razor`)
Responsibilities:
- Renders active notifications from store state as toast messages
- Manages auto-dismiss timers (5s) via `CancellationTokenSource`
- Forwards manual close as `DismissNotification` action
- Implements `IDisposable` for clean timer cleanup

### Drawers
Drawers are input components:
- collect user input
- emit `EventCallback` to parent component
- parent translates requests into actions and dispatches
- do not persist data

**Why no direct dispatch?**
- Reusability (not coupled to store)
- Separation of concerns
- Better testability

---

## JS Interop

### YouTube Player
`youtube-player-interop.js`:
- loads the YouTube IFrame API
- creates and controls the player
- forwards state changes to .NET

JS calls into .NET via `[JSInvokable]` methods, which are translated into actions.

### SortableJS
SortableJS mutates the DOM.
Therefore:
- initialization and teardown are controlled explicitly
- reorder events are translated into actions
- persistence happens in store effects

---

## Typical Flows

### App Start
1. UI initializes JS interop
2. UI dispatches `Initialize`
3. Effect loads playlists → `PlaylistsLoaded`
4. Optionally select first playlist → `SelectPlaylist`
5. Effect loads playlist → `PlaylistLoaded`
6. Optionally select first video → `SelectVideo`
7. Effect calls JS `loadVideo`

### Select Video
1. UI dispatches `SelectVideo`
2. Reducer updates queue and player state
3. Effect calls JS `loadVideo`

### Create Playlist
1. Drawer emits `EventCallback<CreatePlaylistRequest>`
2. Page dispatches `CreatePlaylist`
3. Reducer returns unchanged state
4. Effect writes to DB
5. Effect dispatches `PlaylistsLoaded` (reloads)
6. Effect dispatches `SelectPlaylist` (selects new playlist)

---

## Error Handling

### Result Pattern
Expected failure cases are handled via `Result<T>` instead of exceptions:
- `Result<T>.Success(value)` — successful operation
- `Result<T>.Failure(OperationError)` — categorized error with context

### Error Categories (`ErrorCategory`)
| Category | Meaning | UI Severity |
|----------|---------|-------------|
| `Validation` | Input validation failed (e.g. invalid YouTube URL) | Warning |
| `NotFound` | Resource not found or state conflict | Warning |
| `Transient` | Network/timeout errors — potentially retryable | Warning |
| `External` | JS interop or external API errors | Error |
| `Unexpected` | Unexpected bugs / unhandled exceptions | Error |

### OperationContext
Every operation creates an `OperationContext` with:
- `Operation`: operation name (e.g. `"AddVideo"`)
- `CorrelationId`: unique ID for log correlation
- `PlaylistId`, `VideoId`, `Index`: optional entity references

### Error Flow
1. Effect executes operation
2. On failure: create `OperationError` with category and context
3. Dispatch `OperationFailed(error)` → reducer stores `LastError`
4. Dispatch `ShowNotification(notification)` → UI shows toast
5. Structured logging with correlation ID and entity IDs

### Notifications
- `NotificationPanel` component renders `ImmutableList<Notification>` as toast messages
- Color-coded by severity (Success: green, Info: blue, Warning: yellow, Error: red)
- Auto-dismiss after 5 seconds, manual close via `DismissNotification`
- Slide-in animation, fixed positioned (top right)

### YouTube URL Validation
`ExtractYouTubeId` validates multiple URL formats:
- `youtube.com/watch?v=ID`
- `youtu.be/ID`
- `youtube.com/embed/ID`
- `IsValidYouTubeId`: validates 11-character YouTube ID format

Invalid URLs are treated as `Validation` errors with user-friendly messages.

### Logging Strategy
- `ILogger<YouTubePlayerStore>` for all store effects
- Log levels derived from `ErrorCategory` (Warning/Error)
- Successful operations logged at `Information` level
- Structured properties: `Operation`, `Category`, `CorrelationId`, `PlaylistId`, `VideoId`, `Exception`
- User-facing messages explicitly separated from technical log details

---

## Testing Notes
- **Reducer**: unit-testable (state + action → new state)
- **Effects**: testable via mocked services and asserted follow-up actions
- **Drawers**: component tests without store dependency

---

## Debug Features

Available in `DEBUG` mode:
- **State History**: Last 50 transitions (timestamp, action, before/after)
- Access via `Store.GetHistory()`
- Useful for time-travel debugging