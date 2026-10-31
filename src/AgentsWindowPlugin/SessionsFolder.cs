namespace Loupedeck.AgentsWindowPlugin;

using AgentsWindow;

// A Dynamic Folder with two levels, like Logitech's Home Assistant plugin.
//
// Sessions level: the host's own back key (top left, closes the folder), New, then one
// key per session. A short press opens the session; a long press opens its menu.
//
// Session menu: Back (to sessions), New chat, Archive, then the session's chats. A press
// on a chat opens it; a long press archives it, except for side chats and the main chat,
// which VS Code only archives with their session. Archive actions ask for a second press
// within a few seconds, because the Keypad has no undo and the key vanishes at once.
public sealed class SessionsFolder : PluginDynamicFolder
{
    private const string Back = "back";
    private const string New = "new";
    private const string NewChat = "newchat";
    private const string Archive = "archive";
    private const string SessionPrefix = "s:";
    private const string ChatPrefix = "c:";
    private static readonly TimeSpan ConfirmWindow = TimeSpan.FromSeconds(3);

    // Folder key names carry what the key shows (state, chat count, a short hash of the
    // title), so any visible change renames the key and the host fetches a fresh face and
    // label instead of reusing cached ones. A new chat's title arrives after its first message.
    private const char StateSeparator = '|';

    // How a profile addresses this folder's own key; PluginDynamicFolder has no ButtonImageChanged.
    private const string FolderActionName = "#DynamicFolder";

    private string? openSession;
    private bool longPressHandled;
    private string[] shownNames = [];

    // The key waiting for a confirming press: Archive, or a chat resource after a long press.
    // Armed keys get ArmedMarker in their name, because the host only reliably redraws a
    // key when its name changes.
    private const char ArmedMarker = '!';
    private string? armed;
    private CancellationTokenSource? armTimer;

    public SessionsFolder()
    {
        this.DisplayName = "Agents window";
        this.Description = "Browse VS Code Agents window sessions and their chats";
        this.GroupName = "Agents window";
    }

    private AgentsWindowPlugin Owner => (AgentsWindowPlugin)this.Plugin;

    private SessionBoard Board => this.Owner.Board;

    public override bool Load()
    {
        this.Board.Changed += this.OnBoardChanged;
        return true;
    }

    public override bool Unload()
    {
        this.Board.Changed -= this.OnBoardChanged;
        return true;
    }

    // The host's own back key closes the folder without telling RunCommand, so the
    // level is reset whenever the folder opens or closes. Otherwise it reopens on the
    // menu of the last session.
    public override bool Activate()
    {
        this.ResetLevel();
        return true;
    }

    public override bool Deactivate()
    {
        this.ResetLevel();
        return true;
    }

    public override PluginDynamicFolderNavigation GetNavigationArea(DeviceType deviceType) => PluginDynamicFolderNavigation.None;

    public override string GetButtonDisplayName(PluginImageSize imageSize) => "Agents";

    public override BitmapImage GetButtonImage(PluginImageSize imageSize)
    {
        var top = this.Board.Ranked.FirstOrDefault()?.State ?? KeyState.None;
        return Faces.Ring(this.Board.Online ? top : KeyState.Offline, imageSize);
    }

    public override IEnumerable<string> GetButtonPressActionNames(DeviceType deviceType)
    {
        if (this.openSession is { } resource && this.Board.Find(resource) is { } session)
        {
            yield return this.CreateCommandName(Back);
            yield return this.CreateCommandName(NewChat);
            yield return this.CreateCommandName(this.armed == Archive ? Archive + ArmedMarker : Archive);
            foreach (var chat in session.Chats)
            {
                var marker = this.armed == chat.Resource ? ArmedMarker.ToString() : "";
                yield return this.CreateCommandName($"{ChatPrefix}{chat.State}{TitleTag(chat.Title)}{marker}{StateSeparator}{chat.Resource}");
            }
            yield break;
        }

        yield return this.CreateCommandName(New);
        foreach (var s in this.Board.Ranked)
        {
            yield return this.CreateCommandName($"{SessionPrefix}{s.State}{s.Chats.Count}{TitleTag(s.Title)}{StateSeparator}{s.Resource}");
        }
    }

    public override string GetCommandDisplayName(string actionParameter, PluginImageSize imageSize)
    {
        if (this.IsArmed(actionParameter)) return "Archive?";
        if (actionParameter == Back) return "Back";
        if (actionParameter == New) return "New";
        if (actionParameter == NewChat) return "New chat";
        if (actionParameter.StartsWith(Archive, StringComparison.Ordinal)) return "Archive";
        if (actionParameter.StartsWith(SessionPrefix, StringComparison.Ordinal))
        {
            return KeyFace.Label(this.Board.Find(Resource(actionParameter, SessionPrefix))?.Title);
        }
        if (actionParameter.StartsWith(ChatPrefix, StringComparison.Ordinal))
        {
            return KeyFace.Label(this.FindChat(Resource(actionParameter, ChatPrefix))?.Chat.Title);
        }
        return KeyFace.Blank;
    }

    public override BitmapImage GetCommandImage(string actionParameter, PluginImageSize imageSize)
    {
        if (this.IsArmed(actionParameter)) return Faces.Glyph("?", imageSize, new BitmapColor(200, 30, 30));
        if (actionParameter == Back) return Faces.Glyph("‹", imageSize);
        if (actionParameter == New || actionParameter == NewChat) return Faces.Glyph("+", imageSize);
        if (actionParameter.StartsWith(Archive, StringComparison.Ordinal)) return Faces.Glyph("✓", imageSize);
        if (actionParameter.StartsWith(SessionPrefix, StringComparison.Ordinal))
        {
            var session = this.Board.Find(Resource(actionParameter, SessionPrefix));
            return Faces.Ring(session?.State ?? KeyState.None, imageSize, session?.Chats.Count ?? 1);
        }
        if (actionParameter.StartsWith(ChatPrefix, StringComparison.Ordinal))
        {
            return Faces.Ring(this.FindChat(Resource(actionParameter, ChatPrefix))?.Chat.State ?? KeyState.None, imageSize);
        }
        return Faces.Ring(KeyState.None, imageSize);
    }

    // Session and chat keys handle their own events so a hold can do something other
    // than a tap: a short press acts on Release, a long press on LongPress.
    // Other keys keep the default handling, which calls RunCommand.
    public override bool ProcessButtonEvent2(string actionParameter, DeviceButtonEvent2 buttonEvent)
    {
        var isSession = actionParameter.StartsWith(SessionPrefix, StringComparison.Ordinal);
        var isChat = actionParameter.StartsWith(ChatPrefix, StringComparison.Ordinal);
        if (!isSession && !isChat)
        {
            return base.ProcessButtonEvent2(actionParameter, buttonEvent);
        }

        switch (buttonEvent.EventType)
        {
            case DeviceButtonEventType.Press:
                this.longPressHandled = false;
                return true;
            case DeviceButtonEventType.LongPress:
                this.longPressHandled = true;
                if (isSession)
                {
                    this.OpenMenu(Resource(actionParameter, SessionPrefix));
                }
                else if (this.FindChat(Resource(actionParameter, ChatPrefix)) is { } held)
                {
                    if (held.Chat.CanArchive)
                    {
                        this.Arm(held.Chat.Resource);
                    }
                    else
                    {
                        // VS Code rejects archiving side chats and the main chat on their own.
                        this.Plugin.Log.Info("long press on a chat VS Code cannot archive on its own; opening it");
                        Opener.Open(Links.OpenSession(held.Session.Resource, held.Chat.ChatId));
                    }
                }
                return true;
            case DeviceButtonEventType.RepeatPress:
                return true;
            case DeviceButtonEventType.Release:
                if (!this.longPressHandled)
                {
                    this.Tap(actionParameter);
                }
                this.longPressHandled = false;
                return true;
            default:
                return false;
        }
    }

    public override void RunCommand(string actionParameter) => this.Tap(actionParameter);

    private void Tap(string actionParameter)
    {
        if (this.armed is not null && !this.IsArmed(actionParameter))
        {
            this.Disarm();
        }

        if (actionParameter == Back)
        {
            this.ResetLevel(force: true);
        }
        else if (actionParameter == New)
        {
            Opener.Open(Links.NewSession(" "));
        }
        else if (actionParameter == NewChat && this.openSession is { } session)
        {
            _ = this.CreateChatAsync(session);
        }
        else if (actionParameter.StartsWith(Archive, StringComparison.Ordinal) && this.openSession is { } toArchive)
        {
            if (this.armed == Archive)
            {
                this.Disarm();
                _ = this.ArchiveSessionAsync(toArchive);
            }
            else
            {
                this.Arm(Archive);
            }
        }
        else if (actionParameter.StartsWith(SessionPrefix, StringComparison.Ordinal))
        {
            Opener.Open(Links.OpenSession(Resource(actionParameter, SessionPrefix)));
        }
        else if (actionParameter.StartsWith(ChatPrefix, StringComparison.Ordinal) && this.FindChat(Resource(actionParameter, ChatPrefix)) is { } found)
        {
            if (this.armed == found.Chat.Resource)
            {
                this.Disarm();
                _ = this.ArchiveChatAsync(found.Session, found.Chat);
            }
            else
            {
                Opener.Open(Links.OpenSession(found.Session.Resource, found.Chat.ChatId));
            }
        }
    }

    private void OpenMenu(string resource)
    {
        this.Disarm();
        this.openSession = resource;
        this.shownNames = [];
        this.ButtonActionNamesChanged();
    }

    private async Task CreateChatAsync(string sessionResource)
    {
        try
        {
            var session = this.Board.Find(sessionResource);
            var chat = await this.Owner.Actions.CreateChatAsync(sessionResource, session?.DefaultChat, CancellationToken.None);
            this.Plugin.Log.Info("created a chat from the Keypad");
            Opener.Open(Links.OpenSession(sessionResource, Links.ChatId(chat, session?.DefaultChat)));
        }
        catch (Exception e)
        {
            this.Plugin.Log.Error($"could not create a chat: {e.Message}");
        }
    }

    // Archiving the session's default chat archives the session, as the protocol advises.
    private async Task ArchiveChatAsync(Session session, Chat chat)
    {
        if (chat.ChatId is null)
        {
            await this.ArchiveSessionAsync(session.Resource);
            return;
        }

        try
        {
            await this.Owner.Actions.SetChatArchivedAsync(chat.Resource, true, CancellationToken.None);
            this.Plugin.Log.Info("asked VS Code to archive a chat");
        }
        catch (Exception e)
        {
            this.Plugin.Log.Error($"could not archive a chat: {e.Message}");
        }
    }

    private async Task ArchiveSessionAsync(string sessionResource)
    {
        try
        {
            await this.Owner.Actions.SetSessionArchivedAsync(sessionResource, true, CancellationToken.None);
            this.Plugin.Log.Info("archived a session from the Keypad");
            this.ResetLevel(force: true);
        }
        catch (Exception e)
        {
            this.Plugin.Log.Error($"could not archive a session: {e.Message}");
        }
    }

    private void Arm(string id)
    {
        this.armTimer?.Cancel();
        this.armed = id;
        var timer = this.armTimer = new CancellationTokenSource();
        this.RefreshNames();
        _ = Task.Delay(ConfirmWindow, timer.Token).ContinueWith(
            _ => { if (this.armed == id) this.Disarm(); },
            CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    private void Disarm()
    {
        this.armTimer?.Cancel();
        this.armTimer = null;
        if (this.armed is null) return;
        this.armed = null;
        this.RefreshNames();
    }

    private bool IsArmed(string actionParameter) =>
        this.armed is { } id && actionParameter.Contains(ArmedMarker) &&
        (id == Archive ? actionParameter.StartsWith(Archive, StringComparison.Ordinal) : Resource(actionParameter, ChatPrefix) == id);

    private void RefreshNames()
    {
        try
        {
            this.shownNames = [];
            this.ButtonActionNamesChanged();
        }
        catch (Exception e)
        {
            this.Plugin.Log.Error(e, "could not refresh the Agents window folder");
        }
    }

    // Stable across runs, unlike string.GetHashCode. Hex only, so it never contains the
    // separator or the armed marker.
    private static string TitleTag(string title)
    {
        var hash = 2166136261u;
        foreach (var c in title)
        {
            hash = (hash ^ c) * 16777619u;
        }
        return "t" + hash.ToString("x8");
    }

    private static string Resource(string actionParameter, string prefix)
    {
        var rest = actionParameter[prefix.Length..];
        var separator = rest.IndexOf(StateSeparator);
        return separator < 0 ? rest : rest[(separator + 1)..];
    }

    private void ResetLevel(bool force = false)
    {
        this.Disarm();
        if (this.openSession is null && !force) return;
        this.openSession = null;
        this.shownNames = [];
        try
        {
            this.ButtonActionNamesChanged();
        }
        catch (Exception e)
        {
            this.Plugin.Log.Error(e, "could not reset the Agents window folder");
        }
    }

    private (Session Session, Chat Chat)? FindChat(string chatResource)
    {
        foreach (var s in this.Board.Ranked)
        {
            if (s.Chats.FirstOrDefault(c => c.Resource == chatResource) is { } chat) return (s, chat);
        }
        return null;
    }

    private void OnBoardChanged()
    {
        // The host caches the folder key's face and only re-reads it when asked.
        try
        {
            this.Plugin.OnActionImageChanged(FolderActionName, this.Name, true);
        }
        catch (Exception e)
        {
            this.Plugin.Log.Error(e, "could not redraw the Agents window folder key");
        }

        // A session archived elsewhere closes its menu.
        if (this.openSession is { } open && this.Board.Find(open) is null)
        {
            this.ResetLevel();
            return;
        }

        // ButtonActionNamesChanged alone does not redraw keys whose names stay the same, so
        // each visible key is invalidated too.
        try
        {
            var names = this.GetButtonPressActionNames(DeviceType.All).ToArray();
            if (!names.SequenceEqual(this.shownNames))
            {
                this.shownNames = names;
                this.ButtonActionNamesChanged();
            }
            foreach (var name in names)
            {
                this.CommandImageChanged(name);
            }
        }
        catch (Exception e)
        {
            this.Plugin.Log.Error(e, "could not refresh the Agents window folder");
        }
    }
}
