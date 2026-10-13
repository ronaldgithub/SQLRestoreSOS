# CLAUDE.md

Guidance for Claude Code (or any future contributor) working in this repo.

## What this repo is

SQLRestoreSOS is a Windows-only, dark-mode Avalonia (.NET 8) desktop app built for Junior DBAs
handling a disaster-recovery incident ("SOS" — call 911/112). It connects to a SQL Server instance,
lets the DBA pick a database and a point in time off a visual backup chain, and either shows the
generated T-SQL (copy/paste ready) or runs the restore directly with live progress. It drives the
restore through one of two community stored procedures rather than reimplementing restore logic
itself — see the engine comparison in README.md for why `sp_DatabaseRestore` is the bundled default
and `sp_RestoreGene` is an opt-in, user-installed secondary engine.

This app follows the same conventions as its sibling projects by the same author (`SQLtoParquet`,
`SQLModelViewer`) — Core/App project split, CommunityToolkit.Mvvm, no DI container, a
`ConnectDialog`/`AboutDialog` pattern. When in doubt, match that style.

## Solution layout

```
SQLRestoreSOS.sln
global.json                    # pins .NET 8 SDK (8.0.206)
scripts/
  sp_DatabaseRestore.sql       # bundled, MIT, committed
  sp_RestoreGene.sql           # local-only reference copy, gitignored — see "Licensing guardrail" below
src/
  SQLRestoreSOS.Core/           # net8.0 class library — no Avalonia/UI reference, fully unit-testable
    Connections/                # ConnectionProfile, SqlAuthMode, SqlConnectionService
    Databases/                  # DatabaseInfo, DatabaseCatalogService (sys.databases)
    BackupChain/                # BackupFileEntry/Timeline, BackupChainAnalyzer, BackupRestorePlanner,
                                 # BackupFileInspector (RESTORE FILELISTONLY; no UI uses it right now)
      Engines/                  # IRestoreEngine, DatabaseRestoreEngine, RestoreGeneEngine
    Restore/                    # RestoreRequest, RestoreParameterSet, RestoreScriptBuilder,
                                 # ResolvedStatementFormatter, RestoreExecutionService,
                                 # LogShippingSession, LogShippingMonitor
    Settings/                   # AppSettings, SettingsService (%AppData%\SQLRestoreSOS\settings.json)
  SQLRestoreSOS.App/             # net8.0 Avalonia app (Views/ViewModels)
    Properties/PublishProfiles/win-x64.pubxml   # self-contained single-file release build
tests/
  SQLRestoreSOS.Core.Tests/      # xUnit, targets Core only
docs/blog/                       # blog post source (HTML, images referenced from pictures/)
tools/wordpress/publish_post.py  # pushes the post to WordPress (REST API; asks for the Application Password,
                                 # draft by default, --publish to go live)
pictures/                        # blog/README screenshots (blog_*.png)
```

## Key classes and where logic lives

**`Core/Restore/RestoreScriptBuilder.cs`** — pure, offline, no ADO.NET. Given an ordered list of
`(name, kind, value)` tuples, builds a readable `EXEC proc @Param = value, ...` call with a
copyright-attribution comment header. Null values are omitted entirely (that's how "leave at the
proc's own default" is represented) so generated scripts stay short. This is the single most
valuable unit-test target in the app — see `RestoreScriptBuilderTests`.

**`Core/BackupChain/Engines/IRestoreEngine.cs`** — the engine-agnostic contract:
`DiscoverBackupChainAsync`, `GetDefaultParameters`, `DescribeParameters` (drives the Advanced tab),
`BuildScript`, `ExecuteAsync`. Two implementations:

- **`DatabaseRestoreEngine`** (primary) — discovers backups by enumerating filesystem folders
  server-side (`xp_dirtree`) and reading each file's header (`RESTORE HEADERONLY`), mapped via the
  pure `MapHeaderRow` function. Builds the `EXEC [dbo].[sp_DatabaseRestore] ...` call and executes it
  with `@Execute='Y'`/`'N'`, streaming `SqlConnection.InfoMessage` (the proc's own `RAISERROR ...
  WITH NOWAIT` progress messages) into `RestoreProgressEvent`s, plus an optional secondary-connection
  poller against `sys.dm_exec_requests.percent_complete` for a progress bar (needs a
  `secondaryConnectionFactory` passed to its constructor — `MainWindowViewModel` wires this from the
  stored `ConnectionProfile`). `IsInstalledAsync`/`IsCommandExecuteDependencyInstalledAsync` let the
  Connect flow warn early if the proc or its Ola Hallengren `CommandExecute` dependency is missing,
  instead of failing mid-restore.
- **`RestoreGeneEngine`** (optional/secondary) — `sp_RestoreGene` only *generates* script text (it
  never executes), so `ExecuteAsync` here calls the proc with `@RestoreScriptOnly=1` forced on,
  defensively scans the result set for any string column containing `"RESTORE "` (no fixed column
  name is assumed — the exact result shape wasn't fully read from the source file), then either
  reports that text as a preview or splits it on `GO` and executes each batch itself.

**`Core/BackupChain/BackupChainAnalyzer.cs` / `BackupRestorePlanner.cs`** — pure, offline, the logic
behind the timeline dialog. `Analyze` marks each backup `Restorable` (green check), `NotApplicable` or
`Broken` by LSN continuity (a diff needs its base full via `DatabaseBackupLsn == CheckpointLsn`; a log
must overlap the chain end) and gives each linked log a STOPAT window (previous backup's finish → its own
finish). It is a header/LSN check, **not** `RESTORE VERIFYONLY`. `Plan(rows, target)` returns the files a
restore to `target` applies, in order: latest full that finished by then, latest matching diff, then logs
up to and including the one covering the target. A log counts as "after the diff" if its LastLsn is >= the
diff's **or** it finished after it — on an idle database a log taken right after a diff can end at the
diff's own LSN, and `sp_DatabaseRestore` still applies it. A STOPAT at/before a full's finish fails with
Msg 4335, which is why the slider starts one second after the earliest full finished.

**`App/ViewModels/BackupTimelineDialogViewModel.cs`** — the "Choose on timeline..." dialog: grid in
finish order with a proportional timeline bar, a seconds-resolution slider + exact-time box, numbered
(applied) vs dimmed rows via `BackupRestorePlanner`, **Refresh** (rescans through a delegate from
`MainWindowViewModel`, keeps the chosen time), and **Use this point in time** / **Restore to latest**.
The Basic tab no longer has a date picker or an inline chain list; `SelectedPointInTimeUtc` stays null
(= no `@StopAt`) until the dialog sets it.

**`Core/Restore/LogShippingMonitor.cs`** — delegate-based `PeriodicTimer` poller; it only *detects* new
logs and hands them to the UI, which still asks before each one. A skipped or failed log stops it.

**`Core/Restore/LogShippingSession.cs`** — the "stay in NORECOVERY and keep applying new log
backups" workflow (`RestoreRequest.KeepInNoRecoveryForLogShipping`), modeled directly on the
`@ContinueLogs=1, @RunRecovery=0` → ... → `@RunRecovery=1` pattern shown in `sp_DatabaseRestore`'s
own `@Help` examples. `CheckForNewLogsAsync` finds log backups newer than the last one applied;
`ApplyLogAsync` restores one, staying in NORECOVERY; `RecoverNowAsync` finalizes (brings the
database online) and ends the session. It is seeded with the finish time of the newest backup the
initial restore used (server time, from `BackupRestorePlanner`) — never `DateTime.UtcNow`.

**`Core/Restore/RestoreRequest.cs` / `ExistingDatabaseAction.cs`** — `ExistingDatabaseAction` mirrors
`@ExistingDBAction`'s tinyint (`DoNothing`/`KillConnections`/`TakeOffline`/`Drop`). Anything but
`DoNothing` is destructive — `MainWindowViewModel.RunRestoreAsync` gates those behind
`RestoreConfirmationDialog`, which requires an explicit acknowledgement checkbox before the
`Confirm` button becomes clickable.

**`App/ViewModels/ParameterEditorRow.cs`** — the Advanced tab is data-driven from
`IRestoreEngine.DescribeParameters()`: each `RestoreParameterDescriptor` becomes a typed row
(`BitParameterRow`/`IntParameterRow`/`StringParameterRow`/`PathParameterRow`/`DateTimeParameterRow`),
and `AdvancedRestoreView.axaml` picks the right control via Avalonia's **implicit** `DataTemplate`
matching — these must live under `UserControl.DataTemplates`, not `UserControl.Resources` (the
latter is a keyed dictionary and won't type-match; this is an easy mistake, see git history).

**`App/ViewModels/MainWindowViewModel.cs`** — owns the whole connected-session state: connection,
database/backup-chain discovery, the shared `RestoreParameterSet` both tabs read/write, live SQL
preview regeneration (triggered from property-changed partial methods, not a timer/debounce), and
restore execution. Dialogs are shown via plain delegate **properties** (`ShowConnectDialog` etc.,
assigned once with `=` from `MainWindow.axaml.cs`), not C# `event`s — an `event` can only be
`+=`'d from outside the declaring class, which doesn't fit the single-subscriber Window-wires-up-
the-ViewModel pattern used here.

## Licensing guardrail — read before touching `scripts/`

`sp_DatabaseRestore.sql` (Brent Ozar Unlimited / FirstResponderKit.org) has a full MIT license
embedded in its own `@Help` block — safe to keep committed with attribution preserved.

`sp_RestoreGene.sql` (Paul Brewer / restoregene.com) has **no license grant text anywhere in the
file** — only a bare `(C) 2012, Paul Brewer` copyright line. It is `.gitignore`'d
(`scripts/sp_RestoreGene.sql`) and must **never** be `git add`ed or have its source copied into
generated code/comments beyond short factual parameter descriptions. If you're adding features to
`RestoreGeneEngine`, work from the parameter names/behavior described in README.md and this file,
not by pasting from the local reference copy.

## Data flow

Connect (`ConnectDialog` → `SqlConnectionService.TestConnectionAsync`) → `MainWindowViewModel`
holds the open connection, checks the selected engine is actually installed
(`CheckEngineAvailabilityAsync`), loads `sys.databases` → DBA picks a database and backup folders →
`ChoosePointInTimeCommand` (the "Choose on timeline..." button) discovers the chain, populates
`BackupChainTimeline` and opens the timeline dialog (point-in-time slider, chain status,
refresh) → every relevant field change calls `RefreshPreview()`, which
calls `engine.GetDefaultParameters` + `engine.BuildScript` to regenerate `SqlPreviewText` and rebuild
the Advanced tab's rows → `RunRestoreCommand` builds the real `RestoreRequest`
(`ExecuteImmediately=true`), gates destructive `ExistingDatabaseAction` values behind
`RestoreConfirmationDialog`, then `RestoreExecutionService.ExecuteAsync` runs the engine and streams
`RestoreProgressEvent`s into the UI *and* `logs\audit.jsonl` (best-effort; a logging failure never
aborts a restore). If log-shipping mode was on, a `LogShippingSession` is created for
`CheckForNewLogsCommand`/`FinalizeLogShippingCommand` to use afterward.

## Build / run / test

```
dotnet build SQLRestoreSOS.sln
dotnet run --project src\SQLRestoreSOS.App
dotnet test tests\SQLRestoreSOS.Core.Tests\SQLRestoreSOS.Core.Tests.csproj
dotnet publish src\SQLRestoreSOS.App -p:PublishProfile=win-x64 -o <dir>   # self-contained single file (~50 MB)
```

Close a running SQLRestoreSOS before `dotnet build`: the app locks its DLLs and the copy step fails
(build to a separate `-o` folder to just compile-check).

Releases: zip `SQLRestoreSOS.exe` + `scripts\sp_DatabaseRestore.sql` + `LICENSE` + `README.md`, tag `vX.Y.Z`,
`gh release create`. `<Version>` in `SQLRestoreSOS.App.csproj` feeds the About dialog.

## Conventions

- MVVM via CommunityToolkit.Mvvm (`[ObservableProperty]`/`[RelayCommand]`); no DI container.
- `SQLRestoreSOS.Core` has zero Avalonia/UI references — keep it that way; it's what makes
  `RestoreScriptBuilder`/parameter-defaults/settings/header-parsing logic testable without a live
  SQL Server (see `tests/` for exactly which parts are unit-tested vs. need a real instance).
- `<Nullable>enable</Nullable>` and `<ImplicitUsings>enable</ImplicitUsings>` in every csproj.
- Avalonia `MenuItem`/`Button` `Header`/`Content` render via `AccessText`, which treats a single `_`
  as an access-key marker and silently eats it — a literal underscore needs `__` (seen with
  `sp_DatabaseRestore`/`sp_RestoreGene` in menu headers; plain `TextBlock.Text` bindings, e.g. in
  `AboutDialog`, are unaffected and don't need escaping).
- `sp_DatabaseRestore`'s `@ContinueLogs = 1` does **not** mean "also apply log backups" (logs are
  applied whenever `@BackupPathLog` is set) — it means "skip the FULL restore, the database is
  already RESTORING". It's driven only by `RestoreRequest.ContinueExistingRestore`, which only
  `LogShippingSession`'s follow-up steps set; setting it on a normal restore drops the FULL from the chain.
- Settings (`%AppData%\SQLRestoreSOS\settings.json`) never contain a password — `RecentServerEntry`
  stores a server name and last-used auth mode only; SQL Login passwords are always re-prompted.
- The SQL Server **service account**, not the app or the logged-in user, needs filesystem read
  access to backup folders — all discovery/restore I/O happens server-side (`xp_dirtree`, `RESTORE
  HEADERONLY`/`FILELISTONLY`, the restore itself). See README.md's Prerequisites.
- The MCP `sqlserver` tooling available in this dev environment (server `win10`) denies
  `master`/`msdb` by policy, which blocks verifying proc installation, the `CommandExecute`
  dependency, and `msdb`-history-based testing through it — use SSMS/sqlcmd for that, or widen the
  policy.

## Status

- [x] Core: connection/catalog services, backup-chain + file-list discovery, script builder, both
      engine adapters, settings, log-shipping session + monitor, chain analyzer/planner — unit tested
      (74 tests, no live SQL Server needed for the testable slice).
- [x] Avalonia shell: dark theme, Connect/About/engine-switch toolbar, Basic tab (database, backup
      folders, "Choose on timeline...", existing-db-action, move-files, log-shipping + auto-check),
      pinned Run Restore, data-driven Advanced tab, resizable panes (Generated T-SQL / resolved
      statements / restore log), Dry Run (-WhatIf), destructive-action confirmation dialog,
      new-log-found dialog, backup-chain timeline dialog. Removed on request: the per-file "Preview
      files" dialog, the inline chain list, the Discover button and the progress bar/percentage
      (`BackupFileInspector` and `PercentComplete` remain in Core/the view model).
- [x] App icon (`Assets/app.ico` — a life-ring/rescue-buoy mark, matching the "SOS" theme).
- [x] Release packaging: `win-x64.pubxml` (self-contained, single file, compressed, not trimmed).
      v1.0.0 published on GitHub Releases as `SQLRestoreSOS-v1.0.0-win-x64.zip`.
- [x] Automatic polling for new log backups in log-shipping mode: `Core/Restore/LogShippingMonitor.cs`
      (PeriodicTimer, delegate-based so it is unit-tested without SQL Server) driven from
      `MainWindowViewModel.UpdatePolling`. It only *detects*; each log is still confirmed in
      `NewLogFoundDialog`. All session SQL goes through `_logShipGate` because there is one shared
      connection. A skipped or failed log stops polling (a later log can't be applied without it).

## Explicit non-goals (for now)

- Cross-platform support — this is a Windows-only tool by design (SQL Server backup/restore is a
  Windows-centric workflow here, and the UI assumes Windows-style paths).
- Bundling or relicensing `sp_RestoreGene` — see the licensing guardrail above.
- A light theme toggle.

## Blog post

`docs/blog/sqlrestoresos-for-junior-dbas.html` is the source of the post on dbaronald.nl (live:
https://dbaronald.nl/sqlrestoresos-for-junior-dbas/). `tools/wordpress/publish_post.py` converts it and
uploads the images through the WordPress REST API with an **Application Password that it asks for at
run time** — never put it in a file, an environment variable you keep, or a chat. The WordPress login
name is the account's login (here the e-mail address), not the Application Password's label.
