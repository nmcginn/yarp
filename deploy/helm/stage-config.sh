#!/usr/bin/env sh
# Stage the repository's route and environment configuration into the Helm chart so it can be
# rendered into ConfigMaps (spec §4.3, §10 step 3). Run from the repository root.
#
#   deploy/helm/stage-config.sh dev
#
# Helm can only read files inside the chart directory, which is why this copy exists. The staged
# files are git-ignored: the source of truth stays config/.
set -eu

environment="${1:?usage: deploy/helm/stage-config.sh <environment>   (dev | prod)}"
root="$(cd "$(dirname "$0")/../.." && pwd)"
chart="$root/deploy/helm/proxy"

source_env="$root/config/environments/$environment.yaml"
if [ ! -f "$source_env" ]; then
  echo "no such environment file: $source_env" >&2
  exit 2
fi

rm -f "$chart"/config/routes/*.yaml
mkdir -p "$chart/config/routes"
cp "$root"/config/routes/*.yaml "$chart/config/routes/" 2>/dev/null || true
cp "$source_env" "$chart/config/environment.yaml"

count=$(find "$chart/config/routes" -name '*.yaml' | wc -l | tr -d ' ')
echo "staged $count route file(s) and environment '$environment' into $chart/config"
