# SQLRestoreSOS

A .NET 8 / Avalonia (dark mode) desktop tool for **Junior DBAs handling a disaster-recovery incident** — "SOS" as in call 911/112. It walks you through a point-in-time SQL Server restore by clicking through a backup chain instead of hand-typing a long stored-procedure parameter list, shows you the exact T-SQL it's about to run (copy/paste ready), and can execute the restore directly with live progress.

## Download

Grab `SQLRestoreSOS-v1.0.0-win-x64.zip` from the [Releases](https://github.com/ronaldgithub/SQLRestoreSOS/releases) page, unzip, and run
`SQLRestoreSOS.exe`. It is self-contained (no .NET install needed) and Windows-only. The zip also holds
`scripts\sp_DatabaseRestore.sql`, which you install on the SQL Server once (see Prerequisites).

## What it does

- **Full restore** — pick a database and a backup root; Full / Diff / Log folders are derived for you and
  the generated `EXEC sp_DatabaseRestore ...` call is shown before anything runs. **Dry Run (-WhatIf)**
  resolves the real `RESTORE DATABASE` / `RESTORE LOG` statements without executing them.
- **Point in time (STOPAT)** — *Choose on timeline...* opens a dialog with every backup in the order it was
  made, a green check per restorable file, the STOPAT window of each log, and a slider that numbers the
  files a restore to that moment will apply.
- **Log shipping** — stay in `NORECOVERY`, let the app watch the log folder every N seconds, confirm each
  new log, then *Finalize (bring online)*.
- **Advanced** tab — every parameter of the procedure, grouped, with live T-SQL.
- Destructive actions (kill connections / take offline / drop) always need an explicit confirmation.

Walkthrough with screenshots: **[A junior dev dropped a table. Now the junior DBA has to fix it.](https://dbaronald.nl/sqlrestoresos-for-junior-dbas/)**

## Which restore engine is better?

SQLRestoreSOS can drive the restore through either of two well-known community stored procedures. They solve the same problem differently, so the app lets you pick (the **Engine** menu in the toolbar), but **`sp_DatabaseRestore` is the default and the only one bundled with this repo.**

| | `sp_DatabaseRestore` (default) | `sp_RestoreGene` (optional) |
|---|---|---|
| Author | Brent Ozar Unlimited — [FirstResponderKit.org](http://FirstResponderKit.org) | Paul Brewer — [restoregene.com](https://restoregene.com) |
| Version read | v8.34 (2026-07-02) | v8.4 (2025-11-20) |
| License | **MIT**, full license text embedded in the script | **None found.** Only a bare `(C) 2012, Paul Brewer` copyright line — no redistribution grant anywhere in the file |
| Bundled in this repo | Yes (`scripts\sp_DatabaseRestore.sql`, committed) | **No** — gitignored; install it yourself from restoregene.com if you want it |
| What it does | Actually **executes** the restore (Full → Diff → Log), or prints copy/paste-ready commands without running them (`@Execute = 'N'`) | **Generates** a `RESTORE DATABASE`/`RESTORE LOG` script only — it never executes anything itself |
| How it finds backups | Reads backup files directly from **filesystem folders** you point it at (`@BackupPathFull`/`Diff`/`Log`) | Walks **msdb backup history** (`msdb.dbo.backupset`/`backupmediafamily`), including LSN chains and "recovery fork" handling |
| Disaster resilience | **Works even if msdb history is gone** (e.g. a rebuilt server) — exactly the "911" scenario this app targets | Depends entirely on msdb history being intact on the server you're restoring from |
| Dependencies | Requires Ola Hallengren's `dbo.CommandExecute` proc already installed in the same database context | None beyond `msdb` read access |
| Point-in-time | `@StopAt` (string, `yyyyMMddhhmmss`) | `@StopAt` (native `DATETIME`) |
| Standout options | `@MoveFiles`/`@MoveDataDrive`/`@MoveLogDrive` file relocation, `@StandbyMode`, `@RunCheckDB`, `@TestRestore`, perf tuning (`@BufferCount`, `@MaxTransferSize`, `@BlockSize`), `@Debug`/`@Help` | Striped backup sets (up to 20 stripes), Azure blob backup URLs (`@BlobCredential`), backup **fork** handling for chains that were restored to a point-in-time and then continued, `@ExcludeDiffAndLogBackups` (full/diff/log mix), find-and-replace on the generated script text |
| Progress reporting | Rich `RAISERROR ... WITH NOWAIT` messages throughout — captured live via ADO.NET's `SqlConnection.InfoMessage` | N/A (it doesn't execute, so there's nothing to report progress on) |

**Recommendation: `sp_DatabaseRestore` as the default.** Two reasons tip the scale: it has a clear MIT license so we can actually ship it with the app, and its filesystem-based discovery keeps working in the worst-case disaster scenario (a server rebuild where `msdb` history is gone) — which is the exact situation this app exists for. `sp_RestoreGene`'s LSN/fork-walking logic is genuinely more sophisticated for *finding* a chain when `msdb` history is healthy, so it's kept as an opt-in secondary engine for DBAs who already run it and prefer its script-generation style, but you have to install it yourself.

## Prerequisites

- SQL Server instance to restore to/from.
- `dbo.CommandExecute` from [Ola Hallengren's Maintenance Solution](https://ola.hallengren.com/) installed in the database context you run `sp_DatabaseRestore` from (required by that proc, not by this app).
- Run `scripts\sp_DatabaseRestore.sql` once against your server to install the primary engine.
- Optional: install `sp_RestoreGene` yourself from [restoregene.com](https://restoregene.com) if you want the secondary engine — it is **not** distributed with this app (see licensing above).
- **The SQL Server service account (not the app, not your login) needs read access to the backup folders.** All file discovery (`xp_dirtree`, `RESTORE HEADERONLY`, `RESTORE FILELISTONLY`) and the restore itself run *inside* SQL Server, so it's the account the `MSSQLSERVER`/named-instance Windows service runs as that must be able to read `@BackupPathFull`/`@BackupPathDiff`/`@BackupPathLog` — including over UNC/network shares, which commonly need explicit share **and** NTFS read permissions for that service account (not just "Everyone" or your own Windows login). If discovery comes back empty or a restore fails with an "Access is denied" message, this permission is the first thing to check.
- **Use the full UNC path, never a mapped drive letter (e.g. `W:\`).** Mapped drives are per-interactive-session in Windows — they show up fine in your own Explorer, but the SQL Server *service* process can't see them at all, even running as the same account. The app warns about this in the Basic tab, but the underlying symptom (if you hit it elsewhere) is `sp_DatabaseRestore` reporting `"(FULL) No rows were returned for that database in path ..."` for a folder that clearly has files in it. Switch the backup root to `\\server\share\...` and it resolves immediately.

## Build & run

```sh
dotnet build SQLRestoreSOS.sln
dotnet test tests\SQLRestoreSOS.Core.Tests
dotnet run --project src\SQLRestoreSOS.App
dotnet publish src\SQLRestoreSOS.App -p:PublishProfile=win-x64 -o publish   # release build
```

Close a running instance before `dotnet build`; the running app locks its DLLs.

## Basic usage

1. **Connect** — server name, Windows or SQL login auth.
2. Pick the database to restore. The target name defaults to `<name>_<timestamp>` so the restore lands
   *next to* the original.
3. Enter the **backup root** share once (e.g. `\\fileserver\SQLBackup`) — the Full/Diff/Log
   folders are then derived automatically as `<root>\<server>\<database>\{FULL|DIFF|LOG}\`, which is
   the default folder layout [Ola Hallengren's Maintenance Solution](https://ola.hallengren.com/)
   backup jobs produce. Edit the three fields by hand if your backups don't follow that layout.
4. Leave *Point in time* at "Latest available", or click **Choose on timeline...**, drag the slider (or
   type a time) and click **Use this point in time**. The numbered files are the ones that will be applied.
5. Review the generated T-SQL (**Dry Run** shows the real statements) — copy it, or click **Run Restore**
   (pinned at the bottom) to execute it directly with the live restore log.
6. *Log shipping:* tick **Log shipping mode**, run the initial restore, then use **Auto-check every N
   seconds** (or **Check for new log backups**). You confirm each log; **Finalize** brings the database online.

## License

This repository is MIT licensed (see `LICENSE`), © Ronald de Groot. `scripts\sp_DatabaseRestore.sql` is bundled under its own embedded MIT license, © Brent Ozar Unlimited. `sp_RestoreGene` is a separate third-party product by Paul Brewer (restoregene.com) and is **not** covered by this repository's license — it is never bundled or distributed here.
