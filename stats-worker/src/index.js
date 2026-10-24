// Serves the Pilot's Logbook card (/card.svg) and raw totals (/stats.json) from PostHog.
// A cron refreshes the stats into KV; requests only read KV, so PostHog's speed or outages never reach the card.
import { renderCard } from './card.js';

const CACHE_SECONDS = 300;
const KV_KEY = 'stats/v6';

// Most people aboard at once; older versions only logged the count at the end.
const riders = `toInt(coalesce(properties.max_riders, properties.riders))`;

const QUERY = `
SELECT
  countIf(event = 'flight_ended') AS flights,
  count(DISTINCT if(event = 'flight_ended', distinct_id, NULL)) AS pilots,
  sum(if(event = 'flight_ended', toFloat(properties.distance_m), 0)) AS distance_m,
  sum(if(event = 'flight_ended', toFloat(properties.duration_s), 0)) AS air_s,
  max(if(event = 'flight_ended', toFloat(properties.max_altitude_m), 0)) AS max_altitude_m,
  max(if(event = 'flight_ended', toFloat(properties.top_speed_kmh), 0)) AS top_speed_kmh,
  sum(if(event = 'flight_ended', toFloat(properties.rolls), 0)) AS rolls,
  sum(if(event = 'flight_ended', toFloat(properties.loops), 0)) AS loops,
  countIf(event = 'jumped_out') AS jumped_out,
  max(if(event = 'jumped_out', toFloat(properties.height_m), 0)) AS record_leap_m,
  argMaxIf(toFloat(properties.speed_kmh), toFloat(properties.height_m), event = 'jumped_out') AS record_leap_kmh,
  countIf(event = 'flight_ended' AND properties.how = 'landed') AS landed,
  countIf(event = 'flight_ended' AND properties.how = 'ended abruptly') AS ended_abruptly,
  countIf(event = 'choppa_spawned') AS spawned,
  max(if(event = 'choppa_ended_abruptly', toFloat(properties.impact_kmh), 0)) AS hardest_crash_kmh,
  countIf(event = 'flipped_upright') AS flipped_upright,
  sumIf(toFloat(properties.cockpit_view_pct) * toFloat(properties.duration_s), event = 'flight_ended' AND properties.cockpit_view_pct IS NOT NULL)
    / greatest(1, sumIf(toFloat(properties.duration_s), event = 'flight_ended' AND properties.cockpit_view_pct IS NOT NULL)) AS cockpit_view_pct,
  sum(if(event = 'flight_ended', toFloat(properties.upside_down_s), 0)) AS upside_down_s,
  countIf(event = 'pocket_used' AND properties.action = 'stowed') AS items_pocketed,
  sum(if(event = 'choppa_ended_abruptly', toFloat(properties.pocket_items), 0)) AS items_spilled,
  countIf(event = 'flight_ended' AND ${riders} <= 1) AS solo_flights,
  countIf(event = 'flight_ended' AND ${riders} = 2) AS duo_flights,
  countIf(event = 'flight_ended' AND ${riders} >= 3) AS trio_flights
FROM events
WHERE event IN ('flight_ended', 'jumped_out', 'choppa_spawned', 'pocket_used', 'choppa_ended_abruptly', 'flipped_upright')`;

// Days are UTC to match the card's "updated … UTC" footer.
const DAILY_QUERY = `
SELECT toString(toDate(toTimeZone(timestamp, 'UTC'))) AS day, count() AS flights
FROM events
WHERE event = 'flight_ended' AND timestamp >= now() - INTERVAL 8 DAY
GROUP BY day`;

// Choppas called in, per model. Before 3.0.0 there was only the classic and spawns didn't say which.
const VEHICLE_QUERY = `
SELECT ifNull(nullIf(toString(properties.vehicle), ''), 'classic') AS vehicle, count() AS spawns
FROM events
WHERE event = 'choppa_spawned'
GROUP BY vehicle
ORDER BY spawns DESC`;

// What people stuff in their pockets most.
const ITEM_QUERY = `
SELECT toString(properties.item) AS item, count() AS n
FROM events
WHERE event = 'pocket_used' AND properties.action = 'stowed' AND properties.item != ''
GROUP BY item
ORDER BY n DESC
LIMIT 3`;

async function runQuery(env, name, query) {
  const res = await fetch(`${env.POSTHOG_HOST}/api/projects/${env.POSTHOG_PROJECT_ID}/query/`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${env.POSTHOG_PERSONAL_KEY}`, 'Content-Type': 'application/json' },
    body: JSON.stringify({ query: { kind: 'HogQLQuery', query } }),
    signal: AbortSignal.timeout(30000),
  });
  if (!res.ok) throw new Error(`PostHog ${name} query ${res.status}: ${(await res.text()).slice(0, 300)}`);
  return res.json();
}

// The last 7 UTC days, oldest first, with days nobody flew filled in as 0.
function lastSevenDays(results, now = new Date()) {
  const counts = Object.fromEntries((results ?? []).map(([day, n]) => [day, Number(n) || 0]));
  return Array.from({ length: 7 }, (_, i) => {
    const day = new Date(now.getTime() - (6 - i) * 86_400_000).toISOString().slice(0, 10);
    return { day, flights: counts[day] ?? 0 };
  });
}

async function queryPostHog(env) {
  if (!env.POSTHOG_PERSONAL_KEY) throw new Error('POSTHOG_PERSONAL_KEY secret is not set');
  // One at a time: PostHog limits concurrent queries per project and answers 504 when overloaded.
  const totals = await runQuery(env, 'totals', QUERY);
  const daily = await runQuery(env, 'daily', DAILY_QUERY);
  const vehicles = await runQuery(env, 'vehicle', VEHICLE_QUERY);
  const items = await runQuery(env, 'item', ITEM_QUERY);
  const row = totals.results?.[0] ?? [];
  return {
    ...Object.fromEntries(totals.columns.map((c, i) => [c, Number(row[i]) || 0])),
    daily: lastSevenDays(daily.results),
    vehicles: (vehicles.results ?? []).map(([id, n]) => ({ id: String(id), spawns: Number(n) || 0 })),
    top_items: (items.results ?? []).map(([item, n]) => ({ item: String(item), count: Number(n) || 0 })),
  };
}

async function refreshStats(env) {
  const stats = { ...(await queryPostHog(env)), updated: new Date().toISOString() };
  await env.STATS.put(KV_KEY, JSON.stringify(stats));
  return stats;
}

// Last good stats from KV. Only queries PostHog inline if KV is empty (first deploy, before the cron has run).
async function getStats(env) {
  return (await env.STATS.get(KV_KEY, { type: 'json', cacheTtl: 60 })) ?? refreshStats(env);
}

export default {
  async fetch(request, env, ctx) {
    const { pathname } = new URL(request.url);
    let stats = null, error = null;
    if (pathname === '/card.svg' || pathname === '/stats.json') {
      try { stats = await getStats(env); }
      catch (e) { error = e.message; console.error(error); }
    }

    if (pathname === '/card.svg') {
      const svg = renderCard(stats, { updated: stats ? new Date(stats.updated) : new Date(), error: !!error });
      return new Response(svg, {
        headers: {
          'Content-Type': 'image/svg+xml; charset=utf-8',
          'Cache-Control': error ? 'no-cache' : `public, max-age=${CACHE_SECONDS}`,
        },
      });
    }
    if (pathname === '/stats.json') {
      return Response.json(error ? { error } : stats, {
        status: error ? 502 : 200,
        headers: { 'Cache-Control': error ? 'no-cache' : `public, max-age=${CACHE_SECONDS}`, 'Access-Control-Allow-Origin': '*' },
      });
    }
    return Response.redirect('https://thunderstore.io/c/big-walk/p/Andrew1431/BigChoppa/', 302);
  },

  async scheduled(controller, env, ctx) {
    ctx.waitUntil(refreshStats(env));
  },
};
