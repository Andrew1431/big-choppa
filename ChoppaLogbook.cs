using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using UnityEngine;

namespace BigChoppa;

// The "Pilot's Logbook": a handful of anonymous flight events sent to PostHog so the author can see how the
// choppa gets used. No Steam ID, username or IP-derived location; just a random install id from the config.
// Fully listed in the README; Logbook.Enabled = false turns it off. Failures are silently dropped.
internal static class ChoppaLogbook
{
    const string Endpoint = "https://us.i.posthog.com/batch/";
    const string ProjectKey = "phc_ubbcXrLXwRGBKUnLChUj6uzMQpCMNrcmjLALTKQMivbg"; // write-only, public by design

    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(10) };
    static readonly List<Dictionary<string, object>> queue = new();
    static float nextFlush;
    static bool sending;

    static bool On => ChoppaConfig.LogbookEnabled.Value;

    public static void Spawned() => Log("choppa_spawned", null);

    public static void Boarded(bool pilot) => Log("choppa_boarded", new() { ["seat"] = pilot ? "pilot" : "passenger" });

    public struct Flight
    {
        public float Seconds, Distance, MaxAltitude, TopSpeedKmh, EndSpeedKmh, UpsideDownSeconds, CockpitPercent;
        public string How; // "landed", "bailed" (pilot hopped out mid-air) or "ended abruptly"
        public int Riders, MaxRiders, Rolls, Loops, PocketItems;
    }

    public static void FlightEnded(Flight f) =>
        Log("flight_ended", new()
        {
            ["duration_s"] = Mathf.Round(f.Seconds),
            ["distance_m"] = Mathf.Round(f.Distance),
            ["max_altitude_m"] = Mathf.Round(f.MaxAltitude),
            ["top_speed_kmh"] = Mathf.Round(f.TopSpeedKmh),
            ["end_speed_kmh"] = Mathf.Round(f.EndSpeedKmh),
            ["how"] = f.How,
            ["riders"] = f.Riders,
            ["max_riders"] = f.MaxRiders,
            ["pocket_items"] = f.PocketItems,
            ["rolls"] = f.Rolls,
            ["loops"] = f.Loops,
            ["upside_down_s"] = Mathf.Round(f.UpsideDownSeconds),
            ["cockpit_view_pct"] = Mathf.Round(f.CockpitPercent),
        });

    // A passenger's ride: "got out", "jumped out" (mid-air) or "ended abruptly".
    public static void RideEnded(float seconds, string how) =>
        Log("ride_ended", new() { ["duration_s"] = Mathf.Round(seconds), ["how"] = how });

    // Logged by whichever game simulates the choppa, so it's counted once even with nobody at the controls.
    public static void EndedAbruptly(float impactKmh, int riders, bool piloted, int pocketItems) =>
        Log("choppa_ended_abruptly", new()
        {
            ["impact_kmh"] = Mathf.Round(impactKmh),
            ["riders"] = riders,
            ["piloted"] = piloted,
            ["pocket_items"] = pocketItems,
        });

    // Something went into ("stowed") or came out of ("taken") a choppa pocket. Item is the game's prop name.
    public static void PocketUsed(bool stowed, string item) =>
        Log("pocket_used", new() { ["action"] = stowed ? "stowed" : "taken", ["item"] = (item ?? "").Replace("(Clone)", "").Trim() });

    public static void FlippedUpright(bool seated) => Log("flipped_upright", new() { ["from"] = seated ? "inside" : "outside" });

    // Anyone (pilot or passenger) leaving while the choppa is off the ground.
    public static void JumpedOut(bool pilot, float height, float speedKmh) =>
        Log("jumped_out", new()
        {
            ["seat"] = pilot ? "pilot" : "passenger",
            ["height_m"] = Mathf.Round(height),
            ["speed_kmh"] = Mathf.Round(speedKmh),
        });

    static void Log(string evt, Dictionary<string, object> props)
    {
        if (!On) return;
        props ??= new();
#if DEVBUILD
        // Dev builds never send, so testing doesn't pollute the real stats.
        Plugin.L.LogInfo($"Logbook (dev, not sent): {evt} {JsonSerializer.Serialize(props)}");
#else
        props["distinct_id"] = InstallId();
        props["mod_version"] = Plugin.PluginVersion;
        props["$process_person_profile"] = false;
        props["$geoip_disable"] = true;
        props["$ip"] = "";
        lock (queue)
        {
            queue.Add(new() { ["event"] = evt, ["properties"] = props, ["timestamp"] = DateTime.UtcNow.ToString("o") });
            if (queue.Count > 200) queue.RemoveAt(0);
        }
        Plugin.Verbose($"Logbook: {evt}");
#endif
    }

    static string InstallId()
    {
        var id = ChoppaConfig.LogbookInstallId.Value;
        if (string.IsNullOrEmpty(id))
        {
            id = Guid.NewGuid().ToString("N");
            ChoppaConfig.LogbookInstallId.Value = id;
        }
        return id;
    }

    // Called every frame; sends at most every 30 s in the background.
    public static void Tick()
    {
        if (Time.unscaledTime < nextFlush) return;
        nextFlush = Time.unscaledTime + 30f;
        var json = TakeBatch();
        if (json == null || sending) return;
        sending = true;
        Task.Run(async () =>
        {
            try { await http.PostAsync(Endpoint, new StringContent(json, Encoding.UTF8, "application/json")); }
            catch { }
            finally { sending = false; }
        });
    }

    // Last chance on quit; short timeout so closing the game never hangs.
    public static void FlushNow()
    {
        var json = TakeBatch();
        if (json == null) return;
        try { http.PostAsync(Endpoint, new StringContent(json, Encoding.UTF8, "application/json")).Wait(TimeSpan.FromSeconds(2)); }
        catch { }
    }

    static string TakeBatch()
    {
        lock (queue)
        {
            if (queue.Count == 0) return null;
            if (!On) { queue.Clear(); return null; }
            var json = JsonSerializer.Serialize(new Dictionary<string, object> { ["api_key"] = ProjectKey, ["batch"] = queue });
            queue.Clear();
            return json;
        }
    }
}
