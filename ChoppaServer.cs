using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace BigChoppa;

// Runs on the host (or in-process when offline). Owns the list of choppas, who is sitting where, and whose
// PC simulates each one. The simulating PC ("owner") is whoever last took the pilot seat; everyone else
// shows an interpolated copy. The host only validates and relays.
internal static class ChoppaServer
{
    class Entry
    {
        public uint Id, Owner;
        public Vehicle Vehicle;
        public uint[] Seats;
        public Vector3 Pos;
        public Quaternion Rot = Quaternion.identity;
        public byte[] LastState;
    }

    static readonly Dictionary<uint, Entry> helis = new();

    public static void Reset() => helis.Clear();

    public static void OnMessage(int conn, uint sender, Msg type, BinaryReader r)
    {
        if (sender == 0 && type != Msg.Hello) return; // no player object yet
        switch (type)
        {
            case Msg.Hello: SendWorld(conn); break;
            case Msg.Spawn: OnSpawn(sender, r); break;
            case Msg.Despawn: OnDespawn(sender, r.ReadUInt32()); break;
            case Msg.State: OnState(conn, sender, r); break;
            case Msg.Board: OnBoard(sender, r.ReadUInt32(), r.ReadByte()); break;
            case Msg.Leave: OnLeave(sender, r.ReadUInt32()); break;
            case Msg.Crash: OnCrash(conn, sender, r); break;
        }
    }

    // A late joiner gets every existing choppa, its seats and its latest pose.
    static void SendWorld(int conn)
    {
        foreach (var e in helis.Values)
        {
            ChoppaNet.ToClient(conn, SpawnMsg(e), true);
            ChoppaNet.ToClient(conn, SeatsMsg(e), true);
            if (e.LastState != null) ChoppaNet.ToClient(conn, e.LastState, true);
        }
    }

    static void OnSpawn(uint sender, BinaryReader r)
    {
        uint id = r.ReadUInt32();
        if (id == 0 || helis.ContainsKey(id)) return;
        var e = new Entry { Id = id, Owner = sender, Pos = r.ReadVector3(), Rot = r.ReadQuaternion() };
        e.Vehicle = Vehicles.Parse(r.ReadByte());
        e.Seats = new uint[Vehicles.SeatCount(e.Vehicle)];
        helis[id] = e;
        Plugin.Verbose($"[server] Choppa {id:X8} spawned by {sender}.");
        ChoppaNet.ToClients(SpawnMsg(e), true);
    }

    static void OnDespawn(uint sender, uint id)
    {
        if (!helis.TryGetValue(id, out var e) || e.Owner != sender) return;
        helis.Remove(id);
        ChoppaNet.ToClients(ChoppaNet.Write(Msg.Despawn, w => w.Write(id)), true);
    }

    static void OnState(int conn, uint sender, BinaryReader r)
    {
        var start = r.BaseStream.Position;
        uint id = r.ReadUInt32();
        if (!helis.TryGetValue(id, out var e) || e.Owner != sender) return;
        r.ReadDouble();
        e.Pos = r.ReadVector3();
        e.Rot = r.ReadQuaternion();

        r.BaseStream.Position = start;
        var raw = r.ReadBytes((int)(r.BaseStream.Length - start));
        var payload = new byte[raw.Length + 1];
        payload[0] = (byte)Msg.State;
        raw.CopyTo(payload, 1);
        e.LastState = payload;
        ChoppaNet.ToClients(payload, false, conn);
    }

    static void OnBoard(uint sender, uint id, byte seat)
    {
        if (!helis.TryGetValue(id, out var e) || seat >= e.Seats.Length) return;
        if (e.Seats[seat] != 0 && e.Seats[seat] != sender) return;

        foreach (var other in helis.Values)
            for (int i = 0; i < other.Seats.Length; i++)
                if (other.Seats[i] == sender && (other != e || i != seat))
                {
                    other.Seats[i] = 0;
                    if (other != e) ChoppaNet.ToClients(SeatsMsg(other), true);
                }

        e.Seats[seat] = sender;
        if (seat == 0 && e.Owner != sender) SetOwner(e, sender);
        ChoppaNet.ToClients(SeatsMsg(e), true);
    }

    static void OnLeave(uint sender, uint id)
    {
        if (!helis.TryGetValue(id, out var e)) return;
        bool changed = false;
        for (int i = 0; i < e.Seats.Length; i++)
            if (e.Seats[i] == sender) { e.Seats[i] = 0; changed = true; }
        if (changed) ChoppaNet.ToClients(SeatsMsg(e), true);
    }

    static void OnCrash(int conn, uint sender, BinaryReader r)
    {
        var start = r.BaseStream.Position;
        uint id = r.ReadUInt32();
        if (!helis.TryGetValue(id, out var e) || e.Owner != sender) return;
        helis.Remove(id);
        r.BaseStream.Position = start;
        var raw = r.ReadBytes((int)(r.BaseStream.Length - start));
        var payload = new byte[raw.Length + 1];
        payload[0] = (byte)Msg.Crash;
        raw.CopyTo(payload, 1);
        ChoppaNet.ToClients(payload, true, conn); // the owner already broke its own copy
    }

    // Called about once a second with the netIds of every player still in the game.
    public static void Prune(HashSet<uint> present, uint hostNetId)
    {
        foreach (var e in helis.Values.ToList())
        {
            bool seatsChanged = false;
            for (int i = 0; i < e.Seats.Length; i++)
                if (e.Seats[i] != 0 && !present.Contains(e.Seats[i])) { e.Seats[i] = 0; seatsChanged = true; }

            if (!present.Contains(e.Owner))
            {
                Plugin.L.LogInfo($"[server] Owner of choppa {e.Id:X8} left; host takes it over.");
                SetOwner(e, hostNetId);
            }
            if (seatsChanged) ChoppaNet.ToClients(SeatsMsg(e), true);
        }
    }

    static void SetOwner(Entry e, uint owner)
    {
        e.Owner = owner;
        ChoppaNet.ToClients(ChoppaNet.Write(Msg.Owner, w => { w.Write(e.Id); w.Write(owner); }), true);
    }

    static byte[] SpawnMsg(Entry e) => ChoppaNet.Write(Msg.Spawn, w =>
    {
        w.Write(e.Id);
        w.Write(e.Owner);
        w.Write(e.Pos);
        w.Write(e.Rot);
        w.Write((byte)e.Vehicle);
    });

    static byte[] SeatsMsg(Entry e) => ChoppaNet.Write(Msg.Seats, w =>
    {
        w.Write(e.Id);
        w.Write((byte)e.Seats.Length);
        foreach (var s in e.Seats) w.Write(s);
    });
}
