using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace BigChoppa;

// Turntable thumbnails for the chooser. Each vehicle is built the normal way on an inactive dummy, then flattened
// into one mesh with its lighting baked into flat unlit colours (faceted, like the model), so the picture looks the
// same at noon or midnight and doesn't depend on the scene's lights or sky. Rendered by our own camera far above the
// world into a RenderTexture.
internal sealed class ChoppaPreview
{
    // Default layer: the game's URP renderer may filter out unused high layers (31 drew nothing). The stage is 30 km
    // up, past every other camera's far plane, so nothing else ever sees it.
    const int Layer = 0;
    // Each preview gets its own stage 1 km apart, or every camera would see every model.
    static int stages;
    readonly Vector3 Stage = new(1000f * stages++, 30000f, 0f);
    static readonly Vector3 ViewDir = new Vector3(0.95f, 0.42f, 0.85f).normalized; // front-right, from above
    static readonly Vector3 KeyLight = new Vector3(0.35f, 1f, 0.55f).normalized;

    public RenderTexture Texture { get; private set; }
    public Vector2 ShadowAt { get; private set; } = new(0.5f, 0.8f); // viewport point under the model, y down
    public float ShadowWidth { get; private set; } = 0.6f;
    public bool Failed { get; private set; }
    public bool Alive => !Failed && root != null && cam != null && Texture != null;

    GameObject root;
    Camera cam;
    Bounds bounds;
    float distance;

    public static ChoppaPreview Create(Vehicle v, int width, int height)
    {
        var p = new ChoppaPreview();
        try { p.Build(v, width, height); }
        catch (Exception e)
        {
            Plugin.L.LogWarning($"Choppa preview for {v} failed: {e}");
            p.Failed = true;
            p.Dispose();
        }
        return p;
    }

    void Build(Vehicle v, int width, int height)
    {
        // The normal model build, on an object that never wakes up (no Awake/Update, no tickets, no lights on).
        var dummy = new GameObject("BigChoppa Preview Source");
        dummy.SetActive(false);
        try
        {
            var heli = dummy.AddComponent<Helicopter>();
            heli.Vehicle = v;
            heli.Scale = 1f;
            Vehicles.Build(heli);
            root = new GameObject($"BigChoppa Preview {v}") { layer = Layer, hideFlags = HideFlags.HideAndDontSave };
            root.transform.position = Stage;
            Bake(dummy.transform, root.transform);
        }
        finally { UnityEngine.Object.DestroyImmediate(dummy); }

        Texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
        {
            antiAliasing = 8,
            hideFlags = HideFlags.HideAndDontSave,
            name = $"BigChoppa Preview {v}",
        };
        Texture.Create();

        var camGo = new GameObject("BigChoppa Preview Camera") { hideFlags = HideFlags.HideAndDontSave };
        cam = camGo.AddComponent<Camera>();
        cam.enabled = false;
        cam.cullingMask = 1 << Layer;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 1f / 255f); // invisible, but tells Diagnose the camera ran
        cam.fieldOfView = 24f;
        cam.allowHDR = false;
        cam.allowMSAA = true;
        cam.useOcclusionCulling = false;
        cam.targetTexture = Texture;
        try { cam.cameraType = CameraType.Preview; } catch { }

        cam.depth = -100f;
        Frame(0f);
    }

    // While live, the camera is enabled and URP renders it into the texture every frame (a manual Camera.Render
    // draws nothing under URP in the game build). The model sits 30 km up, beyond every other camera's far plane.
    public void SetLive(bool live)
    {
        if (cam == null || root == null || Texture == null) return;
        if (!Texture.IsCreated()) Texture.Create();
        root.SetActive(live);
        cam.enabled = live;
    }

    // Spin around the model (deg).
    public void Turn(float yaw)
    {
        if (cam != null && root != null) Place(yaw);
    }

#if DEVBUILD
    // Dev: how much of the texture actually got drawn (all transparent = the camera isn't rendering).
    public void Diagnose(Vehicle v)
    {
        if (Texture == null) return;
        var prev = RenderTexture.active;
        try
        {
            RenderTexture.active = Texture;
            var t = new Texture2D(Texture.width, Texture.height, TextureFormat.RGBA32, false);
            t.ReadPixels(new Rect(0, 0, Texture.width, Texture.height), 0, 0);
            t.Apply();
            var px = t.GetPixels();
            int cleared = 0, solid = 0, coloured = 0;
            foreach (var c in px) { if (c.a > 0f) cleared++; if (c.a > 0.5f) solid++; if (c.r + c.g + c.b > 0.05f) coloured++; }
            var main = Camera.main;
            Plugin.L.LogInfo($"Preview (dev) {v}: {cleared} cleared (camera ran if > 0), {solid}/{px.Length} opaque, {coloured} coloured; " +
                             $"cam enabled {cam?.enabled}, root active {root?.activeSelf}, bounds {bounds}, dist {distance:0.0}; " +
                             $"main cam mask {(main != null ? main.cullingMask.ToString("X8") : "none")}.");
            UnityEngine.Object.Destroy(t);
        }
        catch (Exception e) { Plugin.L.LogWarning($"Preview (dev) diagnose: {e.Message}"); }
        finally { RenderTexture.active = prev; }
    }
#endif

    void Place(float yaw)
    {
        var dir = Quaternion.AngleAxis(yaw, Vector3.up) * ViewDir;
        var c = Stage + bounds.center;
        cam.transform.position = c + dir * distance;
        cam.transform.LookAt(c, Vector3.up);
        cam.nearClipPlane = Mathf.Max(0.05f, distance - bounds.extents.magnitude * 2f);
        cam.farClipPlane = distance + bounds.extents.magnitude * 2f;
    }

    // Distance that fills ~84% of the frame for the widest yaw, so turning never clips the model.
    void Frame(float yaw)
    {
        distance = bounds.extents.magnitude * 3f;
        cam.aspect = Texture.width / (float)Texture.height;
        float worst = 0f;
        for (int iter = 0; iter < 3; iter++)
        {
            worst = 0f;
            for (int a = 0; a < 360; a += 30)
            {
                Place(a);
                worst = Mathf.Max(worst, Extent(out _, out _));
            }
            distance *= worst / 0.84f;
        }
        Place(yaw);
        Extent(out var bottom, out var width);
        ShadowAt = bottom;
        ShadowWidth = width;
    }

    // Largest viewport half-extent of the bounds corners (1 = touching the frame edge).
    float Extent(out Vector2 bottomCentre, out float width)
    {
        float max = 0f, minX = 1f, maxX = 0f, maxY = 0f;
        var e = bounds.extents;
        for (int i = 0; i < 8; i++)
        {
            var p = Stage + bounds.center + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
            var vp = cam.WorldToViewportPoint(p);
            max = Mathf.Max(max, Mathf.Abs(vp.x - 0.5f) * 2f, Mathf.Abs(vp.y - 0.5f) * 2f);
            minX = Mathf.Min(minX, vp.x); maxX = Mathf.Max(maxX, vp.x);
        }
        var b = cam.WorldToViewportPoint(Stage + bounds.center - new Vector3(0f, e.y, 0f));
        maxY = 1f - b.y;
        bottomCentre = new Vector2(b.x, maxY);
        width = maxX - minX;
        return max;
    }

    // Flatten every visible mesh under `src` into one mesh under `dst`, one submesh per baked colour.
    void Bake(Transform src, Transform dst)
    {
        var groups = new Dictionary<(int r, int g, int b, int a), (List<Vector3> v, List<int> i)>();
        var toRoot = src.worldToLocalMatrix;
        bool first = true;

        foreach (var mr in src.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (!VisibleUnder(mr.transform, src)) continue;
            var mf = mr.GetComponent<MeshFilter>();
            var mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null) continue;
            try
            {
                var m = toRoot * mr.transform.localToWorldMatrix;
                var verts = mesh.vertices;
                var mats = mr.sharedMaterials;
                for (int sm = 0; sm < mesh.subMeshCount; sm++)
                {
                    var mat = sm < mats.Length ? mats[sm] : mats.Length > 0 ? mats[mats.Length - 1] : null;
                    var baseColor = ColorOf(mat);
                    var tris = mesh.GetTriangles(sm);
                    for (int t = 0; t + 2 < tris.Length; t += 3)
                    {
                        var a = m.MultiplyPoint3x4(verts[tris[t]]);
                        var b = m.MultiplyPoint3x4(verts[tris[t + 1]]);
                        var c = m.MultiplyPoint3x4(verts[tris[t + 2]]);
                        var n = Vector3.Cross(b - a, c - a);
                        if (n.sqrMagnitude < 1e-12f) continue;
                        n.Normalize();
                        float lambert = Mathf.Max(0f, Vector3.Dot(n, KeyLight));
                        float sky = 0.5f + 0.5f * n.y;
                        float shade = 0.38f + 0.62f * lambert + 0.12f * sky;
                        var col = baseColor * shade;
                        col.a = baseColor.a;
                        var key = (Q(col.r), Q(col.g), Q(col.b), baseColor.a < 0.99f ? Mathf.RoundToInt(baseColor.a * 20f) : 20);
                        if (!groups.TryGetValue(key, out var g)) groups[key] = g = (new List<Vector3>(), new List<int>());
                        g.i.Add(g.v.Count); g.v.Add(a);
                        g.i.Add(g.v.Count); g.v.Add(b);
                        g.i.Add(g.v.Count); g.v.Add(c);
                        if (first) { bounds = new Bounds(a, Vector3.zero); first = false; }
                        bounds.Encapsulate(a); bounds.Encapsulate(b); bounds.Encapsulate(c);
                    }
                }
            }
            catch (Exception e) { Plugin.Verbose($"Preview skipped '{mr.name}': {e.Message}"); }
        }
        if (first) throw new Exception("nothing to draw");

        var all = new List<Vector3>();
        var subs = new List<int[]>();
        var colors = new List<Color>();
        foreach (var kv in groups)
        {
            int start = all.Count;
            all.AddRange(kv.Value.v);
            var idx = kv.Value.i.ToArray();
            for (int k = 0; k < idx.Length; k++) idx[k] += start;
            subs.Add(idx);
            colors.Add(new Color(kv.Key.r / 255f, kv.Key.g / 255f, kv.Key.b / 255f, kv.Key.a / 20f));
        }

        var mesh2 = new Mesh { name = "BigChoppa Preview", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh2.hideFlags = HideFlags.HideAndDontSave;
        mesh2.vertices = all.ToArray();
        mesh2.subMeshCount = subs.Count;
        var mats2 = new Il2CppReferenceArray<Material>(subs.Count);
        for (int s = 0; s < subs.Count; s++)
        {
            mesh2.SetTriangles((Il2CppStructArray<int>)subs[s], s);
            mats2[s] = ChoppaMaterials.GetFlat(colors[s]);
        }
        mesh2.RecalculateBounds();

        var go = new GameObject("Mesh") { layer = Layer, hideFlags = HideFlags.HideAndDontSave };
        go.transform.SetParent(dst, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh2;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterials = mats2;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        root.SetActive(false);
    }

    static int Q(float c) => Mathf.Clamp(Mathf.RoundToInt(c * 48f), 0, 48) * 255 / 48;

    static Color ColorOf(Material mat)
    {
        if (mat == null) return Color.grey;
        var c = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : mat.color;
        // Lamp lenses are boosted for glow; keep them in range.
        float max = Mathf.Max(c.r, c.g, c.b);
        if (max > 1f) { c.r /= max; c.g /= max; c.b /= max; }
        return c;
    }

    static bool VisibleUnder(Transform t, Transform root)
    {
        for (var p = t; p != null && p != root; p = p.parent)
            if (!p.gameObject.activeSelf) return false;
        return true;
    }

    public void Dispose()
    {
        if (root != null) UnityEngine.Object.Destroy(root);
        if (cam != null) UnityEngine.Object.Destroy(cam.gameObject);
        if (Texture != null) { Texture.Release(); UnityEngine.Object.Destroy(Texture); }
        root = null; cam = null; Texture = null;
    }
}
