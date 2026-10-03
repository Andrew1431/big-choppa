using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BigChoppa;

// The airframe: a Rigidbody pushed by a deliberately simple model.
// Lift always points out the top of the rotor, so tilting is how you move (no "strafe up/down" shortcuts).
public class Helicopter : MonoBehaviour
{
    public Helicopter(IntPtr ptr) : base(ptr) { }

    public const int SeatCount = 3; // 0 = pilot, 1-2 = rear bench

    public uint Id;
    public uint Owner;      // player netId whose PC simulates this choppa
    public bool IsProxy;    // true = someone else simulates it; we just replay their snapshots
    public uint[] Occupants = new uint[SeatCount];

    public Rigidbody Body;
    public AudioSource RotorAudio, TurbineAudio;
    public Transform MainRotor, TailRotor;
    public Transform[] Seats, Exits;
    public Transform PilotSeat => Seats[0];
    public Transform[] Pupils;
    public Vector3[] PupilRest;
    internal ChoppaLights Lights;

    public bool EngineOn;
    public float Collective;   // 0..1
    public float RotorSpin;    // 0..1, lift scales with this
    public Vector3 CommandedRates; // deg/s: x = pitch (+ nose down), y = yaw (+ right), z = roll (+ left)

    public bool Grounded;
    public bool Broken;
    public Vector3 ImpactVelocity; // velocity just before the crash

    // Raised from FixedUpdate when a hard impact should break the choppa apart. Args: heli, impact speed change.
    public static event Action<Helicopter, float> Crashed;

    Vector3 lastVelocity, smoothedAccel;
    float impactGraceUntil;

    public float HoverCollective => 1f / Mathf.Max(0.01f, ChoppaConfig.MaxLiftG.Value);

    public static Helicopter Build(uint id, Vector3 position, Quaternion rotation, int layer, Scene scene)
    {
        var root = new GameObject($"BigChoppa {id:X8}");
        if (scene.IsValid() && scene.isLoaded) SceneManager.MoveGameObjectToScene(root, scene);
        root.transform.SetPositionAndRotation(position, rotation);

        var body = root.AddComponent<Rigidbody>();
        body.mass = ChoppaConfig.Mass.Value;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.angularDamping = 0.5f;
        body.linearDamping = 0f;

        var heli = root.AddComponent<Helicopter>();
        heli.Id = id;
        heli.Body = body;
        heli.impactGraceUntil = Time.time + 1.5f;
        HeliModel.Build(heli, ChoppaConfig.HeliScale.Value);
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        ChoppaPhysics.CollideWithEverything(root);
        ChoppaAudio.Attach(heli);
        ChoppaBonker.Add(root, 0.9f, 2.5f);

        // Pivot about halfway between skids and rotor so mouse pitch/roll turns it around its middle.
        body.automaticCenterOfMass = false;
        body.centerOfMass = new Vector3(0f, ChoppaConfig.CenterOfMassHeight.Value, 0.1f) * ChoppaConfig.HeliScale.Value;
        return heli;
    }

    void FixedUpdate()
    {
        if (Body == null || Broken) return;
        float dt = Time.fixedDeltaTime;
        if (IsProxy) { ProxyStep(dt); return; }

        // Velocity here is the result of last physics step, so a big jump means we just hit something hard.
        Vector3 vNow = Body.linearVelocity;
        float deltaV = (vNow - lastVelocity).magnitude;
        if (ChoppaConfig.BreakApart.Value && Time.time > impactGraceUntil && deltaV > ChoppaConfig.CrashDeltaV.Value)
        {
            Broken = true;
            ImpactVelocity = lastVelocity;
            Plugin.L.LogInfo($"CRASH! Speed change {deltaV:0.0} m/s in one physics step.");
            Crashed?.Invoke(this, deltaV);
            return;
        }

        Grounded = CheckGrounded();

        RotorSpin = Mathf.MoveTowards(RotorSpin, EngineOn ? 1f : 0f, dt / Mathf.Max(0.1f, ChoppaConfig.SpinUpSeconds.Value));
        if (!EngineOn) Collective = Mathf.MoveTowards(Collective, 0f, dt * 0.5f);

        float g = Mathf.Abs(Physics.gravity.y);
        float lift = Collective * ChoppaConfig.MaxLiftG.Value * g * RotorSpin * RotorSpin;
        Body.AddForce(transform.up * lift, ForceMode.Acceleration);

        Vector3 lv = transform.InverseTransformDirection(Body.linearVelocity);
        float lin = ChoppaConfig.LinearDrag.Value;
        var drag = new Vector3(
            -lv.x * (lin + ChoppaConfig.SideDrag.Value * Mathf.Abs(lv.x)),
            -lv.y * (lin + ChoppaConfig.VerticalDrag.Value * Mathf.Abs(lv.y)),
            -lv.z * (lin + ChoppaConfig.ForwardDrag.Value * Mathf.Abs(lv.z)));
        Body.AddForce(transform.TransformDirection(drag), ForceMode.Acceleration);

        if (RotorSpin > 0.05f)
        {
            Vector3 target = CommandedRates * Mathf.Deg2Rad;

            float autoLevel = ChoppaConfig.AutoLevel.Value;
            if (autoLevel > 0f)
            {
                Vector3 tiltAxis = transform.InverseTransformDirection(Vector3.Cross(transform.up, Vector3.up));
                target.x += tiltAxis.x * autoLevel * 3f;
                target.z += tiltAxis.z * autoLevel * 3f;
            }

            // Tail fin catching sideways air: positive lv.x (sliding right) yaws the nose right.
            float speedFactor = Mathf.Clamp01(new Vector2(lv.x, lv.z).magnitude / 15f);
            target.y += Mathf.Clamp(lv.x * 0.1f, -1f, 1f) * ChoppaConfig.Weathervane.Value * speedFactor;

            Vector3 localAng = transform.InverseTransformDirection(Body.angularVelocity);
            Vector3 accel = (target - localAng) * ChoppaConfig.ControlResponse.Value * RotorSpin;
            Body.AddTorque(transform.TransformDirection(accel), ForceMode.Acceleration);
        }

        Vector3 v = Body.linearVelocity;
        smoothedAccel = Vector3.Lerp(smoothedAccel, (v - lastVelocity) / dt, 0.2f);
        lastVelocity = v;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        ChoppaAudio.Drive(this);
        if (MainRotor != null) MainRotor.Rotate(0f, RotorSpin * 900f * dt, 0f, Space.Self);
        if (TailRotor != null) TailRotor.Rotate(RotorSpin * 1600f * dt, 0f, 0f, Space.Self);
        // Proxies don't know EngineOn, but their synced rotor spin says whether someone's flying.
        Lights?.Drive(EngineOn || RotorSpin > 0.1f);

        // Googly eyes slosh against acceleration.
        if (Pupils != null)
        {
            Vector3 slosh = transform.InverseTransformDirection(-smoothedAccel) * 0.004f;
            slosh.z = 0f;
            slosh = Vector3.ClampMagnitude(slosh, 0.07f);
            for (int i = 0; i < Pupils.Length; i++)
                if (Pupils[i] != null) Pupils[i].localPosition = PupilRest[i] + slosh;
        }
    }

    bool CheckGrounded()
    {
        float s = ChoppaConfig.HeliScale.Value;
        Vector3 origin = transform.position + transform.up * (0.5f * s);
        foreach (var h in Physics.RaycastAll(origin, -transform.up, 0.8f * s, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.collider == null || h.collider.transform.IsChildOf(transform)) continue;
            if (h.collider.attachedRigidbody != null && h.collider.attachedRigidbody.isKinematic && h.collider.GetComponentInParent<PlayerCharacter>() != null) continue;
            return true;
        }
        return false;
    }

    // ---------- networking ----------

    struct Snap
    {
        public double T;
        public Vector3 Pos, Vel, AngVel;
        public Quaternion Rot;
        public float Spin, Collective;
    }

    readonly List<Snap> snaps = new();

    public void SetProxy(bool proxy)
    {
        if (proxy == IsProxy) return;
        IsProxy = proxy;
        if (proxy)
        {
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            Body.isKinematic = true;
            snaps.Clear();
            snaps.Add(new Snap { T = ChoppaNet.Time - ChoppaConfig.NetInterpDelay.Value, Pos = Body.position, Rot = Body.rotation, Spin = RotorSpin, Collective = Collective });
        }
        else
        {
            Body.isKinematic = false;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            if (snaps.Count > 0)
            {
                var last = snaps[snaps.Count - 1];
                Body.linearVelocity = last.Vel;
                Body.angularVelocity = last.AngVel;
            }
            snaps.Clear();
            ClearImpactHistory();
        }
    }

    public void WriteState(System.IO.BinaryWriter w)
    {
        w.Write(Id);
        w.Write(ChoppaNet.Time);
        w.Write(Body.position);
        w.Write(Body.rotation);
        w.Write(Body.linearVelocity);
        w.Write(Body.angularVelocity);
        w.Write((byte)Mathf.RoundToInt(RotorSpin * 255f));
        w.Write((byte)Mathf.RoundToInt(Collective * 255f));
    }

    // Reads everything after the id.
    public void ReadState(System.IO.BinaryReader r)
    {
        var s = new Snap
        {
            T = r.ReadDouble(),
            Pos = r.ReadVector3(),
            Rot = r.ReadQuaternion(),
            Vel = r.ReadVector3(),
            AngVel = r.ReadVector3(),
            Spin = r.ReadByte() / 255f,
            Collective = r.ReadByte() / 255f,
        };
        if (!IsProxy) return;

        int i = snaps.Count;
        while (i > 0 && snaps[i - 1].T > s.T) i--;
        if (i > 0 && snaps[i - 1].T == s.T) return;
        snaps.Insert(i, s);
        while (snaps.Count > 32) snaps.RemoveAt(0);
    }

    // Snapshot interpolation: show where the owner had it InterpolationDelay seconds ago, extrapolating
    // briefly with the last known velocity if updates run late.
    void ProxyStep(float dt)
    {
        if (snaps.Count == 0) return;
        double t = ChoppaNet.Time - ChoppaConfig.NetInterpDelay.Value;

        while (snaps.Count > 2 && snaps[1].T <= t) snaps.RemoveAt(0);

        Snap a = snaps[0];
        Vector3 pos; Quaternion rot; Vector3 vel;
        if (snaps.Count >= 2 && t >= a.T)
        {
            Snap b = snaps[1];
            float f = (float)((t - a.T) / Math.Max(1e-4, b.T - a.T));
            if (f <= 1f)
            {
                pos = Vector3.LerpUnclamped(a.Pos, b.Pos, f);
                rot = Quaternion.Slerp(a.Rot, b.Rot, f);
                vel = Vector3.Lerp(a.Vel, b.Vel, f);
                RotorSpin = Mathf.Lerp(a.Spin, b.Spin, f);
                Collective = Mathf.Lerp(a.Collective, b.Collective, f);
            }
            else Extrapolate(b, t, out pos, out rot, out vel);
        }
        else if (t >= a.T) Extrapolate(a, t, out pos, out rot, out vel);
        else { pos = a.Pos; rot = a.Rot; vel = a.Vel; RotorSpin = a.Spin; Collective = a.Collective; }

        Body.MovePosition(pos);
        Body.MoveRotation(rot);

        smoothedAccel = Vector3.Lerp(smoothedAccel, (vel - lastVelocity) / dt, 0.2f);
        lastVelocity = vel;
    }

    void Extrapolate(Snap s, double t, out Vector3 pos, out Quaternion rot, out Vector3 vel)
    {
        float ahead = Mathf.Min((float)(t - s.T), 0.25f);
        pos = s.Pos + s.Vel * ahead;
        rot = Quaternion.AngleAxis(s.AngVel.magnitude * Mathf.Rad2Deg * ahead, s.AngVel.sqrMagnitude > 1e-8f ? s.AngVel.normalized : Vector3.up) * s.Rot;
        vel = s.Vel;
        RotorSpin = s.Spin;
        Collective = s.Collective;
    }

    public Vector3 Velocity => IsProxy ? lastVelocity : Body.linearVelocity;

    // Call after teleporting so the sudden velocity change isn't mistaken for a crash.
    public void ClearImpactHistory()
    {
        lastVelocity = Body.linearVelocity;
        smoothedAccel = Vector3.zero;
        impactGraceUntil = Time.time + 1f;
    }

    public void ResetUpright()
    {
        var fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.ProjectOnPlane(transform.up, Vector3.up);
        Body.linearVelocity = Vector3.zero;
        Body.angularVelocity = Vector3.zero;
        Body.position = Body.position + Vector3.up * 1.5f;
        Body.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);
        transform.SetPositionAndRotation(Body.position, Body.rotation);
        ClearImpactHistory();
    }
}
