# VS Code Agents window Logi plugin

A Logitech MX Creative Keypad plugin that shows VS Code Agents window sessions and their state on the keys, and opens a session with a press.

Status: 0.1.0, first working version. Tested on macOS. Windows support is written but untested.

## Actions

Drag these onto keys in Logi Options+ (group **VS Code Agents Window**):

| Action | What it does |
|---|---|
| **Recent chat 1…9** | The Nth chat across all non-archived sessions, so a session with several active chats takes several keys: waiting for input first, then error, then working, then idle, in session order. Press opens that chat. A long press asks **Archive?** and a press within 3 seconds archives it; the main chat archives its whole session. Keys placed as "Recent session" in 0.1.0 keep working. |
| **New session** | Opens the Agents window on the new-session composer. While VS Code is not running the key reads **Start VS Code** (▶); the same press starts it. |
| **Agents window** (folder) | Opens a folder with **New** and one key per session, after the back key the Plugin Service always draws top left. A press opens a session. A long press opens the session's menu: **Back**, **New chat** (creates a chat in that session and opens it), **Archive** (the whole session), and one key per chat. In the menu, a press opens a chat and a long press archives it. VS Code refuses to archive side chats (started from another chat) and the main chat on their own, so a long press just opens those. Archiving asks for a second press within 3 seconds (the key turns red with "Archive?"). Sessions with several chats show a white badge with the count. |

Key colors: red waiting for input, purple error, amber working, blue finished but not looked at yet (VS Code's blue dot), green outline idle, grey dotted empty, dark red dotted VS Code not reachable. A session key shows its most urgent chat, so a side chat waiting or unread colors the whole session.

## How it works

- **State:** VS Code's local Agent Host publishes an [Agent Host Protocol](https://microsoft.github.io/agent-host-protocol/) endpoint for other local processes. The plugin finds it through `agent-host/local-endpoint/entries/*.json` in VS Code's user data directory (`~/Library/Application Support/Code` on macOS, `%APPDATA%\Code` on Windows), connects over the Unix socket (macOS) or named pipe (Windows) with the entry's token, and polls `listSessions` once a second. The token is never logged.
- **Actions:** archiving dispatches `session/isArchivedChanged` or `chat/isArchivedChanged`, and New chat sends `createChat`, each after subscribing to the target channel (the host only acts on loaded channels).
- **Focus:** a press opens `vscode://agents/agent-host-session/<provider>/<sessionId>[/chat/<chatId>]`, which VS Code routes to the Agents window. New session uses `vscode://agents/new?prompt=`.
- **Client:** the official .NET client, [`Microsoft.VisualStudioCode.AgentHostProtocol`](https://www.nuget.org/packages/Microsoft.VisualStudioCode.AgentHostProtocol).

Research notes and decisions live in [robertohuertasm/agent-skills, `labs/projects/agent-monitoring`](https://github.com/robertohuertasm/agent-skills/tree/main/labs/projects/agent-monitoring). The plugin layout follows [niklasberglund/herdr-logi-plugin](https://github.com/niklasberglund/herdr-logi-plugin).

## Layout

- `src/AgentsWindow.Core`: everything that does not need the Logi API (endpoint discovery, AHP client, parsing, ranking, links). Tested directly.
- `src/AgentsWindowPlugin`: the Logi plugin (actions, folder, key faces).
- `tests/AgentsWindow.Core.Tests`: xUnit tests for the core.
- `package/metadata/LoupedeckPackage.yaml`: plugin manifest and the only version number.
- `probes/`: throwaway research probes.

## Windows (untested)

The code handles Windows paths, VS Code's named pipe endpoint, and opening links through the shell, but nobody has run it there yet. To try it: install the .NET 10 SDK, Logi Options+, and VS Code 1.144 or later, then run `dotnet build src/AgentsWindowPlugin` and check the log. Please report what works.

## Development

Requires the .NET 10 SDK, Logi Options+ with Logi Plugin Service 6.4 or later, and VS Code 1.144 or later.

```sh
dotnet test tests/AgentsWindow.Core.Tests
dotnet build src/AgentsWindowPlugin       # Debug: links build/Debug into the Plugin Service and reloads it
dotnet clean src/AgentsWindowPlugin       # removes the link
```

The log is at `~/Library/Application Support/Logi/LogiPluginService/Logs/plugin_logs/AgentsWindow.log` on macOS and `%LOCALAPPDATA%\Logi\LogiPluginService\Logs\plugin_logs\AgentsWindow.log` on Windows. A warning that the service "Cannot create dynamic action" for `RecentChatCommand` is expected: the plugin adds those actions itself.

## Probes

- [`probes/AhpProbe`](probes/AhpProbe): connects to the live Agent Host endpoint read-only and prints aggregate counts by provider and status.
- [`probes/AhpLoadProbe`](probes/AhpLoadProbe): a throwaway Logi plugin with no actions that proves the client works inside Logi Plugin Service.

```sh
dotnet run --project probes/AhpProbe
dotnet build probes/AhpLoadProbe -p:Link=true
rm ~/Library/Application\ Support/Logi/LogiPluginService/Plugins/AhpLoadProbe.link
```
