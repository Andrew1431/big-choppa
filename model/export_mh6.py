# Exports the MH6 hierarchy from mh6.blend to Resources/mh6.bin, which the mod embeds and rebuilds at runtime.
#   blender --background model/mh6.blend --python model/export_mh6.py
# (or run it inside an open Blender session).
#
# Blender is right-handed Z-up with the nose at -Y and +X the aircraft's left; Unity is left-handed Y-up with the
# nose at +Z. Points map (x, y, z) -> (-x, z, -y). That map is a reflection (det -1), which is what turns
# right-handed into left-handed, so triangle winding is flipped to keep faces pointing outward.
#
# Format (little endian, strings are .NET BinaryWriter strings):
#   "MH6M" u16 version
#   u16 materials: name, rgba (sRGB floats)
#   u16 nodes (parents first): name, i16 parent, pos 3f, rot 4f (x y z w), scale 3f,
#       u8 props: key, u8 type (0 float, 1 string, 2 float array: u8 n, n floats)
#       u8 has mesh: i32 vertex count, per vertex pos 3f + normal 3f (flat shaded),
#                    u8 submeshes: u16 material, i32 index count, i32 indices
import bpy, struct, os
from mathutils import Matrix

VERSION = 1
ROOT = "MH6"
CONV = Matrix(((-1, 0, 0, 0), (0, 0, 1, 0), (0, -1, 0, 0), (0, 0, 0, 1)))
CONV_INV = CONV.inverted()


def conv_vec(v):
    return (-v.x, v.z, -v.y)


def lin_to_srgb(c):
    return c * 12.92 if c <= 0.0031308 else 1.055 * (c ** (1 / 2.4)) - 0.055


class W:
    def __init__(self):
        self.b = bytearray()

    def pack(self, fmt, *v):
        self.b += struct.pack("<" + fmt, *v)

    def string(self, s):
        data = s.encode("utf-8")
        n = len(data)
        while n >= 0x80:
            self.b.append((n & 0x7F) | 0x80)
            n >>= 7
        self.b.append(n)
        self.b += data


def material_color(mat):
    if mat is None:
        return (1, 0, 1, 1)
    bsdf = next((n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None) if mat.use_nodes else None
    if bsdf is None:
        r, g, b, a = mat.diffuse_color
    else:
        r, g, b, _ = bsdf.inputs["Base Color"].default_value
        a = bsdf.inputs["Alpha"].default_value
    return (lin_to_srgb(r), lin_to_srgb(g), lin_to_srgb(b), a)


def props_of(obj):
    out = []
    for k in obj.keys():
        if k.startswith("_"):
            continue
        v = obj[k]
        if isinstance(v, (int, float)) and not isinstance(v, bool):
            out.append((k, 0, float(v)))
        elif isinstance(v, str):
            out.append((k, 1, v))
        elif hasattr(v, "to_list") or isinstance(v, (list, tuple)):
            vals = v.to_list() if hasattr(v, "to_list") else list(v)
            out.append((k, 2, [float(x) for x in vals]))
    return out


def export(path):
    root = bpy.data.objects[ROOT]
    nodes = []

    def walk(o, parent):
        nodes.append((o, parent))
        idx = len(nodes) - 1
        for c in sorted(o.children, key=lambda c: c.name):
            walk(c, idx)

    walk(root, -1)

    materials = []
    mat_index = {}

    def mat_id(m):
        key = m.name if m else "<none>"
        if key not in mat_index:
            mat_index[key] = len(materials)
            materials.append((key, material_color(m)))
        return mat_index[key]

    dg = bpy.context.evaluated_depsgraph_get()
    body = W()
    body.pack("H", len(nodes))
    tris_total = 0
    for o, parent in nodes:
        body.string(o.name)
        body.pack("h", parent)
        if parent < 0:
            local = Matrix.Identity(4)  # the root becomes the mod's "Model" transform
        else:
            local = nodes[parent][0].matrix_world.inverted() @ o.matrix_world
        loc, rot, scale = (CONV @ local @ CONV_INV).decompose()
        body.pack("3f", *loc)
        body.pack("4f", rot.x, rot.y, rot.z, rot.w)
        body.pack("3f", *scale)

        props = props_of(o)
        body.pack("B", len(props))
        for k, t, v in props:
            body.string(k)
            body.pack("B", t)
            if t == 0:
                body.pack("f", v)
            elif t == 1:
                body.string(v)
            else:
                body.pack("B", len(v))
                body.pack(f"{len(v)}f", *v)

        if o.type != "MESH":
            body.pack("B", 0)
            continue
        ev = o.evaluated_get(dg)
        me = ev.to_mesh()
        me.calc_loop_triangles()
        verts, subs = [], {}
        for tri in me.loop_triangles:
            n = conv_vec(me.polygons[tri.polygon_index].normal) if not tri.use_smooth else None
            slot = ev.material_slots[tri.material_index].material if tri.material_index < len(ev.material_slots) else None
            base = len(verts)
            for li in tri.loops:
                v = me.vertices[me.loops[li].vertex_index]
                verts.append((conv_vec(v.co), n if n else conv_vec(me.loops[li].normal)))
            subs.setdefault(mat_id(slot), []).extend((base, base + 2, base + 1))  # flipped winding
        ev.to_mesh_clear()
        body.pack("B", 1)
        body.pack("i", len(verts))
        for p, n in verts:
            body.pack("6f", *p, *n)
        body.pack("B", len(subs))
        for m, idx in sorted(subs.items()):
            body.pack("Hi", m, len(idx))
            body.pack(f"{len(idx)}i", *idx)
        tris_total += len(verts) // 3

    out = W()
    out.b += b"MH6M"
    out.pack("H", VERSION)
    out.pack("H", len(materials))
    for name, rgba in materials:
        out.string(name)
        out.pack("4f", *rgba)
    out.b += body.b

    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f:
        f.write(out.b)
    print(f"Exported {len(nodes)} nodes, {len(materials)} materials, {tris_total} triangles, {len(out.b)} bytes -> {path}")


export(bpy.path.abspath("//../Resources/mh6.bin"))
