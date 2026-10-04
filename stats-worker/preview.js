// node preview.js → preview.svg with made-up numbers, for tweaking the card without deploying.
import { writeFileSync } from 'node:fs';
import { renderCard } from './src/card.js';

writeFileSync('preview.svg', renderCard({
  flights: 1284, pilots: 213, distance_m: 482_300, air_s: 151_200, max_altitude_m: 812,
  top_speed_kmh: 247, rolls: 3021, loops: 418, jumped_out: 977, landed: 512, ended_abruptly: 655,
  upside_down_s: 9_420, items_pocketed: 2_310, items_spilled: 731, solo_flights: 802, duo_flights: 311, trio_flights: 171,
  daily: [42, 0, 31, 88, 120, 64, 17].map((flights, i) => ({
    day: new Date(Date.now() - (6 - i) * 86_400_000).toISOString().slice(0, 10), flights,
  })),
}));
console.log('wrote preview.svg');
