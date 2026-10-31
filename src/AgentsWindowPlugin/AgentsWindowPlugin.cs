namespace Loupedeck.AgentsWindowPlugin;

using AgentsWindow;

public class AgentsWindowPlugin : Plugin
{
    private readonly CancellationTokenSource stop = new();
    private readonly List<RecentChatCommand> recent = [];
    private NewSessionCommand? newSession;
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
        for (var slot = 1; slot <= RecentChatCommand.SlotCount; slot++)
        {
            var command = new RecentChatCommand(this, slot);
            this.recent.Add(command);
            this.AddDynamicAction(command);
        }
        this.newSession = new NewSessionCommand(this.Board);
        this.AddDynamicAction(this.newSession);
        this.Board.Changed += this.RedrawRecent;
        _ = Task.Run(() => this.Board.RunAsync(this.stop.Token));
        AgentsWindow.Log.Info($"{this.recent.Count} recent-chat keys registered");
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
        this.newSession?.Redraw();
    }
}
