namespace ConstantineHub;

internal enum JensenConnectionState { Disconnected, Connecting, Connected }

internal readonly record struct JensenAnimationFrame(float Connection, float Glow,
    float LeftAngle, float RightAngle, float RingAngle, float RingScale, float RingOpacity,
    float BurstAge, float BurstStrength, double Time);

// Only presentation time lives here. Connection truth comes from the existing adapter snapshot.
internal sealed class JensenAnimationTimeline
{
    private const double FadeSeconds = .65;
    private double _changedAt;
    private double _connectedAt;
    private float _fromConnection;
    private float _fromGlow;
    internal JensenConnectionState State { get; private set; }
    internal int TransitionCount { get; private set; }

    internal void SetState(JensenConnectionState state, double now)
    {
        if (state == State) return;
        var current = Sample(now);
        _fromConnection = current.Connection;
        _fromGlow = current.Glow;
        _changedAt = now;
        State = state;
        if (state == JensenConnectionState.Connected) _connectedAt = now;
        TransitionCount++;
    }

    internal bool NeedsFrames(double now) => State != JensenConnectionState.Disconnected
        || now - _changedAt < FadeSeconds && TransitionCount > 0;

    internal JensenAnimationFrame Sample(double now)
    {
        var progress = (float)Math.Clamp((now - _changedAt) / FadeSeconds, 0, 1);
        progress = progress * progress * (3 - 2 * progress);
        var connection = Lerp(_fromConnection, State == JensenConnectionState.Connected ? 1 : 0, progress);
        var glowTarget = State == JensenConnectionState.Connecting ? .10f + .035f * (float)Math.Sin(now * 2.6) : 0;
        var glow = Lerp(_fromGlow, glowTarget, progress);
        var elapsed = Math.Max(0, now - _connectedAt);
        var burstAge = elapsed < 1.2 ? elapsed : elapsed % 7;
        // The first flash also passes through the connection fade; compensate for that envelope.
        var burstStrength = elapsed < 1.2 ? 1.6f : .25f;
        return new(connection, glow, (float)(-now * 63 % 360), (float)(now * 79 % 360),
            (float)(now * 17 % 360), 1 + .035f * (float)Math.Sin(now * 1.7),
            .67f + .13f * (float)Math.Sin(now * 2.1 + .8), (float)burstAge, burstStrength, now);
    }

    private static float Lerp(float from, float to, float progress) => from + (to - from) * progress;
}
