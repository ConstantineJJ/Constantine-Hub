namespace ConstantineHub.Core;

// An expected control limitation, not a failed service or application error.
internal sealed class ExternalTunnelControlException(string service) : Exception(
    service + " is connected through a tunnel started outside Constantine Hub.\n\n" + Guidance)
{
    internal const string Guidance = "Stop or restart this tunnel in the app or terminal that started it. " +
        "To let Hub manage it, stop it there first, then press Start in Hub.";
}
