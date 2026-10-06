using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BigChoppa;

// The airframe: a Rigidbody pushed by a deliberately simple model.
// Lift always points out the top of the rotor, so tilting is how you move (no "strafe up/down" shortcuts).
public class Helicopter : MonoBehaviour
{
    public Helicopter(IntPtr ptr) : base(ptr) { }

    public uint Id;
    public Vehicle Vehicle;
    public float Scale;     // Model scale: the config's Scale times the vehicle's own factor
    public uint Owner;      // player netId whose PC simulates this choppa
    public bool IsProxy;    // true = someone else simulates it; we just replay their snapshots
    public uint[] Occupants; // seat 0 = pilot; sized by the vehicle's seat count (the host agrees)

    public Rigidbody Body;
    public AudioSource RotorAudio, TurbineAudio;
    public Transform MainRotor, TailRotor;
    public Transform[] Seats, Exits; // may be shorter than Occupants if a model failed to build
    public Transform PilotSeat => Seats[0];
    public Transform[] Pupils;
    public Vector3[] PupilRest;
    internal ChoppaLights Lights;
    internal ChoppaPockets Pockets;
    internal ChoppaBenches Benches; // Little Bird only

    public bool EngineOn;
    public float Collective;   // 0..1
    public float RotorSpin;    // 0..1, lift scales with this
    public Vector3 CommandedRates; // deg/s: x = pitch (+ nose down), y = yaw (+ right), z = roll (+ left)
    public ChoppaAutopilot Autopilot; // flying itself in to land (owner only)

    public bool Grounded;
    public bool Broken;
    public Vector3 ImpactVelocity; // velocity just before the crash

    // Raised from FixedUpdate when a hard impact should break the choppa apart. Args: heli, impact speed change.
    public static event Action<Helicopter, float> Crashed;

    Vector3 lastVelocity, smoothedAccel;
    float impactGraceUntil;

    public float HoverCollective => 1f / Mathf.Max(0.01f, ChoppaConfig.MaxLiftG.Value);

    public static Helicopter Build(uint id, Vehicle vehicle, Vector3 position, Quaternion rotation, int layer, Scene scene)
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
        heli.Vehicle = vehicle;
        heli.Scale = Vehicles.Scale(vehicle);
        heli.Occupants = new uint[Vehicles.SeatCount(vehicle)];
        heli.Body = body;
        heli.impactGraceUntil = Time.time + 1.5f;
        Vehicles.Build(heli);
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        ChoppaPhysics.CollideWithEverything(root);
        heli.Pockets?.Activate();
        heli.Benches?.Activate();
        ChoppaAudio.Attach(heli);
        ChoppaBonker.Add(root, 0.9f, 2.5f);

        // Pivot about halfway between skids and rotor so mouse pitch/roll turns it around its middle.
        body.automaticCenterOfMass = false;
        body.centerOfMass = new Vector3(0f, ChoppaConfig.CenterOfMassHeight.Value, 0.1f) * heli.Scale;
        return heli;
    }

    void FixedUpdate()
    {
        if (Body == null || Broken || IsProxy) return;
        float dt = Time.fixedDeltaTime;

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
        if (Autopilot != null && !Autopilot.Step(this, dt)) Autopilot = null;

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
            if (autoLevel > 0f && Autopilot == null)
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
        if (IsProxy && !Broken && Body != null) ProxyStep(dt);
        ChoppaAudio.Drive(this);
        if (MainRotor != null) MainRotor.Rotate(0f, RotorSpin * 900f * dt, 0f, Space.Self);
        if (TailRotor != null) TailRotor.Rotate(RotorSpin * 1600f * dt, 0f, 0f, Space.Self);
        // Proxies don't know EngineOn, but their synced rotor spin says whether someone's flying.
        Lights?.Drive(EngineOn || RotorSpin > 0.1f);
        if (!Broken) { Pockets?.Tick(); Benches?.Tick(); }

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
        float s = Scale;
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

    // Proxy playback runs on our own clock, in the owner's time. Snapshots are stamped with the owner's local physics
    // time; offset = (our clock - their stamp), tracked as the lowest recent value so network jitter doesn't move it.
    // playbackTime then eases toward where it should be instead of jumping, so the path is replayed at a steady pace.
    double clockOffset, playbackTime;
    bool hasClock;
    float nextJitterLog;
    int extrapolatedFrames, clockNudges;

    static double LocalClock => Time.timeAsDouble;

    public void SetProxy(bool proxy)
    {
        if (proxy == IsProxy) return;
        IsProxy = proxy;
        if (proxy)
        {
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            Body.isKinematic = true;
            // Posed every rendered frame in Update, so Unity's interpolation would only fight it.
            Body.interpolation = RigidbodyInterpolation.None;
            ResetPlayback();
        }
        else
        {
            Body.isKinematic = false;
            Body.interpolation = RigidbodyInterpolation.Interpolate;
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

    // A new owner stamps with a different clock; start over and hold still until their snapshots arrive.
    public void ResetPlayback()
    {
        snaps.Clear();
        hasClock = false;
    }

    public void WriteState(System.IO.BinaryWriter w)
    {
        w.Write(Id);
        // Body state is the result of the last physics step, so stamp it with that step's time, not the frame's.
        w.Write(Time.fixedTimeAsDouble);
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

        double offset = LocalClock - s.T;
        if (!hasClock || Math.Abs(offset - clockOffset) > 1.0)
        {
            if (hasClock) Plugin.Verbose($"Choppa {Id:X8}: owner clock jumped {offset - clockOffset:0.000}s, restarting playback.");
            snaps.Clear();
            clockOffset = offset;
            playbackTime = s.T - ChoppaConfig.NetInterpDelay.Value;
            hasClock = true;
        }
        // Follow faster packets at once, slower ones only gradually (so a lag spike doesn't shift the timeline).
        else if (offset < clockOffset) clockOffset = offset;
        else clockOffset += (offset - clockOffset) * 0.02;

        int i = snaps.Count;
        while (i > 0 && snaps[i - 1].T > s.T) i--;
        if (i > 0 && snaps[i - 1].T == s.T) return;
        snaps.Insert(i, s);
        while (snaps.Count > 32) snaps.RemoveAt(0);
    }

    // Snapshot interpolation: show where the owner had it InterpolationDelay seconds ago, extrapolating
    // briefly with the last known velocity if updates run late. Runs per rendered frame so the camera riding
    // on it moves in lockstep.
    void ProxyStep(float dt)
    {
        if (snaps.Count == 0 || !hasClock) return;

        double wanted = LocalClock - clockOffset - ChoppaConfig.NetInterpDelay.Value;
        double err = wanted - playbackTime;
        if (Math.Abs(err) > 0.5) playbackTime = wanted;
        else
        {
            if (Math.Abs(err) > 0.01) clockNudges++;
            playbackTime += dt * (1.0 + Math.Clamp(err * 0.5, -0.05, 0.05));
        }
        double t = playbackTime;

        while (snaps.Count > 2 && snaps[1].T <= t) snaps.RemoveAt(0);

        Snap a = snaps[0];
        Vector3 pos; Quaternion rot; Vector3 vel;
        if (snaps.Count >= 2 && t >= a.T)
        {
            Snap b = snaps[1];
            float span = (float)Math.Max(1e-4, b.T - a.T);
            float f = (float)((t - a.T) / span);
            if (f <= 1f)
            {
                // Cubic Hermite through both ends' velocities: the path curves through turns instead of kinking
                // at every snapshot, which a passenger's camera shows as a shudder.
                float f2 = f * f, f3 = f2 * f;
                pos = (2f * f3 - 3f * f2 + 1f) * a.Pos + (f3 - 2f * f2 + f) * span * a.Vel
                    + (-2f * f3 + 3f * f2) * b.Pos + (f3 - f2) * span * b.Vel;
                rot = Quaternion.Slerp(a.Rot, b.Rot, f);
                vel = Vector3.Lerp(a.Vel, b.Vel, f);
                RotorSpin = Mathf.Lerp(a.Spin, b.Spin, f);
                Collective = Mathf.Lerp(a.Collective, b.Collective, f);
            }
            else { Extrapolate(b, t, out pos, out rot, out vel); extrapolatedFrames++; }
        }
        else if (t >= a.T) { Extrapolate(a, t, out pos, out rot, out vel); extrapolatedFrames++; }
        else { pos = a.Pos; rot = a.Rot; vel = a.Vel; RotorSpin = a.Spin; Collective = a.Collective; }

        transform.SetPositionAndRotation(pos, rot);
        Body.position = pos;
        Body.rotation = rot;

        if (dt > 1e-5f) smoothedAccel = Vector3.Lerp(smoothedAccel, (vel - lastVelocity) / dt, 0.2f);
        lastVelocity = vel;

        if (ChoppaConfig.VerboseLogging.Value && Time.unscaledTime >= nextJitterLog)
        {
            if (extrapolatedFrames > 0 || clockNudges > 0)
                Plugin.Verbose($"Choppa {Id:X8} playback: {extrapolatedFrames} extrapolated frame(s), {clockNudges} clock correction(s) in 2 s, buffer {snaps.Count}, lag {err * 1000:0} ms.");
            extrapolatedFrames = clockNudges = 0;
            nextJitterLog = Time.unscaledTime + 2f;
        }
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

    // Everyone aboard: mod seats plus anyone sitting on a bench spot.
    public int Riders => Occupants.Count(o => o != 0) + (Benches?.RiderCount ?? 0);

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
