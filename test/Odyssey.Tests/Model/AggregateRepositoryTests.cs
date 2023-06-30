using Microsoft.Extensions.Options;
using Odyssey.Model;
using Shouldly;

namespace Odyssey.Tests.Model;

public class AggregateRepositoryTests
{
    private readonly AggregateRepository<Guid> _repository;

    public AggregateRepositoryTests()
    {
        _repository = new AggregateRepository<Guid>(new InMemoryEventStore(), Options.Create(new OdysseyOptions()));
    }

    [Fact]
    public async Task Can_persist_and_append_state()
    {
        var basket = Basket.Create();
        basket.AddItem("Apple", 2);
        basket.AddItem("Banana", 1);
        basket.RemoveItem("Apple", 1);

        await _repository.Save(basket);

        var fromDb = await _repository.GetById<Basket>(basket.Id);

        fromDb.IsT0.ShouldBeTrue();
        fromDb.AsT0.ItemCount.ShouldBe(2);
        fromDb.AsT0.CurrentVersion.ShouldBe(3);
    }

    private sealed class Basket : Aggregate<Guid>
    {
        public int ItemCount { get; private set; }

        public void AddItem(string item, int quantity) => Raise(new ItemAdded(Id, item, quantity));
        public void RemoveItem(string item, int quantity) => Raise(new ItemRemoved(Id, item, quantity));

        public static Basket Create()
        {
            var basket = new Basket();
            basket.Raise(new BasketCreated(Guid.NewGuid()));
            return basket;
        }

        protected override void When(object @event)
        {
            switch (@event)
            {
                case BasketCreated created:
                    Id = created.BasketId;
                    break;
                case ItemAdded added:
                    ItemCount += added.Quantity;
                    break;
                case ItemRemoved removed:
                    ItemCount -= removed.Quantity;
                    break;
            }
        }
    }

    public record BasketCreated(Guid BasketId);
    public record ItemAdded(Guid BasketId, string Item, int Quantity);
    public record ItemRemoved(Guid BasketId, string Item, int Quantity);
}