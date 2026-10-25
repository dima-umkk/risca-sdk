#!/usr/bin/env bash
# Нативная (NativeAOT) сборка rasm.
#
#   ./build.sh                 # RID текущей платформы
#   ./build.sh linux-x64       # конкретные RID
#   MINOR=42 ./build.sh        # переопределить Minor (по умолчанию = число коммитов в HEAD)
#
# Результат: artifacts/<rid>/rasm[.exe] и artifacts/rasm-<version>-<rid>.zip
# Major берётся из version.props, Minor/Patch — сюда или из CI.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project="$root/src/RiscA.Assembler/RiscA.Assembler.csproj"
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

major="$(sed -n 's/.*<MajorVersion>\([0-9]\{1,\}\)<.*/\1/p' "$root/version.props" | head -1)"
major="${MAJOR:-${major:-0}}"
minor="${MINOR:-$(git -C "$root" rev-list --count HEAD 2>/dev/null || echo 0)}"
version="$major.$minor.0"
echo "rasm $version  (major: version.props, minor: $minor)"

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
  # ${extra[@]+"${extra[@]}"} — пустой массив под set -u: в bash 3.2 (macOS) "${extra[@]}" считается unbound.
  dotnet publish "$project" -c Release -r "$r" "-p:MajorVersion=$major" "-p:MinorVersion=$minor" ${extra[@]+"${extra[@]}"} --nologo -o "$outdir/$r"

  bin=rasm
  case "$r" in win*) bin=rasm.exe ;; esac
  ls -l "$outdir/$r/$bin"

  if command -v zip >/dev/null 2>&1; then
    rm -f "$outdir/rasm-$version-$r.zip"
    ( cd "$outdir/$r" && zip -q -X "../rasm-$version-$r.zip" "$bin" )
    echo "  zip: $outdir/rasm-$version-$r.zip"
  elif command -v python3 >/dev/null 2>&1; then
    # в WSL утилиты zip часто нет — пакуют тем же способом
    python3 - "$outdir" "$r" "$version" "$bin" <<'PY'
import os, sys, zipfile
outdir, rid, version, binary = sys.argv[1:5]
path = os.path.join(outdir, f"rasm-{version}-{rid}.zip")
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
