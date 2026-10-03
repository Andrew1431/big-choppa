// Serves the Pilot's Logbook card (/card.svg) and raw totals (/stats.json) from PostHog.
// PostHog is queried at most once per CACHE_SECONDS per Cloudflare location.
import { renderCard } from './card.js';

const CACHE_SECONDS = 600;
const CACHE_KEY = 'https://big-choppa-stats.internal/stats/v1';

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
  countIf(event = 'flight_ended' AND properties.how = 'ended abruptly') AS ended_abruptly,
  countIf(event = 'choppa_spawned') AS spawned
FROM events
WHERE event IN ('flight_ended', 'jumped_out', 'choppa_spawned')`;

async function queryPostHog(env) {
  if (!env.POSTHOG_PERSONAL_KEY) throw new Error('POSTHOG_PERSONAL_KEY secret is not set');
  const res = await fetch(`${env.POSTHOG_HOST}/api/projects/${env.POSTHOG_PROJECT_ID}/query/`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${env.POSTHOG_PERSONAL_KEY}`, 'Content-Type': 'application/json' },
    body: JSON.stringify({ query: { kind: 'HogQLQuery', query: QUERY } }),
    signal: AbortSignal.timeout(15000),
  });
  if (!res.ok) throw new Error(`PostHog ${res.status}: ${(await res.text()).slice(0, 300)}`);
  const { columns, results } = await res.json();
  const row = results?.[0] ?? [];
  return Object.fromEntries(columns.map((c, i) => [c, Number(row[i]) || 0]));
}

async function getStats(env, ctx) {
  const cache = caches.default;
  const hit = await cache.match(CACHE_KEY);
  if (hit) return hit.json();
  const stats = { ...(await queryPostHog(env)), updated: new Date().toISOString() };
  ctx.waitUntil(cache.put(CACHE_KEY, new Response(JSON.stringify(stats), {
    headers: { 'Content-Type': 'application/json', 'Cache-Control': `max-age=${CACHE_SECONDS}` },
  })));
  return stats;
}

export default {
  async fetch(request, env, ctx) {
    const { pathname } = new URL(request.url);
    let stats = null, error = null;
    if (pathname === '/card.svg' || pathname === '/stats.json') {
      try { stats = await getStats(env, ctx); }
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
};
