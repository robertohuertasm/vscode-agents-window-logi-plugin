namespace Loupedeck.AgentsWindowPlugin;

using AgentsWindow;

// A Dynamic Folder with two levels, like Logitech's Home Assistant plugin:
// sessions, then the chats of one session. Back goes up a level or closes the folder.
// Pressing a session with one chat opens it; with several chats it enters the chat level.
// A long press on a session always opens it (experimental: the Keypad may not send LongPress).
public sealed class SessionsFolder : PluginDynamicFolder
{
    private const string Back = "back";
    private const string New = "new";
    private const string SessionPrefix = "s:";
    private const string ChatPrefix = "c:";
    private const string OpenSessionPrefix = "o:";

    private string? openSession;
    private bool longPressHandled;

    public SessionsFolder()
    {
        this.DisplayName = "Agents window";
        this.Description = "Browse VS Code Agents window sessions and their chats";
        this.GroupName = "Agents window";
    }

    private SessionBoard Board => ((AgentsWindowPlugin)this.Plugin).Board;

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

    public override bool Activate()
    {
        this.openSession = null;
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
        yield return this.CreateCommandName(Back);
        if (this.openSession is { } resource && this.Board.Find(resource) is { } session)
        {
            yield return this.CreateCommandName(OpenSessionPrefix + session.Resource);
            foreach (var chat in session.Chats)
            {
                yield return this.CreateCommandName(ChatPrefix + chat.Resource);
            }
            yield break;
        }

        yield return this.CreateCommandName(New);
        foreach (var s in this.Board.Ranked)
        {
            yield return this.CreateCommandName(SessionPrefix + s.Resource);
        }
    }

    public override string GetCommandDisplayName(string actionParameter, PluginImageSize imageSize)
    {
        if (actionParameter == Back) return "Back";
        if (actionParameter == New) return "New";
        if (actionParameter.StartsWith(OpenSessionPrefix, StringComparison.Ordinal))
        {
            return KeyFace.Label(this.Board.Find(actionParameter[OpenSessionPrefix.Length..])?.Title);
        }
        if (actionParameter.StartsWith(SessionPrefix, StringComparison.Ordinal))
        {
            var s = this.Board.Find(actionParameter[SessionPrefix.Length..]);
            return KeyFace.Label(s is null ? null : s.Chats.Count > 1 ? $"{s.Title} ({s.Chats.Count})" : s.Title);
        }
        if (actionParameter.StartsWith(ChatPrefix, StringComparison.Ordinal))
        {
            return KeyFace.Label(this.FindChat(actionParameter[ChatPrefix.Length..])?.Chat.Title);
        }
        return KeyFace.Blank;
    }

    public override BitmapImage GetCommandImage(string actionParameter, PluginImageSize imageSize)
    {
        if (actionParameter == Back) return Faces.Glyph("‹", imageSize);
        if (actionParameter == New) return Faces.Glyph("+", imageSize);
        if (actionParameter.StartsWith(OpenSessionPrefix, StringComparison.Ordinal))
        {
            return Faces.Ring(this.Board.Find(actionParameter[OpenSessionPrefix.Length..])?.State ?? KeyState.None, imageSize);
        }
        if (actionParameter.StartsWith(SessionPrefix, StringComparison.Ordinal))
        {
            return Faces.Ring(this.Board.Find(actionParameter[SessionPrefix.Length..])?.State ?? KeyState.None, imageSize);
        }
        if (actionParameter.StartsWith(ChatPrefix, StringComparison.Ordinal))
        {
            return Faces.Ring(this.FindChat(actionParameter[ChatPrefix.Length..])?.Chat.State ?? KeyState.None, imageSize);
        }
        return Faces.Ring(KeyState.None, imageSize);
    }

    public override bool ProcessButtonEvent2(string actionParameter, DeviceButtonEvent2 buttonEvent)
    {
        if (buttonEvent.EventType == DeviceButtonEventType.LongPress && actionParameter.StartsWith(SessionPrefix, StringComparison.Ordinal))
        {
            this.Plugin.Log.Info($"long press received after {buttonEvent.PressDuration} ms");
            this.longPressHandled = true;
            Opener.Open(Links.OpenSession(actionParameter[SessionPrefix.Length..]));
            return true;
        }
        return base.ProcessButtonEvent2(actionParameter, buttonEvent);
    }

    public override void RunCommand(string actionParameter)
    {
        if (this.longPressHandled)
        {
            this.longPressHandled = false;
            return;
        }

        if (actionParameter == Back)
        {
            if (this.openSession is null)
            {
                this.Close();
            }
            else
            {
                this.openSession = null;
                this.ButtonActionNamesChanged();
            }
        }
        else if (actionParameter == New)
        {
            Opener.Open(Links.NewSession(" "));
        }
        else if (actionParameter.StartsWith(OpenSessionPrefix, StringComparison.Ordinal))
        {
            Opener.Open(Links.OpenSession(actionParameter[OpenSessionPrefix.Length..]));
        }
        else if (actionParameter.StartsWith(SessionPrefix, StringComparison.Ordinal))
        {
            var resource = actionParameter[SessionPrefix.Length..];
            if (this.Board.Find(resource) is { Chats.Count: > 1 })
            {
                this.openSession = resource;
                this.ButtonActionNamesChanged();
            }
            else
            {
                Opener.Open(Links.OpenSession(resource));
            }
        }
        else if (actionParameter.StartsWith(ChatPrefix, StringComparison.Ordinal) && this.FindChat(actionParameter[ChatPrefix.Length..]) is { } found)
        {
            Opener.Open(Links.OpenSession(found.Session.Resource, found.Chat.ChatId));
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
        try
        {
            this.ButtonActionNamesChanged();
        }
        catch (Exception e)
        {
            this.Plugin.Log.Error(e, "could not refresh the Agents window folder");
        }
    }
}
