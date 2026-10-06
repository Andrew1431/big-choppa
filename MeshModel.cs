using System;
using System.Collections.Generic;
using System.IO;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace BigChoppa;

// Reads a model exported by model/export_mh6.py (embedded in the DLL) and rebuilds it as GameObjects.
// Coordinates are already converted to Unity's; see the exporter for the format.
internal sealed class MeshModel
{
    public sealed class Node
    {
        public string Name;
        public int Parent;
        public Vector3 Pos, Scale;
        public Quaternion Rot;
        public readonly Dictionary<string, object> Props = new(); // float, string or float[]
        public Vector3[] Vertices, Normals;
        public int[] SubMaterials;
        public int[][] SubIndices;
        public Mesh Mesh; // built on first use, shared by every choppa
        public bool HasMesh => Vertices != null;

        public float Float(string key, float fallback) => Props.TryGetValue(key, out var v) && v is float f ? f : fallback;
        public string String(string key) => Props.TryGetValue(key, out var v) ? v as string : null;
        public Color Color(string key, Color fallback) =>
            Props.TryGetValue(key, out var v) && v is float[] a && a.Length >= 3 ? new Color(a[0], a[1], a[2], a.Length > 3 ? a[3] : 1f) : fallback;
    }

    public string[] MaterialNames;
    public Color[] MaterialColors;
    public Node[] Nodes;

    static readonly Dictionary<string, MeshModel> cache = new();

    public static MeshModel Load(string resource)
    {
        if (cache.TryGetValue(resource, out var m)) return m;
        var asm = typeof(MeshModel).Assembly;
        using var s = asm.GetManifestResourceStream(resource) ?? throw new FileNotFoundException($"Embedded model '{resource}' missing from the DLL.");
        using var r = new BinaryReader(s);
        m = Read(r);
        cache[resource] = m;
        Plugin.L.LogInfo($"Loaded model {resource}: {m.Nodes.Length} nodes, {m.MaterialNames.Length} materials.");
        return m;
    }

    static MeshModel Read(BinaryReader r)
    {
        if (new string(r.ReadChars(4)) != "MH6M") throw new InvalidDataException("Not a Big Choppa model file.");
        int version = r.ReadUInt16();
        if (version != 1) throw new InvalidDataException($"Model format {version} not supported.");

        var m = new MeshModel();
        int matCount = r.ReadUInt16();
        m.MaterialNames = new string[matCount];
        m.MaterialColors = new Color[matCount];
        for (int i = 0; i < matCount; i++)
        {
            m.MaterialNames[i] = r.ReadString();
            m.MaterialColors[i] = new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        }

        m.Nodes = new Node[r.ReadUInt16()];
        for (int i = 0; i < m.Nodes.Length; i++)
        {
            var n = new Node
            {
                Name = r.ReadString(),
                Parent = r.ReadInt16(),
                Pos = ReadV3(r),
                Rot = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
                Scale = ReadV3(r),
            };
            int props = r.ReadByte();
            for (int p = 0; p < props; p++)
            {
                string key = r.ReadString();
                switch (r.ReadByte())
                {
                    case 0: n.Props[key] = r.ReadSingle(); break;
                    case 1: n.Props[key] = r.ReadString(); break;
                    default:
                        var a = new float[r.ReadByte()];
                        for (int k = 0; k < a.Length; k++) a[k] = r.ReadSingle();
                        n.Props[key] = a;
                        break;
                }
            }
            if (r.ReadByte() != 0)
            {
                int vc = r.ReadInt32();
                n.Vertices = new Vector3[vc];
                n.Normals = new Vector3[vc];
                for (int v = 0; v < vc; v++)
                {
                    n.Vertices[v] = ReadV3(r);
                    n.Normals[v] = ReadV3(r);
                }
                int subs = r.ReadByte();
                n.SubMaterials = new int[subs];
                n.SubIndices = new int[subs][];
                for (int sm = 0; sm < subs; sm++)
                {
                    n.SubMaterials[sm] = r.ReadUInt16();
                    var idx = new int[r.ReadInt32()];
                    for (int k = 0; k < idx.Length; k++) idx[k] = r.ReadInt32();
                    n.SubIndices[sm] = idx;
                }
            }
            m.Nodes[i] = n;
        }
        return m;
    }

    static Vector3 ReadV3(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

    // Builds every node under `model` (node 0 is the model itself). Returns transforms by node name.
    // `skip` nodes (and their children) get no GameObject; the caller handles them from the node data.
    public Dictionary<string, Transform> Instantiate(Transform model, Func<Node, bool> skip)
    {
        var byIndex = new Transform[Nodes.Length];
        var byName = new Dictionary<string, Transform>();
        byIndex[0] = model;
        byName[Nodes[0].Name] = model;
        for (int i = 1; i < Nodes.Length; i++)
        {
            var n = Nodes[i];
            var parent = n.Parent >= 0 ? byIndex[n.Parent] : model;
            if (parent == null || skip(n)) continue;

            var go = new GameObject(n.Name);
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = n.Pos;
            t.localRotation = n.Rot;
            t.localScale = n.Scale;
            if (n.HasMesh)
            {
                go.AddComponent<MeshFilter>().sharedMesh = MeshFor(n);
                var mats = new Il2CppReferenceArray<Material>(n.SubMaterials.Length);
                for (int sm = 0; sm < mats.Length; sm++)
                {
                    var c = MaterialColors[n.SubMaterials[sm]];
                    mats[sm] = c.a < 0.99f ? ChoppaMaterials.GetTransparent(c) : ChoppaMaterials.Get(c);
                }
                go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            }
            byIndex[i] = t;
            byName[n.Name] = t;
        }
        return byName;
    }

    static Mesh MeshFor(Node n)
    {
        if (n.Mesh != null && !n.Mesh.WasCollected) return n.Mesh;
        var mesh = new Mesh { name = n.Name };
        // Kept across scene loads; the game unloading "unused" assets would otherwise free it between spawns.
        mesh.hideFlags = HideFlags.DontUnloadUnusedAsset;
        if (n.Vertices.Length > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = n.Vertices;
        mesh.normals = n.Normals;
        mesh.subMeshCount = n.SubIndices.Length;
        for (int sm = 0; sm < n.SubIndices.Length; sm++)
            mesh.SetTriangles((Il2CppStructArray<int>)n.SubIndices[sm], sm);
        mesh.RecalculateBounds();
        n.Mesh = mesh;
        return mesh;
    }

    public Node Find(string name)
    {
        foreach (var n in Nodes) if (n.Name == name) return n;
        return null;
    }

    // World-in-model-space bounds of a mesh node that hangs directly off the root.
    public static Bounds ModelBounds(Node n)
    {
        var b = new Bounds(n.Rot * Vector3.Scale(n.Scale, n.Vertices[0]) + n.Pos, Vector3.zero);
        foreach (var v in n.Vertices) b.Encapsulate(n.Rot * Vector3.Scale(n.Scale, v) + n.Pos);
        return b;
    }
}
