#!/usr/bin/env bash
# Compile-check opti-pdf on Linux (no Windows/Mono needed).
# Restores packages.config from nuget.org, builds with dotnet msbuild
# against .NET Framework 4.8 reference assemblies. Compile check only —
# the WinForms exe cannot run on Linux.
set -euo pipefail
cd "$(dirname "$0")/.."
FLAT="https://api.nuget.org/v3-flatcontainer"
REF="packages/refasm-net48"

mkdir -p packages
grep -oP '<package id="\K[^"]+" version="[^"]+' packages.config | sed 's/" version="/ /' |
while read -r id ver; do
  dest="packages/${id}.${ver}"; [ -d "$dest" ] && continue
  lower="$(echo "$id" | tr '[:upper:]' '[:lower:]')"
  curl -fsSL "$FLAT/$lower/$ver/$lower.$ver.nupkg" -o /tmp/pkg.nupkg
  unzip -qo /tmp/pkg.nupkg -d "$dest" && rm -f /tmp/pkg.nupkg
done
if [ ! -d "$REF" ]; then
  curl -fsSL "$FLAT/microsoft.netframework.referenceassemblies.net48/1.0.3/microsoft.netframework.referenceassemblies.net48.1.0.3.nupkg" -o /tmp/ref.nupkg
  unzip -qo /tmp/ref.nupkg -d "$REF" && rm -f /tmp/ref.nupkg
fi

dotnet msbuild opti-pdf.csproj -restore:false -nologo -v:m \
  -p:Configuration=Release \
  -p:FrameworkPathOverride="$PWD/$REF/build/.NETFramework/v4.8" \
  -p:GenerateResourceMSBuildArchitecture=CurrentArchitecture \
  -p:GenerateResourceMSBuildRuntime=CurrentRuntime \
  -p:CustomBeforeMicrosoftCSharpTargets="$PWD/scripts/net48-facades.targets"
