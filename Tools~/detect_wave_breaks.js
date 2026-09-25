// Finds the moments waves break in the shore loop, so the shoreline swash can arrive in time with the sound.
// Usage: node Tools~/detect_wave_breaks.js  (needs ffmpeg; run after make_wave_audio.sh)
// Writes Runtime/Audio/WavesShore_breaks.json: { loopSeconds, times[], amps[] }.
const { execFileSync } = require("child_process");
const fs = require("fs");
const path = require("path");

const audioDir = path.join(__dirname, "..", "Runtime", "Audio");
const clip = path.join(audioDir, "WavesShore.ogg");
const SR = 8000, HOP = 400; // 50 ms frames

// splash band only (the breaking and the rush of water over pebbles)
const pcm = execFileSync("ffmpeg", ["-hide_banner", "-loglevel", "error", "-i", clip,
  "-af", "highpass=f=250,aformat=sample_fmts=flt:channel_layouts=mono", "-ar", String(SR), "-f", "f32le", "-"],
  { maxBuffer: 1 << 28 });
const f = new Float32Array(pcm.buffer, pcm.byteOffset, pcm.length / 4);
const n = Math.floor(f.length / HOP);
const loopSeconds = f.length / SR;

// RMS envelope, 0.45 s smoothing (circular: the clip loops), in dB
const rms = new Float64Array(n);
for (let i = 0; i < n; i++) { let s = 0; for (let j = 0; j < HOP; j++) { const v = f[i * HOP + j]; s += v * v; } rms[i] = Math.sqrt(s / HOP); }
const db = new Float64Array(n);
for (let i = 0; i < n; i++) { let s = 0; for (let k = -4; k <= 4; k++) s += rms[(i + k + n) % n]; db[i] = 20 * Math.log10(s / 9 + 1e-9); }
const median = [...db].sort((a, b) => a - b)[Math.floor(n / 2)];

// a break = the loudest frame within +-1.2 s, at least 1.5 dB above the median; plateaus collapse to one
const W = Math.round(1.2 * SR / HOP);
let peaks = [];
for (let i = 0; i < n; i++) {
  if (db[i] <= median + 1.5) continue;
  let isMax = true;
  for (let k = -W; k <= W && isMax; k++) if (k && db[(i + k + n) % n] > db[i]) isMax = false;
  if (isMax && !(peaks.length && i - peaks[peaks.length - 1] < 1.5 * SR / HOP)) peaks.push(i);
}
if (peaks.length > 1 && (peaks[0] + n - peaks[peaks.length - 1]) < 1.5 * SR / HOP) peaks.pop(); // across the loop point

const levels = peaks.map(i => db[i] - median);
const top = Math.max(...levels);
const out = {
  loopSeconds: +loopSeconds.toFixed(3),
  note: "wave breaks in WavesShore.ogg (Tools~/detect_wave_breaks.js); amp 0..1, loudest = 1",
  times: peaks.map(i => +(i * HOP / SR).toFixed(2)),
  amps: levels.map(v => +(0.35 + 0.65 * Math.pow(10, (v - top) / 20)).toFixed(3)),
};
fs.writeFileSync(path.join(audioDir, "WavesShore_breaks.json"), JSON.stringify(out, null, 1));
const gaps = out.times.map((t, i) => ((out.times[(i + 1) % out.times.length] - t) + loopSeconds) % loopSeconds);
console.log(`${out.times.length} breaks, gap ${Math.min(...gaps).toFixed(1)}-${Math.max(...gaps).toFixed(1)} s (mean ${(loopSeconds / out.times.length).toFixed(1)} s)`);
