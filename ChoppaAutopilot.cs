using UnityEngine;

namespace BigChoppa;

// Flies an empty choppa in to a landing spot using the same inputs a pilot has (collective, pitch/yaw/roll rates),
// so the normal flight model, crash detection and network sync all still apply.
public sealed class ChoppaAutopilot
{
    const float MaxSpeed = 40f, MaxTiltDeg = 35f, MaxAccel = 9f, TimeoutSeconds = 75f;

    readonly Vector3 target;
    readonly float cruiseY;
    readonly float startTime;
    Vector3 heading;
    bool descending;

    public ChoppaAutopilot(Helicopter h, Vector3 target, float cruiseY)
    {
        this.target = target;
        this.cruiseY = cruiseY;
        startTime = Time.time;
        heading = Flat(target - h.transform.position).normalized;
        h.EngineOn = true;
        h.RotorSpin = 1f;
        h.Collective = h.HoverCollective;
        // Arrive already cruising, so it doesn't sag while tipping its nose down to get going.
        h.Body.linearVelocity = heading * 20f;
    }

    // Returns false once it's done (landed, gave up, or someone took the pilot seat).
    public bool Step(Helicopter h, float dt)
    {
        if (h.Occupants[0] != 0) return false;
        if (Time.time - startTime > TimeoutSeconds) { Finish(h, "gave up"); return false; }
        h.EngineOn = true; // a Seats update for the empty choppa would otherwise switch it off mid-air

        var t = h.transform;
        Vector3 p = t.position, v = h.Body.linearVelocity;
        Vector3 toTarget = Flat(target - p);
        float dist = toTarget.magnitude;
        Vector3 vFlat = Flat(v);

        if (!descending && dist < 5f && vFlat.magnitude < 3f) { descending = true; Plugin.Verbose("Autopilot: over the spot, descending."); }
        if (descending && h.Grounded) { Finish(h, "landed"); return false; }

        // Horizontal: desired velocity slows down near the spot; lean to get there.
        Vector3 wantVel = dist > 0.01f ? toTarget / dist * Mathf.Min(MaxSpeed, dist * 0.5f) : Vector3.zero;
        Vector3 accel = Vector3.ClampMagnitude((wantVel - vFlat) * 1.3f, MaxAccel);
        Vector3 fwd = Flat(t.forward).normalized, right = Flat(t.right).normalized;
        float g = Mathf.Abs(Physics.gravity.y);
        float wantPitch = Mathf.Clamp(Mathf.Atan2(Vector3.Dot(accel, fwd), g) * Mathf.Rad2Deg, -MaxTiltDeg, MaxTiltDeg);
        float wantRoll = Mathf.Clamp(-Mathf.Atan2(Vector3.Dot(accel, right), g) * Mathf.Rad2Deg, -MaxTiltDeg, MaxTiltDeg);
        float pitchNow = -Mathf.Asin(Mathf.Clamp(t.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
        float rollNow = Mathf.Asin(Mathf.Clamp(t.right.y, -1f, 1f)) * Mathf.Rad2Deg;

        // Nose into the direction of travel until the final approach, then hold that heading.
        if (dist > 25f) heading = toTarget / dist;
        float yawErr = Vector3.SignedAngle(fwd, heading, Vector3.up);

        h.CommandedRates = new Vector3(
            Mathf.Clamp((wantPitch - pitchNow) * 3f, -60f, 60f),
            Mathf.Clamp(yawErr * 1.2f, -40f, 40f),
            Mathf.Clamp((wantRoll - rollNow) * 3f, -60f, 60f));

        // Vertical: cruise high, glide down on approach, then settle gently onto the spot.
        float wantVs;
        if (descending)
        {
            float above = p.y - target.y;
            wantVs = -Mathf.Clamp(above * 0.6f, 1f, 8f);
        }
        else
        {
            // Stay at cruise height until close, then come down, but never lower than the ground still ahead.
            float wantY = Mathf.Min(cruiseY, target.y + 6f + Mathf.Max(0f, dist - 35f) * 0.8f);
            wantY = Mathf.Max(wantY, GroundAhead(h, p, toTarget, dist) + 12f);
            wantVs = Mathf.Clamp((wantY - p.y) * 0.8f, -10f, 8f);
        }
        float needAccel = g + (wantVs - v.y) * 1.5f;
        float upY = Mathf.Max(0.5f, t.up.y);
        h.Collective = Mathf.Clamp01(needAccel / (ChoppaConfig.MaxLiftG.Value * g * upY));
        return true;
    }

    void Finish(Helicopter h, string why)
    {
        Plugin.L.LogInfo($"Autopilot: {why} {Flat(target - h.transform.position).magnitude:0.0}m from the spot after {Time.time - startTime:0}s.");
        h.CommandedRates = Vector3.zero;
        h.EngineOn = h.Occupants[0] != 0;
    }

    // Highest ground between here and the spot over the next 80 m (stopping short of the spot itself).
    static float GroundAhead(Helicopter h, Vector3 p, Vector3 toTarget, float dist)
    {
        float top = float.MinValue;
        if (dist < 8f) return top;
        Vector3 dir = toTarget / dist;
        float reach = Mathf.Min(80f, dist - 6f);
        for (float d = 0f; d <= reach; d += 10f)
        {
            Vector3 origin = p + dir * d + Vector3.up * 200f;
            foreach (var hit in Physics.RaycastAll(origin, Vector3.down, 400f, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider != null && !hit.collider.transform.IsChildOf(h.transform) && hit.collider.attachedRigidbody == null)
                    top = Mathf.Max(top, hit.point.y);
        }
        return top;
    }

    static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);
}
