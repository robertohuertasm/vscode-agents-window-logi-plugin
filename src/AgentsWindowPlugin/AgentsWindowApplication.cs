namespace Loupedeck.AgentsWindowPlugin;

// The Plugin Service will not load a plugin without a ClientApplication, even one
// not tied to any app.
public class AgentsWindowApplication : ClientApplication
{
    protected override string GetProcessName() => "";

    protected override string GetBundleName() => "";

    public override ClientApplicationStatus GetApplicationStatus() => ClientApplicationStatus.Unknown;
}
