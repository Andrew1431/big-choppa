// Renders the "Pilot's Logbook" flight-board card. Pure function so preview.js can render it offline.

const W = 800, H = 292;
const ink = '#1d2b53', soft = '#5a6b8c';
const stripes = ['#ff5a5f', '#ffb000', '#2bb673', '#3a86ff'];

const esc = s => String(s).replace(/[&<>"']/g, c => `&#${c.charCodeAt(0)};`);
const num = n => Math.round(n).toLocaleString('en-US');

function distance(m) {
  if (m < 1000) return `${num(m)} m`;
  const km = m / 1000;
  return km < 100 ? `${km.toFixed(1)} km` : `${num(km)} km`;
}

function duration(s) {
  if (s < 60) return `${num(s)}s`;
  const m = Math.floor(s / 60);
  if (m < 60) return `${m}m`;
  const h = Math.floor(m / 60);
  return h < 100 ? `${h}h ${m % 60}m` : `${num(h)}h`;
}

// Toy helicopter matching the mod icon, drawn at (x, y) top-left, ~120x56.
const heli = (x, y) => `
  <g transform="translate(${x} ${y})" stroke="${ink}" stroke-width="3" stroke-linejoin="round" stroke-linecap="round">
    <line x1="10" y1="6" x2="98" y2="6"/>
    <line x1="54" y1="6" x2="54" y2="16"/>
    <rect x="80" y="26" width="40" height="8" rx="4" fill="#ff5a5f"/>
    <circle cx="118" cy="24" r="8" fill="none"/>
    <ellipse cx="52" cy="32" rx="32" ry="17" fill="#ff5a5f"/>
    <path d="M40 22 a14 12 0 0 1 26 0 v10 h-26 z" fill="#bfefff"/>
    <line x1="30" y1="54" x2="76" y2="54"/>
    <line x1="40" y1="47" x2="38" y2="54"/>
    <line x1="64" y1="47" x2="66" y2="54"/>
  </g>`;

const tile = (i, value, label) => {
  const col = i % 4, row = Math.floor(i / 4);
  const x = 24 + col * 192, y = 80 + row * 92;
  return `
  <g transform="translate(${x} ${y})">
    <rect width="176" height="76" rx="12" fill="#fff"/>
    <rect width="176" height="6" rx="3" fill="${stripes[(i + row) % 4]}"/>
    <text x="14" y="42" font-size="26" font-weight="800" fill="${ink}">${esc(value)}</text>
    <text x="14" y="63" font-size="13" font-weight="600" fill="${soft}">${esc(label)}</text>
  </g>`;
};

export function renderCard(stats, { updated = new Date(), error = false } = {}) {
  const s = stats ?? {};
  const v = (f, n) => (error || n == null ? '—' : f(n));
  const tiles = [
    [v(num, s.flights), 'flights'],
    [v(num, s.pilots), 'pilots'],
    [v(distance, s.distance_m), 'flown'],
    [v(duration, s.air_s), 'in the air'],
    [v(n => `${num(n)} m`, s.max_altitude_m), 'highest altitude'],
    [v(n => `${num(n)} km/h`, s.top_speed_kmh), 'top speed'],
    [error ? '—' : `${num(s.rolls ?? 0)} · ${num(s.loops ?? 0)}`, 'barrel rolls · loops'],
    [v(num, s.jumped_out), 'jumped out mid-air'],
  ];
  const hhmm = updated.toISOString().slice(11, 16);
  const footer = error
    ? "The logbook is napping. Back soon."
    : `${num(s.ended_abruptly ?? 0)} flights ended abruptly  ·  updated ${hhmm} UTC`;

  return `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}" font-family="'Segoe UI', 'Helvetica Neue', Arial, sans-serif" role="img" aria-label="Big Choppa Pilot's Logbook stats">
  <defs>
    <linearGradient id="sky" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#5ec8f2"/>
      <stop offset="1" stop-color="#a8e4fb"/>
    </linearGradient>
  </defs>
  <rect width="${W}" height="${H}" rx="18" fill="url(#sky)"/>
  <path d="M0 18 a18 18 0 0 1 18 -18 h${W - 36} a18 18 0 0 1 18 18 v46 h-${W} z" fill="#ffd23f"/>
  <text x="24" y="42" font-size="28" font-weight="900" fill="${ink}" letter-spacing="1">BIG CHOPPA</text>
  <text x="218" y="42" font-size="18" font-weight="700" fill="${ink}" opacity="0.75">Pilot's Logbook</text>
  <circle cx="372" cy="36" r="5" fill="#ff5a5f"><animate attributeName="opacity" values="1;0.2;1" dur="1.6s" repeatCount="indefinite"/></circle>
  <text x="383" y="41" font-size="13" font-weight="700" fill="#c0392b">LIVE</text>
  ${heli(650, 4)}
  ${tiles.map(([val, label], i) => tile(i, val, label)).join('')}
  <text x="24" y="${H - 18}" font-size="13" font-weight="600" fill="${ink}" opacity="0.7">${esc(footer)}</text>
</svg>`;
}
