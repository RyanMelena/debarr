#!/usr/bin/env bash
# Replays the build stage of src/Dockerfile on the host: the same COPY sets in the same order,
# the same restore, and the same publish. Checks that the COPY list is complete, that the cached
# restore layer survives a source change, and that the publish writes the payload the runtime
# stage copies. Run it on a machine with the .NET SDK and no Docker.
#
# Keep the COPY sets below in step with src/Dockerfile.
#
# A repeat run can fail in the static web asset compression task while an MSBuild server from an
# earlier run still holds the staging path. Run `dotnet build-server shutdown` and start again.
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
src="$repo/src"
stage="${TMPDIR:-/tmp}/debarr-dockerstage"

rm -rf "$stage"
mkdir -p "$stage/Debarr"

# COPY set 1: the files the restore layer needs.
cp "$src/Directory.Build.props" "$src/Directory.Packages.props" "$src/global.json" "$stage/"
cp "$src/Debarr/Debarr.csproj" "$stage/Debarr/"

cd "$stage"
dotnet restore Debarr/Debarr.csproj
test -s Debarr/obj/project.assets.json
echo "ok: restore resolved the project from the csproj-only layer"

# COPY set 2: the rest of the context, minus what src/.dockerignore excludes.
cd "$src"
find . \
  \( -name bin -o -name obj -o -name .vs -o -name TestResults -o -name Debarr.Tests \) -prune -o \
  -type f -print \
  | while read -r f; do
      mkdir -p "$stage/$(dirname "$f")"
      cp "$f" "$stage/$f"
    done

cd "$stage"
test ! -d Debarr.Tests
test -s Debarr/obj/project.assets.json
echo "ok: the source copy left the restored obj/ intact"

DEBARR__APP__DATADIR="$stage/codegen-data" dotnet run --project Debarr/Debarr.csproj -- codegen write
echo "ok: codegen write ran in the copied context"

dotnet publish Debarr/Debarr.csproj -c Release -o "$stage/app"

cd "$stage/app"
test -f Debarr.dll
test -f runtimes/linux-x64/native/libe_sqlite3.so
test -f Debarr.staticwebassets.endpoints.json
# Blazor Server opens its circuit with blazor.web.js. Publishing with --no-restore leaves it out.
test -f wwwroot/_framework/blazor.web.js
echo "ok: publish produced the linux payload the runtime stage copies"
