# VS Code Agents window Logi plugin

A Logitech MX Creative Keypad plugin that shows VS Code Agents window sessions and their state on the keys, and opens a session with a press.

Status: 0.1.0, first working version. macOS only.

## Actions

Drag these onto keys in Logi Options+ (group **VS Code Agents Window**):

| Action | What it does |
|---|---|
| **Recent session 1…9** | The Nth non-archived session: waiting for input first, then error, then working, then most recently modified. Press opens it in the Agents window. |
| **New session** | Opens the Agents window on the new-session composer. |
| **Agents window** (folder) | Opens a folder with **New** and one key per session, after the back key the Plugin Service always draws top left. Pressing a session with one chat opens it; with several chats it shows them, with a **Back** key to the sessions and a key that opens the session itself. A long press on a session opens it directly (experimental). |

Key colors: red waiting for input, purple error, amber working, green outline idle, grey dotted empty, dark red dotted VS Code not reachable.

## How it works

- **State:** VS Code's local Agent Host publishes an [Agent Host Protocol](https://microsoft.github.io/agent-host-protocol/) endpoint for other local processes. The plugin finds it through `~/Library/Application Support/Code/agent-host/local-endpoint/entries/*.json`, connects over the Unix socket with the entry's token, and polls `listSessions` once a second. The token is never logged.
- **Focus:** a press opens `vscode://agents/agent-host-session/<provider>/<sessionId>[/chat/<chatId>]`, which VS Code routes to the Agents window. New session uses `vscode://agents/new?prompt=`.
- **Client:** the official .NET client, [`Microsoft.VisualStudioCode.AgentHostProtocol`](https://www.nuget.org/packages/Microsoft.VisualStudioCode.AgentHostProtocol).

Research notes and decisions live in [robertohuertasm/agent-skills, `labs/projects/agent-monitoring`](https://github.com/robertohuertasm/agent-skills/tree/main/labs/projects/agent-monitoring). The plugin layout follows [niklasberglund/herdr-logi-plugin](https://github.com/niklasberglund/herdr-logi-plugin).

## Layout

- `src/AgentsWindow.Core`: everything that does not need the Logi API (endpoint discovery, AHP client, parsing, ranking, links). Tested directly.
- `src/AgentsWindowPlugin`: the Logi plugin (actions, folder, key faces).
- `tests/AgentsWindow.Core.Tests`: xUnit tests for the core.
- `package/metadata/LoupedeckPackage.yaml`: plugin manifest and the only version number.
- `probes/`: throwaway research probes.

## Development

Requires the .NET 10 SDK, Logi Options+ with Logi Plugin Service 6.4 or later, and VS Code 1.144 or later.

```sh
dotnet test tests/AgentsWindow.Core.Tests
dotnet build src/AgentsWindowPlugin       # Debug: links build/Debug into the Plugin Service and reloads it
dotnet clean src/AgentsWindowPlugin       # removes the link
```

The log is at `~/Library/Application Support/Logi/LogiPluginService/Logs/plugin_logs/AgentsWindow.log`. A warning that the service "Cannot create dynamic action" for `RecentSessionCommand` is expected: the plugin adds those actions itself.

## Probes

- [`probes/AhpProbe`](probes/AhpProbe): connects to the live Agent Host endpoint read-only and prints aggregate counts by provider and status.
- [`probes/AhpLoadProbe`](probes/AhpLoadProbe): a throwaway Logi plugin with no actions that proves the client works inside Logi Plugin Service.

```sh
dotnet run --project probes/AhpProbe
dotnet build probes/AhpLoadProbe -p:Link=true
rm ~/Library/Application\ Support/Logi/LogiPluginService/Plugins/AhpLoadProbe.link
```
