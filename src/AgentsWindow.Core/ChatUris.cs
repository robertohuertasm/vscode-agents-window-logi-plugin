using System.Text;

namespace AgentsWindow;

// Mints chat URIs the host accepts for `createChat` (agentHostChatMinting.ts, VS Code 1.144):
// a session whose default chat is opaque (`ahp-chat:/<uuid>`) takes opaque chats, and any
// other session takes shaped ones (`ahp-chat://<uuid>/<base64url(session)>`).
public static class ChatUris
{
    public static string MintPeer(string sessionResource, string? defaultChat, Guid? id = null)
    {
        var uuid = (id ?? Guid.NewGuid()).ToString();
        var opaque = defaultChat is not null && !defaultChat.StartsWith("ahp-chat://", StringComparison.Ordinal);
        return opaque ? $"ahp-chat:/{uuid}" : $"ahp-chat://{uuid}/{Base64Url(sessionResource)}";
    }

    private static string Base64Url(string text) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
