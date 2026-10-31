# VS Code Agents window Logi plugin

A Logitech MX Creative Keypad plugin that shows VS Code Agents window sessions and their state on the keys, and focuses a session with a press.

Status: research. There is no plugin yet, only probes.

## How it works (planned)

- **State:** VS Code's local Agent Host publishes an [Agent Host Protocol](https://microsoft.github.io/agent-host-protocol/) endpoint for other local processes. The plugin finds it through `~/Library/Application Support/Code/agent-host/local-endpoint/entries/*.json`, connects over the Unix socket with the entry's token, and calls `listSessions`.
- **Focus:** a press opens `vscode://agents/agent-host-session/<provider>/<sessionId>`, which reveals the session in the Agents window.
- **Client:** the official .NET client, [`Microsoft.VisualStudioCode.AgentHostProtocol`](https://www.nuget.org/packages/Microsoft.VisualStudioCode.AgentHostProtocol).

Research notes and decisions live in [robertohuertasm/agent-skills, `labs/projects/agent-monitoring`](https://github.com/robertohuertasm/agent-skills/tree/main/labs/projects/agent-monitoring).

## Probes

- [`probes/AhpProbe`](probes/AhpProbe): connects to the live Agent Host endpoint read-only and prints aggregate counts by provider and status. It never prints titles, IDs, paths, or the connection token.

```sh
dotnet run --project probes/AhpProbe
```

Requires the .NET 9 SDK and a running VS Code (1.144 or later).
