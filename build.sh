#!/usr/bin/env bash
# Нативная (NativeAOT) сборка rasm и rsim.
#
#   ./build.sh                 # RID текущей платформы
#   ./build.sh linux-x64       # конкретные RID
#   TOOLS="rsim" ./build.sh    # только нужные утилиты (по умолчанию rasm rsim)
#   PATCH=42 ./build.sh        # переопределить Patch (по умолчанию = число коммитов минус PatchBase)
#
# Результат: artifacts/<rid>/<tool>[.exe] и artifacts/<tool>-<version>-<rid>.zip
# Major и Minor берутся из version.props, Patch — отсюда или из CI.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
read -r -a tools <<< "${TOOLS:-rasm rsim}"
outdir="${OUTDIR:-$root/artifacts}"

case "$(uname -s)/$(uname -m)" in
  Linux/x86_64)  default_rid=(linux-x64) ;;
  Linux/aarch64) default_rid=(linux-arm64) ;;
  Darwin/arm64)  default_rid=(osx-arm64) ;;
  Darwin/x86_64) default_rid=(osx-x64) ;;
  *)             default_rid=(linux-x64) ;;
esac

rids=("$@")
[ ${#rids[@]} -eq 0 ] && rids=("${default_rid[@]}")

props="$root/version.props"
prop() { sed -n "s/.*<$1>\([0-9]\{1,\}\)<.*/\1/p" "$props" | head -1; }
major="${MAJOR:-$(prop MajorVersion)}"
minor="${MINOR:-$(prop MinorVersion)}"
base="$(prop PatchBase)"
major="${major:-0}"; minor="${minor:-0}"; base="${base:-0}"

# Patch = число коммитов в HEAD минус PatchBase (после релиза 0.9.0 растёт на 1 за коммит).
count="$(git -C "$root" rev-list --count HEAD 2>/dev/null || echo 0)"
patch=$((count - base)); [ "$patch" -lt 0 ] && patch=0
patch="${PATCH:-$patch}"
version="$major.$minor.$patch"
echo "${tools[*]} $version  (major/minor: version.props, patch: $patch)"

mkdir -p "$outdir"
for r in "${rids[@]}"; do
  echo
  echo "==> publish $r"
  extra=()
  case "$r" in
    # Кросс-сборка aarch64 на x64-хосте: мультиархитектурный lld вместо bfd-линкера хоста
    # и objcopy из binutils-aarch64-linux-gnu вместо objcopy хоста.
    linux-arm64) extra=("-p:LinkerFlavor=lld" "-p:ObjCopyName=aarch64-linux-gnu-objcopy") ;;
  esac
  for tool in "${tools[@]}"; do
    case "$tool" in
      rasm) rel=src/RiscA.Assembler/RiscA.Assembler.csproj ;;
      rsim) rel=src/RiscA.Rsim/RiscA.Rsim.csproj ;;
      *) echo "unknown tool: $tool (known: rasm rsim)" >&2; exit 1 ;;
    esac
    # ${extra[@]+"${extra[@]}"} — пустой массив под set -u: в bash 3.2 (macOS) "${extra[@]}" считается unbound.
    dotnet publish "$root/$rel" -c Release -r "$r" "-p:MajorVersion=$major" "-p:MinorVersion=$minor" "-p:PatchVersion=$patch" ${extra[@]+"${extra[@]}"} --nologo -o "$outdir/$r"

    bin=$tool
    case "$r" in win*) bin=$tool.exe ;; esac
    ls -l "$outdir/$r/$bin"

    if command -v zip >/dev/null 2>&1; then
      rm -f "$outdir/$tool-$version-$r.zip"
      ( cd "$outdir/$r" && zip -q -X "../$tool-$version-$r.zip" "$bin" )
      echo "  zip: $outdir/$tool-$version-$r.zip"
    elif command -v python3 >/dev/null 2>&1; then
      # в WSL утилиты zip часто нет — пакуют тем же способом
      python3 - "$outdir" "$r" "$version" "$bin" "$tool" <<'PY'
import os, sys, zipfile
outdir, rid, version, binary, tool = sys.argv[1:6]
path = os.path.join(outdir, f"{tool}-{version}-{rid}.zip")
if os.path.exists(path):
    os.remove(path)
with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as zf:
    info = zipfile.ZipInfo(binary, date_time=(1980, 1, 1, 0, 0, 0))
    info.external_attr = 0o100755 << 16
    info.compress_type = zipfile.ZIP_DEFLATED
    with open(os.path.join(outdir, rid, binary), "rb") as src:
        zf.writestr(info, src.read())
print("  zip:", path)
PY
    else
      echo "  zip не создан: нет ни zip, ни python3"
    fi
  done
done
