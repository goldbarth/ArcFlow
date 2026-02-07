# Architektur

Dieses Repository enthält ein Blazor-Feature ("YouTube Player"), das mit einer strikt store-getriebenen Architektur umgesetzt ist.
Ziel sind vorhersagbare Zustandsübergänge, eine zentrale Datenquelle und eine saubere Trennung zwischen UI und Side-Effects.

## Prinzipien

### Single Source of Truth
Der komplette Feature-State lebt im `YouTubePlayerState` innerhalb des `YouTubePlayerStore`.
Die UI verändert Feature-Daten niemals direkt.

### Unidirektionaler Datenfluss
User- und Interop-Ereignisse werden in **Actions** übersetzt.
Die Verarbeitung erfolgt im Store in zwei Schritten:

1. **Reduce** – reine Zustandsänderung (kein I/O)
2. **Effects** – Side-Effects (DB, JS-Interop, asynchrone Aufrufe), optional mit Folge-Actions

### UI ist Dispatch-only
Razor-Komponenten:
- rendern ausschließlich auf Basis des Store-States
- dispatchen Actions bei User-Interaktionen
- halten nur lokalen UI-State (z. B. Drawer offen/geschlossen)

### Side-Effects liegen im Store
Persistenz (Playlist-/Video-CRUD), JS-Interop (YouTube-Iframe) und sonstige asynchrone Logik liegen in den Store-Effects.
Drawer und Pages greifen nicht direkt auf die Datenbank zu.

---

## Ordnerübersicht (Feature)

Typische Struktur:

- `Features/YouTubePlayer/Store/`
  - `YouTubePlayerStore.cs` – verwaltet State, Dispatch, Reducer und Effects
- `Features/YouTubePlayer/State/`
  - `YouTubePlayerState.cs` (Root-State)
  - Sub-States (z. B. `PlaylistsState`, `QueueState`, `PlayerState`)
  - `YtAction.cs` (Actions)
- `Features/YouTubePlayer/Models/`
  - Domänenmodelle (z. B. `Playlist`, `VideoItem`)
- `wwwroot/js/`
  - `youtube-player-interop.js` (YouTube IFrame API)
  - `sortable-interop.js` (SortableJS)

---

## State-Modell

### Root-State: `YouTubePlayerState`
Enthält:
- `Playlists`: loading / empty / loaded (Liste der Playlists)
- `Queue`: ausgewählte Playlist, sortierte Videoliste, aktueller Index
- `Player`: Player-Zustand (empty / loading / buffering / playing / paused)

Alle UI-Entscheidungen werden aus diesen Werten abgeleitet.

### Queue
- `SelectedPlaylistId`: `Guid?`
- `Videos`: sortierte Liste (nach `Position`)
- `CurrentIndex`: `int?` (aktuelle Auswahl)

### Player
Repräsentiert den Wiedergabe-Lifecycle und wird ausschließlich über Actions aus der JS-Interop aktualisiert
(z. B. `PlayerStateChanged`, `VideoEnded`).

---

## Actions

Actions sind der einzige Eingang in den Store.

### Commands (Intent)
Von der UI ausgelöst:
- `Initialize`
- `SelectPlaylist(playlistId)`
- `SelectVideo(index, autoplay)`
- `CreatePlaylist(...)`
- `AddVideo(...)`
- `SortChanged(oldIndex, newIndex)`

### Results (Loaded / Abgeleitet)
Von Effects ausgelöst:
- `PlaylistsLoaded(playlists)`
- `PlaylistLoaded(playlist)`
- optionale Fehlerbehandlung (z. B. `OperationFailed(message)`)

Faustregel:
- **Commands** beschreiben Nutzerintentionen
- **Results** beschreiben abgeschlossene I/O-Operationen

---

## Store-Pipeline

### Dispatch
`Dispatch(action)` reiht Actions ein und verarbeitet sie seriell, um Race-Conditions zu vermeiden.

### Reduce (Pure)
Der Reducer erzeugt einen neuen, unveränderlichen `YouTubePlayerState`.
Keine DB-Zugriffe, keine JS-Aufrufe, keine asynchronen Abhängigkeiten.

### Effects (Async)
Effects laufen nach dem Reduce-Schritt und dürfen:
- Services aufrufen (DB, HTTP)
- JS-Interop nutzen
- weitere Actions dispatchen

---

## UI-Komponenten

### Page (`YouTubePlayer.razor`)
Aufgaben:
- Initialisierung des Stores nach JS-Interop-Setup
- Rendern von Playlists, Queue und Controls aus dem Store-State
- Dispatch von Actions bei Klicks
- Weiterleitung von JS-Callbacks in Store-Actions

Erlaubter lokaler UI-State:
- Drawer-Flags
- SortableJS-Lifecycle-Flags

### Drawer
Drawer sind reine Eingabekomponenten:
- sammeln Nutzereingaben
- senden Requests an Page/Store
- persistieren keine Daten

---

## JavaScript-Interop

### YouTube Player
`youtube-player-interop.js`:
- lädt die YouTube IFrame API
- erstellt und steuert den Player
- leitet State-Änderungen an .NET weiter

JS ruft `[JSInvokable]`-Methoden auf, die in Actions übersetzt werden.

### SortableJS
SortableJS verändert das DOM direkt.
Daher:
- kontrollierte Initialisierung und Zerstörung
- Sortierereignisse werden in Actions übersetzt
- Persistenz erfolgt in Store-Effects

---

## Typische Abläufe

### App-Start
1. UI initialisiert JS-Interop
2. UI dispatcht `Initialize`
3. Effect lädt Playlists → `PlaylistsLoaded`
4. Optional: erste Playlist auswählen → `SelectPlaylist`
5. Effect lädt Playlist → `PlaylistLoaded`
6. Optional: erstes Video auswählen → `SelectVideo`
7. Effect ruft JS `loadVideo` auf

### Video auswählen
1. UI dispatcht `SelectVideo`
2. Reducer aktualisiert Queue und Player-State
3. Effect ruft JS `loadVideo` auf

---

## Fehlerbehandlung
Fehler sollten nicht aus UI-Event-Handlern geworfen werden.
Stattdessen kann der Store `OperationFailed(message)` dispatchen,
wodurch die UI gezielt Feedback anzeigen kann.

---

## Tests
- Reducer: Unit-Tests (State + Action → neuer State)
- Effects: Tests mit gemockten Services und verifizierten Folge-Actions

