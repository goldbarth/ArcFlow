# BlazorStore

Ein Blazor Server Referenzprojekt für store-gesteuertes State Management — gebaut, um zu zeigen,
wie eine konsequente Architektur im .NET-Frontend-Bereich aussieht.

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet)
![Blazor](https://img.shields.io/badge/Blazor-Server-512BD4?style=flat-square&logo=blazor)
![C#](https://img.shields.io/badge/C%23-14.0-239120?style=flat-square&logo=csharp)
![SQLite](https://img.shields.io/badge/SQLite-003B57?style=flat-square&logo=sqlite)

---

## Hintergrund

Die meisten Blazor-Beispiele enden bei Component State oder Cascade Parameters. Dieses Projekt
verfolgt einen anderen Ansatz: strikter unidirektionaler Datenfluss mit reinen Reducern, isolierten
Effects und einer Channel-basierten Action Queue — konsequent angewendet, nicht nur dort, wo es
gerade passt.

Das Feature (ein YouTube-Playlist-Manager) ist zweitrangig. Die Architektur ist der Punkt.

---

## Architektur

```
UI → Action → Reducer → State → UI
                ↓
            Effects (DB, JS Interop)
```

| Prinzip | Umsetzung |
|---------|-----------|
| Single Source of Truth | Gesamter Feature-Zustand lebt im Store |
| Pure Reducers | Kein DB-Zugriff, kein JS, keine Async-Logik |
| Side-Effect Isolation | DB und JS Interop ausschließlich in Effects |
| Dispatch-only UI | Komponenten lesen State und dispatchen — keine direkte Manipulation |

→ [Architekturübersicht](BlazorStore/docs/en/ARCHITECTURE_EN.md)  
→ [Architectural Decision Records](BlazorStore/docs/en/ADR_EN.md)

---

## Umfang

Der Playlist-Manager kombiniert bewusst mehrere unabhängige Komplexitätsebenen:

- **Store-Architektur** — unidirektionaler Datenfluss, Channel-basierte Action Queue, immutable Records
- **Undo/Redo** — Snapshot-basiertes Time Travel mit Past/Future Stacks und Effect Gating
- **Shuffle & Repeat** — Fisher-Yates-Permutation, Playback-History-Stack, drei Repeat-Modi,
  reine `PlaybackNavigation`-Strategiefunktionen
- **Import/Export** — JSON-Pipeline (DTO → Serializer → JS-Download), Schema-Versionierung,
  Discriminated Union State Machine für den Lifecycle, Dirty Tracking mit Persist Effect
- **YouTube IFrame Integration** — kontrolliertes JS Interop, explizites PlayerState Tracking
- **Drag & Drop** — SortableJS außerhalb von Blazors Diffing-Zyklus
- **Fehlerbehandlung** — Result Pattern, kategorisierte Fehler, Structured Logging, MudBlazor Snackbar

---

## Stack

| Technologie | Version |
|-------------|---------|
| .NET / C# | 10.0 / 14.0 |
| Blazor Server | — |
| Entity Framework Core + SQLite | 10.0.2 |
| MudBlazor | 8.15.0 |
| xUnit | 2.9.3 |

---

## Struktur

```
BlazorStore/
├── Features/YouTubePlayer/
│   ├── Components/         # Feature-UI
│   ├── ImportExport/       # DTO, Mapper, Serializer, Import Policy
│   ├── Models/             # Domänenmodelle
│   ├── State/              # State Slices, Actions, Result Types
│   └── Store/              # Store, Reducer, Effects, Logging
├── Data/                   # EF Core DbContext + Fluent API Mappings
├── Components/             # Layout, Pages, geteilte UI-Komponenten
└── wwwroot/                # Static Assets, JS Interop

BlazorStore.Tests/
├── PlaybackNavigationTests.cs
├── ShuffleRepeatReducerTests.cs
├── UndoRedoReducerTests.cs
├── ImportExportReducerTests.cs
├── ExportPipelineTests.cs
└── ...
```