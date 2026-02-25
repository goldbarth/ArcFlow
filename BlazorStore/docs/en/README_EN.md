# BlazorStore

A Blazor Server reference project for store-driven state management — built to explore what
"doing it properly" looks like in the .NET frontend space.

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet)
![Blazor](https://img.shields.io/badge/Blazor-Server-512BD4?style=flat-square&logo=blazor)
![C#](https://img.shields.io/badge/C%23-14.0-239120?style=flat-square&logo=csharp)
![SQLite](https://img.shields.io/badge/SQLite-003B57?style=flat-square&logo=sqlite)

---

## Purpose

Most Blazor examples stop at component state or cascade parameters. This project takes a different
approach: strict unidirectional data flow with pure reducers, isolated effects, and a channel-based
action queue — applied consistently, not just where it's convenient.

The feature set (a YouTube playlist manager) is secondary. The architecture is the point.

---

## Architecture
```
UI → Action → Reducer → State → UI
                ↓
            Effects (DB, JS Interop)
```

| Principle | Implementation |
|-----------|----------------|
| Single Source of Truth | All feature state lives in the store |
| Pure Reducers | No DB access, no JS, no async |
| Side-Effect Isolation | DB and JS interop exclusively in Effects |
| Dispatch-only UI | Components read state and dispatch — no direct manipulation |

→ [Architecture Overview](ARCHITECTURE_EN.md)  
→ [Architectural Decision Records](ADR_EN.md)

---

## What's Built

The playlist manager deliberately combines several independent complexity layers:

- **Store architecture** — unidirectional data flow, channel-based action queue, immutable records
- **Undo/Redo** — snapshot-based time travel with Past/Future stacks and effect gating
- **Shuffle & Repeat** — Fisher-Yates permutation, playback history stack, three repeat modes,
  pure `PlaybackNavigation` strategy functions
- **Import/Export** — JSON pipeline (DTO → Serializer → JS download), schema versioning,
  discriminated union state machine for lifecycle management, dirty tracking with persist effect
- **YouTube IFrame integration** — controlled JS interop, explicit PlayerState tracking
- **Drag & Drop** — SortableJS outside Blazor's diffing cycle
- **Error Handling** — Result pattern, categorized errors, structured logging, MudBlazor Snackbar

---

## Tech Stack

| Technology | Version |
|------------|---------|
| .NET / C# | 10.0 / 14.0 |
| Blazor Server | — |
| Entity Framework Core + SQLite | 10.0.2 |
| MudBlazor | 8.15.0 |
| xUnit | 2.9.3 |

---

## Project Structure
```
BlazorStore/
├── Features/YouTubePlayer/
│   ├── Components/         # Feature UI
│   ├── ImportExport/       # DTO, mapper, serializer, import policy
│   ├── Models/             # Domain models
│   ├── State/              # State slices, actions, result types
│   └── Store/              # Store, reducer, effects, logging
├── Data/                   # EF Core DbContext + Fluent API mappings
├── Components/             # Layout, pages, shared UI
└── wwwroot/                # Static assets, JS interop

BlazorStore.Tests/
├── PlaybackNavigationTests.cs
├── ShuffleRepeatReducerTests.cs
├── UndoRedoReducerTests.cs
├── ImportExportReducerTests.cs
├── ExportPipelineTests.cs
└── ...
```
