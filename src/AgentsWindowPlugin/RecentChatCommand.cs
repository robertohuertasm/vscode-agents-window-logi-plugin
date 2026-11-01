namespace Loupedeck.AgentsWindowPlugin;

using AgentsWindow;

// "Recent chat N": the Nth chat across all sessions by priority, dragged onto a key in
// Options+. The name stays agentswindow_recent_N, as in 0.1.0 when keys were sessions,
// because profiles refer to keys by it.
//
// A short press opens the chat. A long press asks "Archive?" and a short press within a
// few seconds confirms, as in the folder. The main chat archives its whole session. While
// waiting, the key stays on the chat it is about to archive even if the ranking changes.
public sealed class RecentChatCommand : PluginDynamicCommand
{
    public const int SlotCount = 9;
    private static readonly TimeSpan ConfirmWindow = TimeSpan.FromSeconds(3);

    private readonly AgentsWindowPlugin owner;
    private readonly int slot;
    private bool longPressHandled;
    private RankedChat? armed;
    private CancellationTokenSource? armTimer;

    public RecentChatCommand(AgentsWindowPlugin owner, int slot)
        : base($"Recent chat {slot}", $"Status of the {Ordinal(slot)} chat in the Agents window, active ones first; press to open it, hold to archive it", "Recent chats", DeviceType.All)
    {
        this.owner = owner;
        this.slot = slot;
        this.Name = $"agentswindow_recent_{slot}";
    }

    private SessionBoard Board => this.owner.Board;

    private RankedChat? Current => this.armed ?? this.Board.ChatAt(this.slot - 1);

    public void Redraw()
    {
        try
        {
            this.ActionImageChanged();
        }
        catch (Exception e)
        {
            this.Log.Error(e, $"could not redraw '{this.Name}'");
        }
    }

    protected override bool ProcessButtonEvent2(string actionParameter, DeviceButtonEvent2 buttonEvent)
    {
        switch (buttonEvent.EventType)
        {
            case DeviceButtonEventType.Press:
                this.longPressHandled = false;
                return true;
            case DeviceButtonEventType.LongPress:
                this.longPressHandled = true;
                if (this.armed is null && this.Board.ChatAt(this.slot - 1) is { } held)
                {
                    if (held.IsMain || held.Chat.CanArchive)
                    {
                        this.Arm(held);
                    }
                    else
                    {
                        // VS Code rejects archiving side chats on their own.
                        this.Log.Info("long press on a chat VS Code cannot archive on its own; opening it");
                        this.Open(held);
                    }
                }
                return true;
            case DeviceButtonEventType.RepeatPress:
                return true;
            case DeviceButtonEventType.Release:
                if (!this.longPressHandled)
                {
                    this.Tap();
                }
                this.longPressHandled = false;
                return true;
            default:
                return false;
        }
    }

    protected override void RunCommand(string actionParameter) => this.Tap();

    protected override BitmapImage GetCommandImage(string actionParameter, PluginImageSize imageSize)
    {
        if (this.armed is not null) return Faces.Glyph("?", imageSize, new BitmapColor(200, 30, 30));
        if (!this.Board.Online) return Faces.State(KeyState.Offline, imageSize);
        return Faces.State(this.Current?.Chat.State ?? KeyState.None, imageSize);
    }

    protected override string GetCommandDisplayName(string actionParameter, PluginImageSize imageSize)
    {
        if (this.armed is not null) return "Archive?";
        return KeyFace.Label(this.Board.Online ? this.Current?.Title : "offline");
    }

    private void Tap()
    {
        if (this.armed is { } target)
        {
            this.Disarm();
            _ = this.ArchiveAsync(target);
        }
        else if (this.Board.ChatAt(this.slot - 1) is { } chat)
        {
            this.Open(chat);
        }
    }

    private void Open(RankedChat chat) => Opener.Open(Links.OpenSession(chat.Session.Resource, chat.Chat.ChatId));

    // The main chat stands for its session, as in the folder.
    private async Task ArchiveAsync(RankedChat target)
    {
        try
        {
            if (target.IsMain)
            {
                await this.owner.Actions.SetSessionArchivedAsync(target.Session.Resource, true, CancellationToken.None);
                this.Log.Info("archived a session from a Recent chat key");
            }
            else
            {
                await this.owner.Actions.SetChatArchivedAsync(target.Chat.Resource, true, CancellationToken.None);
                this.Log.Info("archived a chat from a Recent chat key");
            }
        }
        catch (Exception e)
        {
            this.Log.Error($"could not archive from a Recent chat key: {e.Message}");
        }
    }

    private void Arm(RankedChat target)
    {
        this.armTimer?.Cancel();
        this.armed = target;
        var timer = this.armTimer = new CancellationTokenSource();
        this.Redraw();
        _ = Task.Delay(ConfirmWindow, timer.Token).ContinueWith(
            _ => { if (ReferenceEquals(this.armed, target)) this.Disarm(); },
            CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    private void Disarm()
    {
        this.armTimer?.Cancel();
        this.armTimer = null;
        if (this.armed is null) return;
        this.armed = null;
        this.Redraw();
    }

    private static string Ordinal(int n) => n switch
    {
        1 => "1st",
        2 => "2nd",
        3 => "3rd",
        _ => $"{n}th",
    };
}
