# Super Palette (TaskImporter) — context for Claude Code

A PowerToys Command Palette extension that captures tasks into Super Productivity.
Danny is the only developer; solo IT.

## The decision everything rests on

Talk to Super Productivity through its **Local REST API** at `127.0.0.1:3876`.
No Super Productivity plugin is involved, including for the link-title feature —
an SP plugin runs in a sandboxed renderer under a restrictive CSP, only sees a
task after it exists, and cannot see the Windows clipboard's HTML flavour.

Auth is a bearer token from Settings → Misc in Super Productivity. Desktop app
only; the API is off by default. Every response is
`{ ok, data | error }`, unwrapped once in `SuperProductivityClient`.

## Current state

Built, compiled and tested (114 tests passing):

- `src/TaskImporter.Core` — quick-add parser, date and duration grammars,
  CF_HTML clipboard parsing, page-title extraction, the routing seam,
  default-project resolution, time-spent formatting (`Timing/TimeProgress`)
- `src/TaskImporter.SuperProductivity` — REST client, reference cache
- `tests/TaskImporter.Core.Tests` — console runner, no NuGet dependencies
- `TaskImporterExtension/` — the packaged extension. Builds clean under
  `dotnet build` and registers locally. Three top-level commands: Add task,
  Capture clipboard, Current task. Settings page over `JsonSettingsManager`.
  One dock band (`Dock/TopTaskBand`): the running task, or the first open
  task on today's list, with time spent / estimate and a start-stop button.
  Polls the API every 30s and counts a running timer forward locally each
  second in between.

Note the extension landed in `TaskImporterExtension/` rather than
`src/TaskImporter.CmdPal` — that is where the Command Palette template put it,
and it carries its own `Directory.Build.props` and `Directory.Packages.props`.
The two libraries stay outside that folder, so they do not inherit it.


## Decisions already made — do not relitigate these

- **Entry is a quick-add line**, not a form. `Fix switch +Acme IT #onsite @friday 45m`
- **Default project is Inbox**, changeable in settings.
- **Capture clipboard opens the quick-add line pre-filled** rather than firing
  silently, so a tag can be added before it goes.
- **No global hotkey** for now; opening the palette first is good enough.
- **Link titles**: clipboard anchor text if present, otherwise fetch the page
  and read `<title>` / `og:title`, otherwise a tidied URL. The URL always goes
  on the first line of the notes.
- **Routing is deferred** but the seam is built. Do not add a rule engine
  without being asked; do keep `ITaskRouter` intact.
- **Shown as "Super Palette"**; the code keeps the name TaskImporter. Only
  display strings changed (manifest `DisplayName`/`Description`, the provider's
  `DisplayName`, the dock band title). Leave the provider `Id` and the
  `TaskImporter` settings folder name alone: changing them loses the pinned
  band or the saved token.
- **Package identity is the Partner Center one** for the "Super Palette" Store
  listing: `Name="irondan.SuperPalette"`,
  `Publisher="CN=B8ABD490-3553-417B-A912-72159E3023C4"`, display publisher
  `irondan`. It is set in both `Package.appxmanifest` and the csproj
  (`AppxPackageIdentityName`, `AppxPackagePublisher`) and must match Partner
  Center exactly, so never change it. The executable is still
  `TaskImporterExtension.exe`.
- **Settings live inside the package's data folder**, not plain
  `%LOCALAPPDATA%`: MSIX redirects it to
  `%LOCALAPPDATA%\Packages\irondan.SuperPalette_xf259y3a7ystp\LocalCache\Local\TaskImporter`.
  Re-registering keeps them; `Remove-AppxPackage` deletes them, token included.
  Changing the manifest makes plain re-registration fail with 0x80073CFB, so
  either remove the package (and re-enter the token) or bump `Version`.

## Conventions that matter

- **Explicit beats inferred.** Anything the user typed is recorded in
  `TaskDraft.ExplicitFields`. Routers fill gaps; they never overwrite a typed
  value. This is the contract that keeps future routing from being annoying.
- `Core` has **no Windows or Command Palette dependency** and must stay that
  way — it is what makes the two-second test loop possible. Windows-specific
  work (reading the clipboard, the foreground window) belongs in `CmdPal`.
- **No `Directory.Build.props` at the repo root.** The Command Palette template
  writes its own; each project here carries its own settings instead. Override
  `TaskImporterTfm` on the command line to build against another SDK.
- Tests are a plain console app, not xunit, so they need no restore. Add cases
  to `tests/TaskImporter.Core.Tests/Program.cs`. The "wire" suite drives the
  real client through `FakeApi`, a fake `HttpMessageHandler`.
- **JSON is source-generated.** The extension ships trimmed, which turns off
  reflection-based `System.Text.Json`. Any type the client reads or writes
  must be listed in `Models/ApiJsonContext.cs`. The test project runs with
  reflection off too, so a missing entry fails the test loop, and both
  libraries set `IsAotCompatible` so the analyzers warn about trim hazards.
- **`PRIVACY.md` is the Store listing's privacy policy** and states exactly
  what is stored, what is sent where, and when the clipboard is read. Any
  change to settings, network calls or clipboard use must update it in the
  same commit.
- Unresolvable `#tags` and `+projects` stay in the title and are reported.
  Tags **cannot** be created through the API, so silently dropping one would be
  the wrong failure.

## Toolchain

VS Code with the base **C#** extension. Deliberately **not** Visual Studio
(Community's licence excludes organisations over 250 PCs or $1M revenue) and
**not** C# Dev Kit (paid tier). Developer Mode is on, which is what allows
registering an unsigned local build.

Three things must be installed, and the third is easy to miss:

- **.NET 9 SDK** — the libraries and tests target `net9.0`.
- **.NET 10 SDK** — the extension targets `net10.0-windows10.0.26100.0`.
- **Windows SDK 10.0.26100** — *not optional*. `Microsoft.CommandPalette.Extensions`
  ships its API as a `.winmd`, so the build runs `cswinrt.exe` to project it,
  and that needs `Windows.winmd` from an installed Windows SDK. Without it the
  build fails with "Could not find the Windows SDK in the registry". No NuGet
  package substitutes for it, and winget has no 10.0.26100 package — use the
  installer from the Windows SDK archive. The version must match
  `TargetPlatformVersion` in the csproj. The only feature needed is
  **Windows SDK for UWP Managed Apps**; the **Windows App Certification Kit**
  is worth adding to run the Store's checks locally.

```powershell
# fast loop — about a second, no network, no Super Productivity
dotnet run --project tests\TaskImporter.Core.Tests

# the extension
dotnet build TaskImporterExtension\TaskImporterExtension\TaskImporterExtension.csproj -p:Platform=x64
Add-AppxPackage -Register .\TaskImporterExtension\TaskImporterExtension\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\AppxManifest.xml
# then restart Command Palette (not just Reload)
```

Re-registering while Command Palette is running can leave it holding the
extension twice — the Dock's band picker then lists "Top task" twice. Reload
only restarts extensions; quitting and relaunching Command Palette clears it.
Stop `TaskImporterExtension.exe` before building, or the copy step fails on
locked files.

`-p:Platform=x64` is required: the template's `Directory.Build.props` sets
`PlatformTarget` from `$(Platform)`, and the default `AnyCPU` is not one of the
platforms it declares.

## Settled

- **MSIX packaging works under `dotnet build`.** This was the main unproven
  assumption. No Visual Studio MSBuild is needed; `AppxManifest.xml` lands in
  the output folder and `Add-AppxPackage -Register` takes it.
- **The Store bundle builds without Visual Studio too.**
  `TaskImporterExtension\build-store.ps1` builds unsigned Release MSIX for x64
  and ARM64 and bundles them with `makeappx` into `AppPackages\`. It depends on
  the `Microsoft.Windows.SDK.BuildTools` package reference: without it the MSIX
  targets look for makepri via `Platforms\UAP\<ver>\UAP.props`, which the
  standalone SDK installer does not provide, and packaging fails with
  APPX3217. It also passes `AppxBundle=Never`: with scaled assets the tooling
  otherwise splits them into resource packages and bundles each architecture
  on its own, and bundles cannot be nested. The `mspdbcmf.exe` warning only
  means no symbols package; upload
  the `.msixbundle`.
- The extension project is not in the solution file. It builds by path.

## Next steps, in order

1. Verify against a live instance — see "Known unknowns" below.
2. Check the dock band in a live Dock: rendering, click behaviour, and
   whether `timeSpent` drifts against Super Productivity's own display.
3. Ship to the Microsoft Store, then list it in the Command Palette Extension
   Gallery (a PR to `microsoft/CmdPal-Extensions` pointing at the Store
   listing). WinGet waits; if added later it is the unpackaged Inno Setup EXE
   from Microsoft's docs (`LocalServer32`, not the template skill's
   `InprocServer32`), and never alongside the Store build on one machine.
   Before submitting:
   - Run the trimmed Release build against a live Super Productivity. Tests
     cover JSON with reflection off, but nothing has run the trimmed package.
   - A `runFullTrust` justification, and certification notes saying
     PowerToys with Command Palette is required.

## Known unknowns

- `dueWithTime` is sent as epoch milliseconds. The API docs name the field but
  not its encoding, and **this is still untested against a live instance**. If a
  timed due date is rejected or lands at the wrong moment, change it in
  `Models/ApiModels.cs` and `SuperProductivityClient.ToRequest`. Test it with
  `@tomorrow 9am` and check what Super Productivity shows.
- The palette is the foreground window by the time a capture runs, so
  `CaptureContext.SourceApp` is deliberately left null rather than filled with
  a useless "WindowsPackageManager". A future routing rule that wants the real
  source app will need the foreground window sampled before the palette opens.
- Page-title fetches may go through a corporate firewall; some hosts will be
  blocked and fall back to the tidied URL. That is expected, not a bug.

## Reference

`README.md` has the full quick-add syntax table and the repo layout. The
original build plan lives as a document in Danny's Claude account — ask him for
it rather than guessing at anything above.
