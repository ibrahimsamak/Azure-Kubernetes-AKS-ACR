namespace OrderFlow.Order.Domain.Common;

/// <summary>A business rule was violated. Maps to 400/409 at the API edge, never to 500.</summary>
public sealed class DomainException : Exception
{
    public DomainException() { }

    public DomainException(string message) : base(message) { }

    public DomainException(string message, Exception innerException) : base(message, innerException) { }
}
