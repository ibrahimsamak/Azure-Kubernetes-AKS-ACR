namespace OrderFlow.IntegrationTests;

using Microsoft.EntityFrameworkCore;
using OrderFlow.IntegrationTests.Fixtures;
using OrderFlow.Messaging.Inbox;
using OrderFlow.Order.Infrastructure.Persistence;

[Collection(DistributedCollection.Name)]
public sealed class IdempotentConsumerTests(DistributedFixture fixture)
{
    [DockerFact]
    public async Task A_redelivery_to_the_same_consumer_is_rejected_by_the_unique_index()
    {
        var messageId = Guid.CreateVersion7();

        await ClaimAsync(messageId, "order-service");

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ClaimAsync(messageId, "order-service"));

        Assert.True(InboxDuplicateDetector.IsDuplicate(ex),
            "a duplicate-key violation must be detected, or redeliveries would dead-letter");
    }

    [DockerFact]
    public async Task The_same_message_can_still_be_claimed_by_a_different_consumer()
    {
        var messageId = Guid.CreateVersion7();

        await ClaimAsync(messageId, "order-service");
        await ClaimAsync(messageId, "notification-service");

        await using var db = fixture.CreateOrderDb();
        var claims = await db.Set<InboxMessage>().CountAsync(x => x.MessageId == messageId);
        Assert.Equal(2, claims);
    }

    [DockerFact]
    public async Task A_committed_claim_is_visible_to_the_fast_path_check()
    {
        var messageId = Guid.CreateVersion7();
        await ClaimAsync(messageId, "order-service");

        await using var db = fixture.CreateOrderDb();
        var store = new EfInboxStore<OrderDbContext>(db, "order-service");

        Assert.True(await store.AlreadyProcessedAsync(messageId, CancellationToken.None));
        Assert.False(await store.AlreadyProcessedAsync(Guid.CreateVersion7(), CancellationToken.None));
    }

    private async Task ClaimAsync(Guid messageId, string consumer)
    {
        await using var db = fixture.CreateOrderDb();
        new EfInboxStore<OrderDbContext>(db, consumer).Claim(messageId, "OrderPlaced");
        await db.SaveChangesAsync();
    }
}
