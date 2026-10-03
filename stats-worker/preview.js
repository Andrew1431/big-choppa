// node preview.js → preview.svg with made-up numbers, for tweaking the card without deploying.
import { writeFileSync } from 'node:fs';
import { renderCard } from './src/card.js';

writeFileSync('preview.svg', renderCard({
  flights: 1284, pilots: 213, distance_m: 482_300, air_s: 151_200, max_altitude_m: 812,
  top_speed_kmh: 247, rolls: 3021, loops: 418, jumped_out: 977, ended_abruptly: 655,
}));
console.log('wrote preview.svg');
