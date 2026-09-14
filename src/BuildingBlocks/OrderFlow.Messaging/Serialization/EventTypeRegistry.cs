namespace OrderFlow.Messaging.Serialization;

using System.Collections.Concurrent;
using System.Reflection;
using OrderFlow.Contracts;

public static class EventTypeRegistry
{
    private static readonly ConcurrentDictionary<string, Type> ByName = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<Type, string> ByType = new();


    /// <summary>Called once from DI setup. Scans OrderFlow.Contracts for IntegrationEvent types.</summary>
    public static void RegisterAssembly(Assembly assembly)
    {
        foreach (var t in assembly.GetTypes().Where(t => t is { IsAbstract: false } && t.IsAssignableTo(typeof(IntegrationEvent))))
        {
            ByName[t.Name] = t;   // short name is the wire contract, e.g. "OrderPlaced"
            ByType[t] = t.Name;
        }
    }
    /// <summary>Use for renames: Alias("OrderSubmitted", typeof(OrderPlaced)) keeps old
    /// messages readable after a refactor. Cheap insurance on a replayable log.</summary>
    public static void Alias(string wireName, Type clrType) => ByName[wireName] = clrType;

    public static string NameOf(Type t) => ByType.TryGetValue(t, out var n) ? n : t.Name;
    public static Type? Resolve(string name) => ByName.GetValueOrDefault(name);
}