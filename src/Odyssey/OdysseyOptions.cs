namespace Odyssey;

public delegate EventData EventDataFactory(object @event);

public sealed class OdysseyOptions
{
    public EventDataFactory EventDataFactory { get; set; }
        = @event => new EventData(Guid.NewGuid(), @event.GetType().Name, @event);
}