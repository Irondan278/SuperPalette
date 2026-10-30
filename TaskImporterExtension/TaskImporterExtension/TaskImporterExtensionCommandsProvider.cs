// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using TaskImporterExtension.Commands;
using TaskImporterExtension.Dock;
using TaskImporterExtension.Pages;
using TaskImporterExtension.Services;

namespace TaskImporterExtension;

public partial class TaskImporterExtensionCommandsProvider : CommandProvider
{
    private readonly TaskImporterSettings _settings = new();
    private readonly TaskImporterHost _host;
    private readonly AddTaskPage _addTaskPage;
    private readonly AddTaskPage _capturePage;
    private readonly CurrentTaskPage _currentTaskPage;
    private readonly TopTaskBand _topTaskBand;
    private readonly ICommandItem[] _commands;

    public TaskImporterExtensionCommandsProvider()
    {
        Id = "TaskImporter";
        DisplayName = "Super Palette";
        Icon = IconHelpers.FromRelativePath("Assets\\ExtensionIcon.png");

        // Exposing this is what gives the extension its settings page.
        Settings = _settings.Settings;

        _host = new TaskImporterHost(_settings);

        // Two instances of the same page rather than one page plus a command
        // that navigates to it. A top-level item whose command *is* a page is
        // the navigation the host performs reliably; asking it to go to a page
        // by id from inside an Invoke simply did not move the palette.
        _addTaskPage = new AddTaskPage(_host);
        _capturePage = new AddTaskPage(_host, seedsFromClipboard: true);
        _currentTaskPage = new CurrentTaskPage(_host);
        _topTaskBand = new TopTaskBand(_host, _currentTaskPage);

        _commands =
        [
            new CommandItem(_addTaskPage)
            {
                Title = "Add task",
                Subtitle = "Capture a task into Super Productivity",
            },
            new CommandItem(_capturePage)
            {
                Title = "Capture clipboard",
                Subtitle = "The quick-add line, filled in from the clipboard",
            },
            new CommandItem(_currentTaskPage)
            {
                Title = "Current task",
                Subtitle = "Start, stop or complete what the timer is on",
            },
        ];
    }

    public override ICommandItem[] TopLevelCommands() => _commands;

    public override ICommandItem[]? GetDockBands() => [_topTaskBand.Band];

    public override void Dispose()
    {
        _topTaskBand.Dispose();
        _host.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
