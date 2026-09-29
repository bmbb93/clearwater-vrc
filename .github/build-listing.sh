#!/usr/bin/env bash
# Builds the VPM listing (index.json) and a small landing page from the repository's releases, and copies the
# explainer page (Documentation~/how-it-works) next to them.
# Each release carries the package zip and its package.json; the listing gives every version with its zip URL
# and SHA-256, the format ALCOM / VCC read. Usage (in Actions, GH_TOKEN set): build-listing.sh <out dir>
set -euo pipefail
OUT="$1"
REPO="$GITHUB_REPOSITORY"
OWNER="${REPO%%/*}"
NAME="${REPO#*/}"
BASE="https://${OWNER}.github.io/${NAME}"
AUTHOR=$(jq -r '.author.name // empty' package.json)
AUTHOR=${AUTHOR:-$OWNER}

packages='{}'
for tag in $(gh release list --repo "$REPO" --limit 200 --json tagName,isDraft --jq '.[] | select(.isDraft | not) | .tagName'); do
  dir=$(mktemp -d)
  gh release download "$tag" --repo "$REPO" --dir "$dir" --pattern "*.zip" --pattern "package.json"
  zip=$(ls "$dir"/*.zip)
  sha=$(sha256sum "$zip" | cut -d" " -f1)
  url="https://github.com/$REPO/releases/download/$tag/$(basename "$zip")"
  entry=$(jq --arg url "$url" --arg sha "$sha" '. + {url: $url, zipSHA256: $sha}' "$dir/package.json")
  packages=$(jq --argjson e "$entry" '.[$e.name].versions[$e.version] = $e' <<<"$packages")
done

mkdir -p "$OUT"
jq -n --arg name "$AUTHOR packages" --arg id "com.${OWNER,,}.vpm" --arg url "$BASE/index.json" \
  --arg author "$AUTHOR" --argjson packages "$packages" \
  '{name: $name, id: $id, url: $url, author: $author, packages: $packages}' > "$OUT/index.json"

display=$(jq -r '.displayName' package.json)
cat > "$OUT/index.html" <<EOF
<!doctype html>
<html lang="ja"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>$display — VPM</title>
<style>body{font-family:system-ui,sans-serif;max-width:40rem;margin:3rem auto;padding:0 1rem;line-height:1.6}
code{background:#8882;padding:.1em .3em;border-radius:4px;word-break:break-all}a.b{display:inline-block;padding:.6em 1em;border-radius:8px;background:#2563eb;color:#fff;text-decoration:none}</style>
</head><body>
<h1>$display</h1>
<p><a class="b" href="vcc://vpm/addRepo?url=$BASE/index.json">ALCOM / VCC に追加</a></p>
<p>ボタンが動かないときは、ALCOM / VCC の「パッケージ」設定でリポジトリを追加し、次の URL を入れてください。</p>
<p><code>$BASE/index.json</code></p>
<p><a href="how-it-works/">仕組み（動く図解）</a> · <a href="https://github.com/$REPO">GitHub</a></p>
</body></html>
EOF
# the animated explainer (Documentation~/how-it-works) is served beside the listing
cp -r "Documentation~/how-it-works" "$OUT/how-it-works"

echo "listing: $(jq '[.packages[].versions | keys[]] | length' "$OUT/index.json") version(s) -> $BASE/index.json"
