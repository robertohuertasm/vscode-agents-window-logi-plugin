namespace Loupedeck.AgentsWindowPlugin;

using AgentsWindow;

// A Dynamic Folder with two levels, like Logitech's Home Assistant plugin:
// sessions, then the chats of one session. The host always draws its own back key
// (top left), which closes the folder, so the sessions level adds none. The chats
// level adds a Back key that returns to the sessions, then one key per chat; the main
// chat's key opens the session itself.
// A short press on a session opens it. A long press on a session with several chats
// (badged with the count) enters its chat level.
public sealed class SessionsFolder : PluginDynamicFolder
{
    private const string Back = "back";
    private const string New = "new";
    private const string SessionPrefix = "s:";
    private const string ChatPrefix = "c:";

    // Folder key names carry the state ("s:Working|<resource>"), so a state change renames
    // the key and the host fetches a fresh face instead of reusing a cached one.
    private const char StateSeparator = '|';

    // How a profile addresses this folder's own key; PluginDynamicFolder has no ButtonImageChanged.
    private const string FolderActionName = "#DynamicFolder";

    private string? openSession;
    private bool longPressHandled;
    private string[] shownNames = [];

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

    // The host's own back key closes the folder without telling RunCommand, so the
    // level is reset whenever the folder opens or closes. Otherwise it reopens on the
    // chats of the last session.
    public override bool Activate()
    {
        this.Plugin.Log.Info("folder activated");
        this.ResetLevel();
        return true;
    }

    public override bool Deactivate()
    {
        this.Plugin.Log.Info("folder deactivated");
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
            foreach (var chat in session.Chats)
            {
                yield return this.CreateCommandName($"{ChatPrefix}{chat.State}{StateSeparator}{chat.Resource}");
            }
            yield break;
        }

        yield return this.CreateCommandName(New);
        foreach (var s in this.Board.Ranked)
        {
            yield return this.CreateCommandName($"{SessionPrefix}{s.State}{s.Chats.Count}{StateSeparator}{s.Resource}");
        }
    }

    public override string GetCommandDisplayName(string actionParameter, PluginImageSize imageSize)
    {
        if (actionParameter == Back) return "Back";
        if (actionParameter == New) return "New";
        if (actionParameter.StartsWith(SessionPrefix, StringComparison.Ordinal))
        {
            var s = this.Board.Find(Resource(actionParameter, SessionPrefix));
            return KeyFace.Label(s?.Title);
        }
        if (actionParameter.StartsWith(ChatPrefix, StringComparison.Ordinal))
        {
            return KeyFace.Label(this.FindChat(Resource(actionParameter, ChatPrefix))?.Chat.Title);
        }
        return KeyFace.Blank;
    }

    public override BitmapImage GetCommandImage(string actionParameter, PluginImageSize imageSize)
    {
        if (actionParameter == Back) return Faces.Glyph("‹", imageSize);
        if (actionParameter == New) return Faces.Glyph("+", imageSize);
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

    // Session keys handle their own events so a hold can open the session instead of
    // entering its chats: a short press acts on Release, a long press on LongPress.
    // Other keys keep the default handling, which calls RunCommand.
    public override bool ProcessButtonEvent2(string actionParameter, DeviceButtonEvent2 buttonEvent)
    {
        if (!actionParameter.StartsWith(SessionPrefix, StringComparison.Ordinal))
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
                this.LongPressSession(Resource(actionParameter, SessionPrefix));
                return true;
            case DeviceButtonEventType.RepeatPress:
                return true;
            case DeviceButtonEventType.Release:
                if (!this.longPressHandled)
                {
                    this.PressSession(Resource(actionParameter, SessionPrefix));
                }
                this.longPressHandled = false;
                return true;
            default:
                return false;
        }
    }

    public override void RunCommand(string actionParameter)
    {
        if (actionParameter == Back)
        {
            this.openSession = null;
            this.shownNames = [];
            this.ButtonActionNamesChanged();
        }
        else if (actionParameter == New)
        {
            Opener.Open(Links.NewSession(" "));
        }
        else if (actionParameter.StartsWith(SessionPrefix, StringComparison.Ordinal))
        {
            this.PressSession(Resource(actionParameter, SessionPrefix));
        }
        else if (actionParameter.StartsWith(ChatPrefix, StringComparison.Ordinal) && this.FindChat(Resource(actionParameter, ChatPrefix)) is { } found)
        {
            Opener.Open(Links.OpenSession(found.Session.Resource, found.Chat.ChatId));
        }
    }

    // A short press always opens the session.
    private void PressSession(string resource) => Opener.Open(Links.OpenSession(resource));

    // A long press enters the chats of a session that has several; otherwise it opens it.
    private void LongPressSession(string resource)
    {
        if (this.Board.Find(resource) is { Chats.Count: > 1 })
        {
            this.Plugin.Log.Info("long press: showing the session's chats");
            this.openSession = resource;
            this.shownNames = [];
            this.ButtonActionNamesChanged();
        }
        else
        {
            Opener.Open(Links.OpenSession(resource));
        }
    }

    private static string Resource(string actionParameter, string prefix)
    {
        var rest = actionParameter[prefix.Length..];
        var separator = rest.IndexOf(StateSeparator);
        return separator < 0 ? rest : rest[(separator + 1)..];
    }

    private void ResetLevel()
    {
        if (this.openSession is null) return;
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
        // The host caches the folder key's face and only re-reads it when asked, so without
        // this it keeps whatever state it drew last (for example amber after a page change).
        try
        {
            this.Plugin.OnActionImageChanged(FolderActionName, this.Name, true);
        }
        catch (Exception e)
        {
            this.Plugin.Log.Error(e, "could not redraw the Agents window folder key");
        }

        // ButtonActionNamesChanged alone does not redraw keys whose names stay the same,
        // and a session key's name is fixed while its state changes, so each key is
        // invalidated too.
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
