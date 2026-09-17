namespace OrderFlow.Messaging.UnitTests;

using Microsoft.EntityFrameworkCore;
using OrderFlow.Messaging.Inbox;

public sealed class IdempotencyTests
{
    [Fact]
    public void A_plain_exception_is_not_a_duplicate()
    {
        Assert.False(InboxDuplicateDetector.IsDuplicate(new InvalidOperationException("boom")));
    }

    [Fact]
    public void A_DbUpdateException_with_no_inner_SqlException_is_not_a_duplicate()
    {
        // Losing a concurrency race is not the same as "already handled". Treating it as a
        // duplicate would silently skip work that never ran.
        var ex = new DbUpdateException("update failed", new TimeoutException());

        Assert.False(InboxDuplicateDetector.IsDuplicate(ex));
    }

    [Fact]
    public void A_null_inner_exception_is_not_a_duplicate()
    {
        Assert.False(InboxDuplicateDetector.IsDuplicate(new DbUpdateException("no inner")));
    }
}
