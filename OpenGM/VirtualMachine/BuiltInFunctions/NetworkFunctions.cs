using UndertaleModLib.Models;
using EventType = OpenGM.VirtualMachine.EventType;

namespace OpenGM.VirtualMachine.BuiltInFunctions;

/// <summary>
/// GameMaker Studio's TCP client functions, connected to loopback ports the host answers.
///
/// What a game reaches on <c>127.0.0.1</c> on a real machine is, here, the host: a connection to
/// one of <see cref="Ports"/> becomes a byte pipe the JS side drains and fills through
/// OpenGM.Wasm/WasmEntry.cs, and a connection to anything else fails the way a machine with nothing
/// listening would. What the bytes mean is no business of the engine's.
///
/// Only the raw client is here -- <c>network_create_socket</c>, <c>network_connect_raw</c>,
/// <c>network_send_raw</c>, <c>network_destroy</c> and the Async Networking event's data -- because
/// that is what a game talking to something beside it uses. Servers, UDP and packet framing are not.
/// </summary>
public static class NetworkFunctions
{
    /// <summary>Something the host has to be told, in order: open (Arg = port), send (Arg = byte count), end.</summary>
    public enum EventKind { Open = 0, Send = 1, End = 2 }

    public static int[] Ports = [];

    private static readonly List<(EventKind Kind, int Id, int Arg)> Events = [];
    private static readonly List<byte> SentBytes = [];
    private static readonly Queue<(int Id, byte[] Bytes)> Received = new();
    private static readonly HashSet<int> Open = [];
    private static int _nextId = 1;

    /// <summary>Called at load: the engine's static state outlives a game in the worker.</summary>
    public static void Reset()
    {
        Events.Clear();
        SentBytes.Clear();
        Received.Clear();
        Open.Clear();
        _nextId = 1;
    }

    /// <summary>Takes every event since the last call, and the bytes the Send events carry, concatenated.</summary>
    public static (int[] Events, byte[] Bytes) Drain()
    {
        var events = Events.SelectMany(e => new[] { (int)e.Kind, e.Id, e.Arg }).ToArray();
        var bytes = SentBytes.ToArray();
        Events.Clear();
        SentBytes.Clear();
        return (events, bytes);
    }

    public static void Push(int id, byte[] bytes)
    {
        if (Open.Contains(id) && bytes.Length > 0)
        {
            Received.Enqueue((id, bytes));
        }
    }

    /// <summary>The host closed <paramref name="id"/>. A closed socket just stops hearing anything.</summary>
    public static void ClosedByHost(int id) => Open.Remove(id);

    /// <summary>
    /// Fires the Async Networking event for what arrived since the last frame, once per arrival, in
    /// every instance that has one -- the event is global in GameMaker, not addressed to an instance.
    /// </summary>
    public static void HandleReceived()
    {
        while (Received.TryDequeue(out var arrival))
        {
            var buffer = BufferManager.CreateBuffer(arrival.Bytes.Length, BufferType.Grow, 1, arrival.Bytes);
            var map = (int)DataStructuresFunctions.ds_map_create();
            DataStructuresFunctions.ds_map_add([map, "type", 3]); // network_type_data
            DataStructuresFunctions.ds_map_add([map, "id", arrival.Id]);
            DataStructuresFunctions.ds_map_add([map, "ip", "127.0.0.1"]);
            DataStructuresFunctions.ds_map_add([map, "buffer", buffer]);
            DataStructuresFunctions.ds_map_add([map, "size", arrival.Bytes.Length]);

            var previous = AsyncManager.AsyncLoadDsIndex;
            AsyncManager.AsyncLoadDsIndex = map;
            foreach (var instance in InstanceManager.instances.Values.ToList())
            {
                GamemakerObject.ExecuteEvent(instance, instance.Definition, EventType.Other, (int)EventSubtypeOther.AsyncNetworking);
            }
            AsyncManager.AsyncLoadDsIndex = previous;

            // Both live only for the event, as in GameMaker.
            DataStructuresFunctions.ds_map_destroy([map]);
            BufferManager.Buffers.Remove(buffer);
        }
    }

    [GMLFunction("network_create_socket")]
    public static object? network_create_socket(object?[] args)
    {
        // TCP only (network_socket_tcp, 0); anything else is refused rather than half-supported.
        return args[0].Conv<int>() == 0 ? _nextId++ : -1;
    }

    /// <summary>Synchronous, as in GameMaker: &lt; 0 when nothing answers.</summary>
    [GMLFunction("network_connect_raw")]
    public static object? network_connect_raw(object?[] args)
    {
        var socket = args[0].Conv<int>();
        var address = args[1].Conv<string>();
        var port = args[2].Conv<int>();
        var loopback = address is "127.0.0.1" or "localhost" or "::1";
        if (!loopback || !Ports.Contains(port) || !Open.Add(socket))
        {
            return -1;
        }

        Events.Add((EventKind.Open, socket, port));
        return 0;
    }

    /// <summary>Sends the first <c>size</c> bytes of a buffer; returns how many were sent, or &lt; 0.</summary>
    [GMLFunction("network_send_raw")]
    public static object? network_send_raw(object?[] args)
    {
        var socket = args[0].Conv<int>();
        var buffer = args[1].Conv<int>();
        var size = args[2].Conv<int>();
        if (!Open.Contains(socket) || !BufferManager.Buffers.TryGetValue(buffer, out var source))
        {
            return -1;
        }

        size = Math.Clamp(size, 0, source.Data.Length);
        Events.Add((EventKind.Send, socket, size));
        SentBytes.AddRange(source.Data.AsSpan(0, size).ToArray());
        return size;
    }

    [GMLFunction("network_destroy")]
    public static object? network_destroy(object?[] args)
    {
        var socket = args[0].Conv<int>();
        if (Open.Remove(socket))
        {
            Events.Add((EventKind.End, socket, 0));
        }

        return null;
    }
}
