# Super Palette privacy policy

Effective 29 October 2026.

Super Palette is a Command Palette extension that adds tasks to
[Super Productivity](https://super-productivity.com/) on your own computer. It
has no account, no server and no analytics. The developer receives nothing
from it.

## What it stores

Super Palette keeps four settings in a file on your computer, inside the
extension's own app data folder:

- your Super Productivity access token
- the address of Super Productivity's local API (by default `127.0.0.1`, which
  is your own machine)
- your default project
- whether to look up page titles for links

The file is readable by your Windows user account. It is not encrypted, so
treat it as you would the token itself. Uninstalling Super Palette deletes it.

Super Palette keeps nothing else. It does not keep a history of what you add.

## What it sends, and where

**To Super Productivity.** When you add, start, stop or complete a task, or
open the current-task view, Super Palette talks to Super Productivity's local
API at the address in its settings. While the dock band is pinned, it also
checks the current task there every 30 seconds. By default that
address is your own computer, and nothing leaves it. If you change the address
to another machine, that traffic goes there instead.

**To the web, only for link titles.** When you add a task containing a web
link that has no title of its own, Super Palette fetches the start of that page
(at most 64 KB) to read its title. That request goes directly from your
computer to the site you linked, which sees an ordinary web request from your
IP address, as it would if you opened the link. No cookies from your browser
are sent. This happens only when you press Enter to add the task, never while
you type. You can turn it off with **Look up page titles** in the extension's
settings.

Nothing is sent to the developer or to any other third party.

## The clipboard

Super Palette reads the clipboard only when you open **Capture clipboard**,
to fill the task line with what you copied. It does not watch the clipboard
in the background and does not keep what it reads.

## Other software

Super Productivity, PowerToys and the Microsoft Store each have their own
privacy policies, which cover what they do with your data. Super Palette does
not change them.

## Changes

If this policy changes, the new version will be published at this address,
with a new effective date, and the change will be visible in the repository's
history.

## Contact

Questions or concerns: open an issue at
<https://github.com/Irondan278/SuperPalette/issues>.
