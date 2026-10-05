#if DEVBUILD
using UnityEngine;

namespace BigChoppa;

// Dev builds only: while the game says we're sprinting, push the player along by (multiplier - 1) times their own
// horizontal speed each physics step. Moves the position rather than the velocity so the game's mover can't undo it.
internal static class DevSprint
{
    public static void FixedTick(PlayerCharacter pc)
    {
        if (pc == null || pc.sprinter == null || !pc.sprinter.isSprinting) return;
        float extra = ChoppaConfig.DevSprintMultiplier.Value - 1f;
        var rb = pc.rb;
        if (extra <= 0f || rb == null) return;
        var v = rb.linearVelocity;
        rb.position += new Vector3(v.x, 0f, v.z) * (extra * Time.fixedDeltaTime);
    }
}
#endif
