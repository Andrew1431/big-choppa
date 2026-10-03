using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BigChoppa;

// The airframe: a Rigidbody pushed by a deliberately simple model.
// Lift always points out the top of the rotor, so tilting is how you move (no "strafe up/down" shortcuts).
public class Helicopter : MonoBehaviour
{
    public Helicopter(IntPtr ptr) : base(ptr) { }

    public Rigidbody Body;
    public Transform MainRotor, TailRotor, PilotSeat, ExitPoint;
    public Transform[] Pupils;
    public Vector3[] PupilRest;

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

    public static Helicopter Build(Vector3 position, Quaternion rotation, int layer, Scene scene)
    {
        var root = new GameObject("BigChoppa");
        if (scene.IsValid() && scene.isLoaded) SceneManager.MoveGameObjectToScene(root, scene);
        root.transform.SetPositionAndRotation(position, rotation);

        var body = root.AddComponent<Rigidbody>();
        body.mass = ChoppaConfig.Mass.Value;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.angularDamping = 0.5f;
        body.linearDamping = 0f;

        var heli = root.AddComponent<Helicopter>();
        heli.Body = body;
        heli.impactGraceUntil = Time.time + 1.5f;
        HeliModel.Build(heli, ChoppaConfig.HeliScale.Value);
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        ChoppaPhysics.CollideWithEverything(root);

        // Low centre of mass so it sits on its skids instead of toppling.
        body.automaticCenterOfMass = false;
        body.centerOfMass = new Vector3(0f, 0.6f, 0f) * ChoppaConfig.HeliScale.Value;
        return heli;
    }

    void FixedUpdate()
    {
        if (Body == null || Broken) return;
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
        if (MainRotor != null) MainRotor.Rotate(0f, RotorSpin * 900f * dt, 0f, Space.Self);
        if (TailRotor != null) TailRotor.Rotate(RotorSpin * 1600f * dt, 0f, 0f, Space.Self);

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
