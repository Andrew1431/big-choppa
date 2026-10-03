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

    float nextAllowed;
    static float windowStart;
    static int windowCount;

    public static ChoppaBonker Add(GameObject go, float size, float minSpeed)
    {
        var b = go.AddComponent<ChoppaBonker>();
        b.Size = size;
        b.MinSpeed = minSpeed;
        b.Source = ChoppaAudio.OneShotSource(go, 4f + 6f * size);
        return b;
    }

    void OnCollisionEnter(Collision c)
    {
        float speed = c.relativeVelocity.magnitude;
        if (speed < MinSpeed || Time.time < nextAllowed) return;
        nextAllowed = Time.time + 0.08f;

        // A whole choppa landing at once would be a wall of noise; cap it at a dozen bonks per 0.1 s.
        if (Time.time - windowStart > 0.1f) { windowStart = Time.time; windowCount = 0; }
        if (++windowCount > 12) return;

        ChoppaAudio.Bonk(Source, speed, Size);
    }
}
