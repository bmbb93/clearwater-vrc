#!/usr/bin/env bash
# Builds the Clearwater wave ambience from the source recording (needs ffmpeg with libvorbis).
#
# Source: "Stromboli beach" by nicola_ariutti, CC0
#   https://freesound.org/people/nicola_ariutti/sounds/813228/
#   (download the original FLAC while logged in; it is not kept in the repo)
#
# Usage: Tools~/make_wave_audio.sh path/to/813228__nicola_ariutti__stromboli-beach.flac
set -euo pipefail
SRC="$1"
OUT="$(cd "$(dirname "$0")/.." && pwd)/Runtime/Audio"
TMP="$(mktemp -d)"
mkdir -p "$OUT"

START=55   # steadiest 90 s stretch (fewest wind thumps), found from per-second band RMS
LEN=90
XF=4       # the last XF seconds crossfade into the start, so the loop has no seam

# 1. cut, remove wind rumble, make seamless: seg[XF:LEN] with its tail crossfaded into seg[0:XF]
ffmpeg -hide_banner -loglevel error -y -ss "$START" -t $((LEN + XF)) -i "$SRC" -filter_complex \
  "[0:a]highpass=f=80:poles=2,asplit=2[s1][s2];[s1]atrim=start=$XF,asetpts=N/SR/TB[body];[s2]atrim=end=$XF,asetpts=N/SR/TB[head];[body][head]acrossfade=d=$XF:c1=qsin:c2=qsin[loop]" \
  -map "[loop]" -c:a pcm_s24le "$TMP/loop_raw.wav"

# 2. normalise to -20 LUFS (the recording is very quiet), peak-limited
L=$(ffmpeg -hide_banner -nostats -i "$TMP/loop_raw.wav" -af ebur128 -f null - 2>&1 | grep -E "^\s+I:" | tail -1 | awk '{print $2}')
G=$(awk "BEGIN { printf \"%.2f\", -20 - ($L) }")
ffmpeg -hide_banner -loglevel error -y -i "$TMP/loop_raw.wav" -af "volume=${G}dB,alimiter=limit=0.89:level=false" -c:a pcm_s24le "$TMP/loop.wav"

# 3. variants
#   shore: mono, for the 3D source that follows the player along the waterline
ffmpeg -hide_banner -loglevel error -y -i "$TMP/loop.wav" -ac 1 -c:a libvorbis -q:a 5 "$OUT/WavesShore.ogg"
#   bed: stereo, darker (distant sea), rotated by half a loop so it never lines up with the shore source
H=$((LEN / 2))
ffmpeg -hide_banner -loglevel error -y -i "$TMP/loop.wav" -filter_complex \
  "[0:a]asplit=2[a][b];[a]atrim=start=$H,asetpts=N/SR/TB[t];[b]atrim=end=$H,asetpts=N/SR/TB[h];[t][h]concat=n=2:v=0:a=1,lowpass=f=2500,volume=-4dB[o]" \
  -map "[o]" -c:a libvorbis -q:a 4 "$OUT/WavesBed.ogg"
#   underwater: muffled — only the low rumble of the surf gets through
ffmpeg -hide_banner -loglevel error -y -i "$TMP/loop.wav" \
  -af "lowpass=f=450:poles=2,lowpass=f=450:poles=2,bass=g=4:f=150,volume=-2dB,alimiter=limit=0.89:level=false" \
  -c:a libvorbis -q:a 4 "$OUT/WavesUnderwater.ogg"

rm -rf "$TMP"
ls -la "$OUT"

# 4. wave break timing for the shoreline swash
node "$(dirname "$0")/detect_wave_breaks.js"
