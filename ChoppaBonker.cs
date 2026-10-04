using System;
using UnityEngine;

namespace BigChoppa;

// Goes "bonk" when this body hits something. On every debris piece and on the choppa itself (hard landings).
public class ChoppaBonker : MonoBehaviour
{
    public ChoppaBonker(IntPtr ptr) : base(ptr) { }

    public AudioSource Source;
    public float Size = 0.5f;     // 0..1, bigger = deeper
    public float MinSpeed = 1f;
    public float Volume = 1f;     // debris is quieter than the choppa itself
    public float Cooldown = 0.08f;

    Rigidbody body;
    Vector3 lastVelocity;
    bool haveLast;
    float nextAllowed;
    static float windowStart;
    static int windowCount;

    public static ChoppaBonker Add(GameObject go, float size, float minSpeed, float volume = 1f, float cooldown = 0.08f)
    {
        var b = go.AddComponent<ChoppaBonker>();
        b.Size = size;
        b.MinSpeed = minSpeed;
        b.Volume = volume;
        b.Cooldown = cooldown;
        b.body = go.GetComponent<Rigidbody>();
        if (b.body != null) { b.lastVelocity = b.body.linearVelocity; b.haveLast = true; }
        b.Source = ChoppaAudio.OneShotSource(go, 4f + 6f * size);
        return b;
    }

    // FixedUpdate runs before the physics step, OnCollisionEnter after it, so the difference is the hit's speed change.
    void FixedUpdate()
    {
        if (body == null) return;
        lastVelocity = body.linearVelocity;
        haveLast = true;
    }

    // Collision's own getters (relativeVelocity, impulse) are stripped from the game's IL2CPP build, so the
    // collision argument isn't touched. Kinematic bodies (proxy choppas) never bonk.
    void OnCollisionEnter(Collision c)
    {
        if (body == null || body.isKinematic || !haveLast) return;
        float speed = (body.linearVelocity - lastVelocity).magnitude;
        if (speed < MinSpeed || Time.time < nextAllowed) return;
        nextAllowed = Time.time + Cooldown;

        // A whole choppa's worth of debris landing at once would be a wall of noise; cap it at 6 bonks per 0.1 s.
        if (Time.time - windowStart > 0.1f) { windowStart = Time.time; windowCount = 0; }
        if (++windowCount > 6) return;

        ChoppaAudio.Bonk(Source, speed, Size, Volume);
    }
}
