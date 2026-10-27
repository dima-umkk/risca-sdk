#!/usr/bin/env bash
# Нативная (NativeAOT) сборка rasm и rasim.
#
#   ./build.sh                 # RID текущей платформы
#   ./build.sh linux-x64       # конкретные RID
#   TOOLS="rasim" ./build.sh    # только нужные утилиты (по умолчанию rasm rasim)
#   PATCH=42 ./build.sh        # переопределить Patch (по умолчанию = число коммитов минус PatchBase)
#
# Результат: artifacts/<rid>/<tool>[.exe] и единый архив artifacts/risca-sdk-<version>-<rid>.zip
# (инструменты + doc/ + samples/lib как lib/)
# Major и Minor берутся из version.props, Patch — отсюда или из CI.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
read -r -a tools <<< "${TOOLS:-rasm rasim}"
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
      rasim) rel=src/RiscA.Rasim/RiscA.Rasim.csproj ;;
      *) echo "unknown tool: $tool (known: rasm rasim)" >&2; exit 1 ;;
    esac
    # ${extra[@]+"${extra[@]}"} — пустой массив под set -u: в bash 3.2 (macOS) "${extra[@]}" считается unbound.
    dotnet publish "$root/$rel" -c Release -r "$r" "-p:MajorVersion=$major" "-p:MinorVersion=$minor" "-p:PatchVersion=$patch" ${extra[@]+"${extra[@]}"} --nologo -o "$outdir/$r"

    bin=$tool
    case "$r" in win*) bin=$tool.exe ;; esac
    ls -l "$outdir/$r/$bin"
  done

  # единый SDK-архив: инструменты этой публикации + доки + ассемблерные библиотеки (samples/lib -> lib/)
  stage="$outdir/.sdk-stage-$r"; rm -rf "$stage"; mkdir -p "$stage"
  entries=()
  for tool in "${tools[@]}"; do
    bin=$tool
    case "$r" in win*) bin=$tool.exe ;; esac
    [ -f "$outdir/$r/$bin" ] && cp "$outdir/$r/$bin" "$stage/" && entries+=("$bin")
  done
  cp -R "$root/samples/lib" "$stage/lib"
  cp -R "$root/doc" "$stage/doc"
  entries+=("lib" "doc")
  zipname="$outdir/risca-sdk-$version-$r.zip"
  if command -v zip >/dev/null 2>&1; then
    rm -f "$zipname"
    ( cd "$stage" && zip -q -X -r "$zipname" "${entries[@]}" )
    echo "  zip: $zipname"
  elif command -v python3 >/dev/null 2>&1; then
    # в WSL утилиты zip часто нет — пакуют тем же способом
    python3 - "$stage" "$zipname" <<'PY'
import os, sys, zipfile
stage, path = sys.argv[1:3]
if os.path.exists(path):
    os.remove(path)
with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as zf:
    for d, _, files in os.walk(stage):
        for f in files:
            full = os.path.join(d, f)
            arc = os.path.relpath(full, stage).replace(os.sep, "/")
            mode = 0o100755 if os.access(full, os.X_OK) else 0o100644
            info = zipfile.ZipInfo(arc, date_time=(1980, 1, 1, 0, 0, 0))
            info.external_attr = mode << 16
            info.compress_type = zipfile.ZIP_DEFLATED
            with open(full, "rb") as src:
                zf.writestr(info, src.read())
print("  zip:", path)
PY
  else
    echo "  zip не создан: нет ни zip, ни python3"
  fi
  rm -rf "$stage"
done
