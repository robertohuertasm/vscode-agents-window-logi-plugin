namespace AgentsWindow;

// Product protocol links handled by VS Code's main process, which opens the Agents window.
// Shapes from src/vs/platform/agentHost/common/openSessionLink.ts and
// src/vs/platform/window/common/window.ts (VS Code 1.144).
public static class Links
{
    public const string DefaultChatId = "default";
    private const string CanonicalChatPrefix = "ahp-chat:/";
    private const string ShapedChatPrefix = "ahp-chat://";

    // `vscode://agents/agent-host-session/<provider>/<rawId>[/chat/<chatId>]`
    public static string OpenSession(string sessionResource, string? chatId = null, string protocol = "vscode")
    {
        var uri = new Uri(sessionResource);
        var provider = uri.Scheme;
        var rawId = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        var link = $"{protocol}://agents/agent-host-session/{provider}/{Uri.EscapeDataString(rawId)}";
        if (!string.IsNullOrEmpty(chatId) && chatId != DefaultChatId)
        {
            link += "/chat/" + Uri.EscapeDataString(Uri.EscapeDataString(chatId));
        }
        return link;
    }

    // `vscode://agents/new?prompt=...` opens the new-session composer; VS Code ignores the link without a prompt.
    public static string NewSession(string prompt, string protocol = "vscode") =>
        $"{protocol}://agents/new?prompt={Uri.EscapeDataString(prompt)}";

    // The client chat id VS Code uses in `chat=` links (getAgentHostChatId):
    // the uuid of `ahp-chat:/<uuid>`, the authority of `ahp-chat://<id>/...` unless it is
    // `default`, otherwise the whole resource. Null means "the session's default chat".
    public static string? ChatId(string chatResource, string? defaultChat)
    {
        if (chatResource == defaultChat) return null;
        if (chatResource.StartsWith(ShapedChatPrefix, StringComparison.Ordinal))
        {
            var authority = chatResource[ShapedChatPrefix.Length..].Split('/')[0];
            return authority == DefaultChatId ? null : authority;
        }
        if (chatResource.StartsWith(CanonicalChatPrefix, StringComparison.Ordinal))
        {
            var uuid = chatResource[CanonicalChatPrefix.Length..];
            return Guid.TryParse(uuid, out _) ? uuid : chatResource;
        }
        return chatResource;
    }
}
