namespace Odyssey.Tests;

internal static class Utils
{
    public static EventData MapEvent<TEvent>(TEvent @event)
        => new(Guid.NewGuid(), @event!.GetType().Name, @event);

    public static string CreateStreamId() => Guid.NewGuid().ToString();
}