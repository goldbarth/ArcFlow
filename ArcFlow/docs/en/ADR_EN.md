
# 📐 Architecture Decision Records (ADR-Light)

> Compact documentation of the key architectural decisions in this project — not as a formal RFC, but as traceable reasoning.

---

## ADR-001: Store Architecture over MVVM

**Decision**
Central store with unidirectional data flow instead of the classic MVVM pattern.

**Reasoning**
MVVM is familiar to me from WPF development and works well there. For this project, I deliberately wanted to learn a different architecture based on a single source of truth that enforces explicit, traceable state transitions. The store approach makes state changes testable and predictable — especially with async flows and JS interop.

**Consequences**
More boilerplate (actions, reducers, effects), but a clear separation of state logic and side effects. Every change is traceable and reproducible.

---

## ADR-002: Explicit JS Interop over Blazor Abstractions

**Decision**
JavaScript APIs (YouTube IFrame, SortableJS) are integrated via explicit interop calls — not through Blazor wrappers or third-party components.

**Reasoning**
Blazor wrappers often hide internal state that doesn't live in the store. With two simultaneous state sources (Blazor + JS), race conditions and hard-to-trace bugs emerge. Explicit interop ensures that JS serves only as an execution layer while all state remains in the store.

**Consequences**
More manual interop code, but no hidden state between C# and JavaScript. Every JS-side effect flows back into the store as an action.

---

## ADR-003: Immutable Records for State Slices

**Decision**
Feature state is modeled as C# `record` types — changes always produce new instances via `with` expressions.

**Reasoning**
Immutable state prevents accidental mutation outside the reducer. Change detection becomes trivial (reference comparison instead of deep compare), and the foundation for future features like undo/redo is built in from the start.

**Consequences**
Slightly more allocation from new instances, which is irrelevant at this project's scale. In return: guaranteed correct state transitions and simpler debugging.

---

## ADR-004: SortableJS Outside of Blazor Diffing

**Decision**
Drag & drop runs entirely through SortableJS directly on the DOM — not through Blazor components or MudBlazor DnD.

**Reasoning**
Drag & drop is a DOM problem, not a UI state problem. SortableJS works directly on the DOM without virtual DOM overhead, delivers clean `oldIndex`/`newIndex` events, and requires no continuous syncing during movement. A single event at the end of the drag is enough to update the store. Component-based solutions would trigger re-renders on every mouse move and introduce additional race conditions with Blazor's diffing.

**Consequences**
Blazor "knows nothing" about DOM manipulation during the drag — only the `onEnd` event flows into the store as an action. This requires deliberate lifecycle handling, but keeps the data flow clean and performant.

---

## ADR-005: ImmutableList for State Collections

**Decision**
Collections in state (e.g. `Videos`, `Playlists`) are modeled as `ImmutableList<T>` instead of `List<T>`.

**Reasoning**
`ImmutableList` enforces immutable collections and prevents accidental mutations outside the reducer. Every change creates a new collection instance, simplifying change detection and eliminating race conditions during concurrent access. The slightly higher allocation overhead is negligible at this project's scale.

**Consequences**
- Reducers must explicitly call `.ToImmutableList()` after mutations
- Collections are guaranteed threadsafe for read access
- Foundation for future features like undo/redo is established

---

## ADR-006: Channel-based Action Queue

**Decision**
Actions are serialized through a `Channel<YtAction>` instead of `SemaphoreSlim`.

**Reasoning**
`Channel<T>` is more idiomatic for producer-consumer patterns in modern .NET and provides built-in backpressure mechanisms. Action processing runs in a dedicated background task that can be cleanly stopped via `CancellationToken`. This prevents race conditions and guarantees FIFO ordering.

**Consequences**
- All actions are processed serially (no parallelism)
- Clean lifecycle management via `IDisposable`
- Easier testability through deterministic behavior

---

## ADR-007: Exhaustive Pattern Matching in Reducer

**Decision**
The reducer uses exhaustive pattern matching with `UnreachableException` for unhandled actions.

**Reasoning**
The compiler enforces explicit handling of all action types. New actions cannot be accidentally "forgotten". Actions that only trigger side-effects (e.g. `CreatePlaylist`, `AddVideo`) explicitly return unchanged state. This makes the intent clear in code.

**Consequences**
- Compiler-guaranteed action completeness
- Clear documentation of which actions change state and which don't
- Runtime exception for forgotten actions (instead of silent ignore)

---

## ADR-008: Result Pattern for Error Handling

**Decision**
Expected failure cases in store operations are handled via a `Result<T>` pattern instead of exceptions.

**Reasoning**
Exceptions are meant for unexpected errors. Validation failures (e.g. invalid YouTube URL), missing resources, or external API errors are *expected* and should not interrupt normal control flow. The Result pattern allows explicit distinction between `Success(T)` and `Failure(OperationError)` with categorized errors (`Validation`, `NotFound`, `Transient`, `External`, `Unexpected`).

**Consequences**
- Effects can handle errors specifically instead of relying solely on try-catch
- Error categories enable differentiated UI responses (Warning vs. Error)
- `OperationContext` with correlation IDs allows log correlation across async flows
- Technical error details remain separated from user-facing messages

---

## ADR-009: Notification System with Toast UI

**Decision**
User feedback is managed through a centralized notification system in store state — rendered as toast notifications with auto-dismiss.

**Reasoning**
Error messages, warnings, and success messages are UI state and belong in the store. Instead of `alert()` calls or component-local error states, an `ImmutableList<Notification>` is maintained in `YouTubePlayerState`. The `NotificationPanel` component renders these as color-coded, animated toast messages. Auto-dismiss (5s) and manual close are controlled via dedicated actions (`ShowNotification`, `DismissNotification`).

**Consequences**
- Notifications are part of the unidirectional data flow
- No hidden UI state for error messages
- Severity mapping: `Validation`/`NotFound`/`Transient` → Warning, `External`/`Unexpected` → Error
- Notifications can be verified in tests via state inspection

---

## ADR-010: Structured Logging with Operation Context

**Decision**
All store effects log in a structured manner via `ILogger<YouTubePlayerStore>` with an `OperationContext` containing operation name, correlation ID, and entity IDs.

**Reasoning**
In async flows (DB → JS interop → follow-up actions), correlating log entries without explicit context is nearly impossible. The `OperationContext` is created for every operation and contains a unique `CorrelationId` along with optional `PlaylistId`, `VideoId`, and `Index`. Log levels are derived from `ErrorCategory` (Warning/Error). Successful operations are logged at `Information` level.

**Consequences**
- Every operation is traceable via its correlation ID
- Technical log details and user-facing messages are explicitly separated
- Log entries contain structured properties for machine-readable analysis
- For `Unexpected` errors, the correlation ID is displayed in the notification to enable log tracing

---

## ADR-011: Snapshot-based Undo/Redo for QueueState

**Decision**
Undo/Redo is implemented exclusively for `QueueState` — via a Past/Present/Future snapshot model with `ImmutableList<QueueSnapshot>` stacks in state.

**Reasoning**
The store architecture with immutable state and pure reducers is ideally suited for time-travel features. Rather than building a generic command stack, snapshots of `QueueState` are captured before each undoable action. This is simpler, more direct, and avoids the complexity of inverse operations.

**Critical detail — `VideoPositions`:** `VideoItem` is a mutable class (not a record). `HandleSortChanged` mutates `Position` in-place on shared references. Without a separate `VideoPositions` array in the snapshot, past snapshots would be silently corrupted by later sorts. The parallel array captures `Position` values at snapshot time and restores them on undo.

**UndoPolicy** determines behavior per action:
- `SelectVideo`, `SortChanged` → undoable (snapshot pushed to Past)
- `PlaylistLoaded`, `SelectPlaylist` → boundary (complete history reset)
- All others → history unchanged

**Effect gating:** `UndoRequested`/`RedoRequested` skip `RunEffects` entirely — undo/redo is purely reducer-based, with no DB persistence or JS interop.

**Consequences**
- Only queue mutations are undoable — player state and playlist management remain outside
- History limit of 30 entries prevents memory issues
- New undoable actions only require an update to `UndoPolicy.IsUndoable()`
- Comprehensive test coverage (27 tests) ensures correctness