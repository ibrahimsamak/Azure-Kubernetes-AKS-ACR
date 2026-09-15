namespace OrderFlow.Messaging.Inbox;

using Microsoft.Data.SqlClient;

public static class InboxDuplicateDetector
{
    /// <summary>True if this exception is "someone already processed this message".
    /// 2627 = primary key violation, 2601 = unique index violation.</summary>
    public static bool IsDuplicate(Exception ex) =>
        ex.InnerException is SqlException { Number: 2627 or 2601 };
}
