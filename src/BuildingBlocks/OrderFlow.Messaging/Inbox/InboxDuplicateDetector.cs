namespace OrderFlow.Messaging.Inbox;

using Microsoft.Data.SqlClient;

public static class InboxDuplicateDetector
{
    public static bool IsDuplicate(Exception ex) =>
        ex.InnerException is SqlException { Number: 2627 or 2601 };
}
