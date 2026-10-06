using System;
using System.Collections.Generic;
using System.IO;
using Il2CppInterop.Runtime;
using Mirror;
using UnityEngine;

namespace BigChoppa;

internal enum Msg : byte
{
    Hello = 1, HelloAck, Spawn, State, Board, Leave, Seats, Owner, Crash, Despawn,
}

// Rides on the game's own Mirror connection: one raw handler id on both NetworkServer and NetworkClient,
// carrying [ushort length][Msg type][payload]. IL2CPP can't host new generic Mirror message structs, so we
// write bytes ourselves. Without a network (or with networking disabled) messages loop back in-process,
// so the rest of the mod has a single code path.
internal static class ChoppaNet
{
    public const ushort Protocol = 3; // 3: Spawn carries the vehicle; seat count depends on it
    public const int LocalConn = -1;
    const ushort HandlerId = 0xC40F; // arbitrary; checked for collisions at registration
    const int Reliable = 0, Unreliable = 1;

    public enum NetMode { Offline, Connecting, Client, Host }
    public static NetMode Mode { get; private set; } = NetMode.Offline;

    // Set by ChoppaManager once the local player exists; 0 until then.
    public static uint LocalNetId;

    // Client side: true once the host has confirmed it runs a compatible Big Choppa.
    public static bool Ready => Mode == NetMode.Offline || serverAcked;

    public static Action<int, uint, Msg, BinaryReader> ServerReceived; // conn, sender player netId, type, payload
    public static Action<Msg, BinaryReader> ClientReceived;
    public static Action ModeChanged;

    public static double Time => Mode == NetMode.Offline ? UnityEngine.Time.timeAsDouble : NetworkTime.time;

    static bool serverAcked, helloSent;
    static readonly HashSet<int> moddedConns = new();
    static NetworkMessageDelegate serverHandler, clientHandler; // held so the GC doesn't collect them
    static bool collisionWarned;

    public static void Tick()
    {
        var mode = DetectMode();
        if (mode != Mode)
        {
            Plugin.L.LogInfo($"Network mode {Mode} -> {mode}");
            Mode = mode;
            serverAcked = helloSent = false;
            moddedConns.Clear();
            ModeChanged?.Invoke();
        }
        if (Mode is NetMode.Offline or NetMode.Connecting) return;

        if (Mode == NetMode.Host) EnsureHandler(NetworkServer.handlers, ref serverHandler, OnServerData, "server");
        EnsureHandler(NetworkClient.handlers, ref clientHandler, OnClientData, "client");

        if (!helloSent && LocalNetId != 0 && NetworkClient.connection != null)
        {
            helloSent = true;
            ToServer(Write(Msg.Hello, w => w.Write(Protocol)), true);
            Plugin.Verbose("Sent Big Choppa hello to host.");
        }
    }

    static NetMode DetectMode()
    {
        if (!ChoppaConfig.NetEnabled.Value) return NetMode.Offline;
        if (NetworkServer.active) return NetMode.Host;
        if (NetworkClient.isConnected) return NetMode.Client;
        if (NetworkClient.active) return NetMode.Connecting;
        return NetMode.Offline;
    }

    static void EnsureHandler(Il2CppSystem.Collections.Generic.Dictionary<ushort, NetworkMessageDelegate> handlers,
        ref NetworkMessageDelegate mine, Action<NetworkConnection, NetworkReader, int> callback, string side)
    {
        if (handlers == null) return;
        if (handlers.ContainsKey(HandlerId))
        {
            if (mine == null && !collisionWarned)
            {
                collisionWarned = true;
                Plugin.L.LogError($"Mirror message id {HandlerId:X4} is already used by the game ({side}); networking disabled.");
            }
            return;
        }
        mine ??= DelegateSupport.ConvertDelegate<NetworkMessageDelegate>(callback);
        handlers.Add(HandlerId, mine);
        Plugin.Verbose($"Registered Big Choppa {side} handler {HandlerId:X4}.");
    }

    // ---------- sending ----------

    public static byte[] Write(Msg type, Action<BinaryWriter> body)
    {
        using var ms = new MemoryStream(64);
        using (var w = new BinaryWriter(ms))
        {
            w.Write((byte)type);
            body?.Invoke(w);
        }
        return ms.ToArray();
    }

    public static void ToServer(byte[] payload, bool reliable)
    {
        if (Mode == NetMode.Offline) { DispatchServer(LocalConn, LocalNetId, payload); return; }
        if (Mode == NetMode.Connecting) return;
        var type = (Msg)payload[0];
        if (!serverAcked && type != Msg.Hello) return;
        SendRaw(NetworkClient.connection, payload, reliable);
    }

    // Host only. exceptConn skips the sender when relaying.
    public static void ToClients(byte[] payload, bool reliable, int exceptConn = int.MinValue)
    {
        if (Mode == NetMode.Offline) { if (exceptConn != LocalConn) DispatchClient(payload); return; }
        if (Mode != NetMode.Host) return;
        foreach (var kv in NetworkServer.connections)
        {
            if (kv.Key == exceptConn || !moddedConns.Contains(kv.Key)) continue;
            SendRaw(kv.Value, payload, reliable);
        }
    }

    public static void ToClient(int conn, byte[] payload, bool reliable)
    {
        if (Mode == NetMode.Offline) { if (conn == LocalConn) DispatchClient(payload); return; }
        if (Mode != NetMode.Host) return;
        if (NetworkServer.connections.TryGetValue(conn, out var c)) SendRaw(c, payload, reliable);
    }

    static void SendRaw(NetworkConnection conn, byte[] payload, bool reliable)
    {
        if (conn == null) return;
        var w = NetworkWriterPool.Get();
        try
        {
            NetworkWriterExtensions.WriteUShort(w, HandlerId);
            NetworkWriterExtensions.WriteUShort(w, (ushort)payload.Length);
            w.WriteBytes(payload, 0, payload.Length);
            conn.Send(w.ToArraySegment(), reliable ? Reliable : Unreliable);
        }
        finally { NetworkWriterPool.Return(w); }
    }

    // ---------- receiving ----------

    static byte[] ReadPayload(NetworkReader reader)
    {
        int len = NetworkReaderExtensions.ReadUShort(reader);
        return NetworkReaderExtensions.ReadBytes(reader, len);
    }

    static void OnServerData(NetworkConnection conn, NetworkReader reader, int channel)
    {
        try
        {
            var payload = ReadPayload(reader);
            uint sender = conn.identity != null ? conn.identity.netId : 0u;
            var type = (Msg)payload[0];
            if (type == Msg.Hello)
            {
                ushort theirs = BitConverter.ToUInt16(payload, 1);
                if (theirs != Protocol)
                {
                    Plugin.L.LogWarning($"Connection {conn.connectionId} runs Big Choppa protocol {theirs}, we run {Protocol}; ignoring them.");
                    return;
                }
                moddedConns.Add(conn.connectionId);
                ToClient(conn.connectionId, Write(Msg.HelloAck, w => w.Write(Protocol)), true);
                Plugin.L.LogInfo($"Player netId {sender} (conn {conn.connectionId}) has Big Choppa.");
            }
            else if (!moddedConns.Contains(conn.connectionId)) return;
            DispatchServer(conn.connectionId, sender, payload);
        }
        catch (Exception e) { Plugin.L.LogError($"Big Choppa server message: {e}"); }
    }

    static void OnClientData(NetworkConnection conn, NetworkReader reader, int channel)
    {
        try
        {
            var payload = ReadPayload(reader);
            if ((Msg)payload[0] == Msg.HelloAck)
            {
                serverAcked = true;
                Plugin.L.LogInfo("Host has Big Choppa; choppas are shared.");
            }
            DispatchClient(payload);
        }
        catch (Exception e) { Plugin.L.LogError($"Big Choppa client message: {e}"); }
    }

    static void DispatchServer(int conn, uint sender, byte[] payload)
    {
        using var r = new BinaryReader(new MemoryStream(payload, 1, payload.Length - 1));
        ServerReceived?.Invoke(conn, sender, (Msg)payload[0], r);
    }

    static void DispatchClient(byte[] payload)
    {
        using var r = new BinaryReader(new MemoryStream(payload, 1, payload.Length - 1));
        ClientReceived?.Invoke((Msg)payload[0], r);
    }

    // Host only: netIds of every connected player's character, regardless of what's visible locally.
    public static HashSet<uint> ConnectedPlayers()
    {
        var set = new HashSet<uint>();
        if (LocalNetId != 0) set.Add(LocalNetId);
        foreach (var kv in NetworkServer.connections)
        {
            var id = kv.Value?.identity;
            if (id != null) set.Add(id.netId);
        }
        return set;
    }

    // ---------- payload helpers ----------

    public static void Write(this BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
    public static void Write(this BinaryWriter w, Quaternion q) { w.Write(q.x); w.Write(q.y); w.Write(q.z); w.Write(q.w); }
    public static Vector3 ReadVector3(this BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    public static Quaternion ReadQuaternion(this BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
}
