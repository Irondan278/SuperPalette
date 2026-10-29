# Super Palette

A PowerToys Command Palette extension for capturing tasks into Super Productivity.

Shown in the app as **Super Palette**; the code, projects and folders still use the
original working name, TaskImporter.

Talks directly to Super Productivity's Local REST API on `127.0.0.1:3876`. No
Super Productivity plugin is involved.

## State of play

| Part | Status |
| --- | --- |
| `src/TaskImporter.Core` | Written and tested — parser, link handling, routing seam |
| `src/TaskImporter.SuperProductivity` | Written — REST client, reference cache |
| `tests/TaskImporter.Core.Tests` | 106 tests, all passing |
| `TaskImporterExtension/` | Builds clean, registers locally — Add task, Capture clipboard, Current task, and a dock band |

Still to do: a live run against a real Super Productivity instance to confirm
how `dueWithTime` is encoded, and a check of the dock band in a live Dock.

## Before anything else

1. In Super Productivity: **Settings → Misc → Enable local REST API**, then copy
   the access token shown there.
2. In Windows: **Settings → System → For developers → Developer Mode** on.
3. Install the **.NET 9 SDK** (libraries and tests) and the **.NET 10 SDK**
   (the extension).
4. Install the **Windows SDK 10.0.26100**. This one is easy to miss and the
   build fails without it — `Microsoft.CommandPalette.Extensions` ships a
   `.winmd`, and projecting it needs `Windows.winmd` from an installed SDK.
   Symptom: *"Could not find the Windows SDK in the registry"*. winget has no
   10.0.26100 package; use the installer from the Windows SDK archive.

## Running the tests

```powershell
dotnet run --project tests\TaskImporter.Core.Tests
```

Takes about a second and needs no network, no NuGet restore and no Super
Productivity. This is the fast loop; use it for anything to do with parsing.

The projects target `net9.0`. To build against a different SDK without editing
project files, pass `-p:TaskImporterTfm=net10.0`.

## The build and deploy loop

```powershell
# The extension's COM server keeps running after the palette closes, and it
# holds the build output open. Rebuilding without stopping it fails with
# MSB3027 "file is locked by TaskImporterExtension". Command Palette starts it
# again on demand, so stopping it costs nothing.
Get-Process -Name TaskImporterExtension -ErrorAction SilentlyContinue | Stop-Process -Force

dotnet build TaskImporterExtension\TaskImporterExtension\TaskImporterExtension.csproj -p:Platform=x64
Add-AppxPackage -Register .\TaskImporterExtension\TaskImporterExtension\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\AppxManifest.xml
```

The `Add-AppxPackage` step is only needed when the manifest changes. For a code
change, stop the process, build, and run `Reload` in Command Palette.

Then run `Reload` in Command Palette. Developer Mode makes the unsigned
registration legal; no signing certificate is needed until the extension is
distributed. `-Register` runs the app in place from the build folder — nothing
is copied, nothing is published, and only the current user sees it.

`-p:Platform=x64` is not optional: the template's `Directory.Build.props` sets
`PlatformTarget` from `$(Platform)`, and the default `AnyCPU` is not a platform
it declares.

To remove it again:

```powershell
Get-AppxPackage *TaskImporterExtension* | Remove-AppxPackage
```

## Using it

Three commands appear in the palette:

| Command | What it does |
| --- | --- |
| **Add task** | The quick-add line. Parses as you type; the subtitle explains what will happen before you press Enter. |
| **Capture clipboard** | Opens the quick-add line pre-filled from the clipboard, so you can add a tag or a due date before it goes. |
| **Current task** | What the timer is on, plus today's list. Enter starts or stops; Ctrl+K offers Complete. |

One dock band, **Top task**, shows the running task — or the first open task on
today's list — with time spent against the estimate and a start / stop button.
It polls Super Productivity every 30 seconds and counts a running timer forward
in between.

Settings — access token, API address, default project, and whether to fetch page
titles — live on the extension's settings page. MSIX redirects them into the
package's data folder,
`%LOCALAPPDATA%\Packages\TaskImporterExtension_ajkqh9gttjjec\LocalCache\Local\TaskImporter`,
so rebuilding and re-registering keep the token but `Remove-AppxPackage`
deletes it.

## Quick-add syntax

| Token | Example | Effect |
| --- | --- | --- |
| `+project` | `+Acme IT` | Sets the project. Multi-word names need no quoting. |
| `#tag` | `#onsite` | Adds a tag. Repeatable. |
| `@due` | `@friday`, `@tomorrow`, `@oct5`, `@2026-10-05`, `@+3d`, `@eom` | Sets the due date. |
| `@due time` | `@tomorrow 9am`, `@friday 14:30` | Sets a due date with a time. |
| estimate | `45m`, `2h`, `1h30m`, `1.5h` | Sets the time estimate. |
| `//notes` | `// rack 3, APC 1500` | Everything after goes to the notes. |
| a URL | pasted anywhere in the line | Title from the page, link into the notes. |

Rules: tokens only count at a word boundary, so `C#` and `rob@example.com` are
safe. A `\` before `+`, `#` or `@` makes it literal. Anything that cannot be
resolved stays in the title and is reported, because tags cannot be created
through the API and silently dropping one would be worse.

## Layout

```
src/
  TaskImporter.Core/                 no Windows or CmdPal dependency
    CaptureContext.cs                what was captured and what is known about it
    TaskDraft.cs                     the task on its way out, maps to POST /tasks
    ParseResult.cs                   what the parser made of one line
    DraftFactory.cs                  capture -> draft, including the link rules
    Parsing/QuickAddParser.cs        the syntax above
    Parsing/DateGrammar.cs           @friday, @+3d, @oct5, 9am
    Parsing/DurationGrammar.cs       45m, 1h30m
    Clipboard/HtmlClipboardParser.cs CF_HTML anchor extraction
    Enrichment/PageTitleEnricher.cs  fetches a page and reads its title
    Routing/ITaskRouter.cs           the seam future rules plug into
    Timing/TimeProgress.cs           time spent / estimate, formatted
  TaskImporter.SuperProductivity/
    SuperProductivityClient.cs       the REST client
    ReferenceCache.cs                projects and tags, 60s TTL, never blocks
tests/
  TaskImporter.Core.Tests/           console runner, no dependencies
TaskImporterExtension/               the packaged extension (template's layout)
  TaskImporterExtension/
    TaskImporterSettings.cs          token, address, default project, title fetch
    Icons.cs                         Segoe Fluent glyphs, named
    Services/TaskImporterHost.cs     client + cache + submit path, one per process
    Services/WindowsClipboard.cs     CF_HTML and CF_UNICODETEXT, on an STA thread
    Services/Sync.cs                 async work from a synchronous Invoke
    Pages/AddTaskPage.cs             the quick-add line
    Pages/CurrentTaskPage.cs         the timer and today's list
    Dock/TopTaskBand.cs              the dock band
    Commands/                        submit, capture, start / stop / complete
```

## One thing to verify

`dueWithTime` is sent as epoch milliseconds. That is the one field in the client
not confirmed against a live instance — the API docs list the field but not its
encoding. If a timed due date is rejected, that is the field to change, in
`Models/ApiModels.cs` and `SuperProductivityClient.ToRequest`.

## Routing, later

`ITaskRouter` takes a `CaptureContext` and a `TaskDraft` and returns a draft.
`DefaultsRouter` fills the default project; `PassThroughRouter` does nothing;
`RouterPipeline` chains them. A rule engine becomes one more entry in the
pipeline.

The contract that matters: anything the user typed is marked in
`TaskDraft.ExplicitFields`, and routers leave those alone. Rules fill gaps
rather than argue with the typist.

## License

MIT — see [LICENSE](LICENSE). Parts of `TaskImporterExtension/` come from
Microsoft's Command Palette extension template, also MIT.
