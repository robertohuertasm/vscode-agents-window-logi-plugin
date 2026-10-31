namespace Loupedeck.AgentsWindowPlugin;

using AgentsWindow;

public class AgentsWindowPlugin : Plugin
{
    private readonly CancellationTokenSource stop = new();
    private readonly List<RecentSessionCommand> recent = [];
    private AgentHostClient? client;

    public AgentsWindowPlugin()
    {
        AgentsWindow.Log.Info = text => this.Log.Info(text);
        AgentsWindow.Log.Warning = text => this.Log.Warning(text);
        AgentsWindow.Log.Error = text => this.Log.Error(text);
        this.client = new AgentHostClient(EndpointRegistry.DefaultEntriesDirectory());
        this.Board = new SessionBoard(this.client);
    }

    public override bool UsesApplicationApiOnly => true;

    public override bool HasNoApplication => true;

    public SessionBoard Board { get; }

    public ISessionActions Actions => this.client ?? throw new InvalidOperationException("plugin unloaded");

    public override void Load()
    {
        for (var slot = 1; slot <= RecentSessionCommand.SlotCount; slot++)
        {
            var command = new RecentSessionCommand(this.Board, slot);
            this.recent.Add(command);
            this.AddDynamicAction(command);
        }
        this.AddDynamicAction(new NewSessionCommand());
        this.Board.Changed += this.RedrawRecent;
        _ = Task.Run(() => this.Board.RunAsync(this.stop.Token));
        AgentsWindow.Log.Info($"{this.recent.Count} recent-session keys registered");
    }

    public override void Unload()
    {
        this.stop.Cancel();
        this.Board.Changed -= this.RedrawRecent;
        _ = this.client?.DisposeAsync().AsTask();
        this.client = null;
    }

    private void RedrawRecent()
    {
        foreach (var command in this.recent)
        {
            command.Redraw();
        }
    }
}
