## RiscA SDK

| Project | Path | Type |
|---------|------|------|
| `RiscA.Core` | `src/RiscA.Core/` | Library — `RiscA.Core.ISA` (`Instruction`, `OpCode`, func enums) + `RiscA.Core.Asm` (`Assembler`, `Parser`, `Tokenizer`, `TK`) |
| `RiscA.Assembler` | `src/RiscA.Assembler/` | CLI (`Program.cs`) — references Core. Builds as `rasm.exe`. |
| `RiscA.Core.Tests` | `tests/RiscA.Core.Tests/` | xunit + FluentAssertions — references Core |

## Essentials

- **Build:** `dotnet build` — .NET 10.0 target throughout. Do not downgrade.
- **Test:** `dotnet test` — xunit v2, FluentAssertions v8, coverlet collector v6.
  Filter: `dotnet test --filter "FullyQualifiedName~ClassName"`.
- **Solution:** `RiscASDK.slnx` (new XML solution format, not Visual Studio `.sln`).
- **Native build:** `RiscA.Assembler` has `<PublishAot>true</PublishAot>` (no runtime dependency,
  ~2 MB exe); `RiscA.Core` has `<IsAotCompatible>true</IsAotCompatible>`. Use `.\build.ps1` (Windows)
  / `./build.sh` (Linux/macOS) at repo root — they publish AOT binaries into `artifacts/<rid>/`
  and zip them as `artifacts/rasm-<version>-<rid>.zip`. AOT cannot be cross-built between OSes:
  win-* needs MSVC (win-arm64 also needs the "C++ ARM64 build tools" VS component),
  linux-* needs clang + lld (+ `gcc-aarch64-linux-gnu libc6-dev-arm64-cross binutils-aarch64-linux-gnu`
  for the linux-arm64 cross build), osx-arm64 needs a Mac. The scripts add
  `-p:LinkerFlavor=lld -p:ObjCopyName=aarch64-linux-gnu-objcopy` automatically for `linux-arm64`
  (host bfd linker and objcopy cannot handle aarch64 objects; the driver must stay clang —
  `CppCompilerAndLinker=aarch64-linux-gnu-gcc` fails on the SDK's `--target=` flag).
  `build.sh` falls back to `python3` for zipping when `zip` is missing (typical in WSL).
- **Versioning:** root `version.props` holds `<MajorVersion>` and `<MinorVersion>` (both manual) plus
  `<PatchBase>`; `Directory.Build.props` composes `Version = Major.Minor.Patch`, where Patch is
  `git rev-list --count HEAD` minus `PatchBase` (clamped at 0), passed in as `-p:PatchVersion=` by
  `build.ps1`/`build.sh`/CI (param `-Patch` / env `PATCH` overrides; 0 when git is absent). Bump
  `PatchBase` to the current commit count whenever `MinorVersion` changes, so Patch restarts at 0.
  `rasm --version` prints `rasm <major.minor.patch>`.
- **CI:** `.github/workflows/release.yml` — push to `main` (or manual run) → version → test →
  build matrix (win-x64, win-arm64, linux-x64, linux-arm64, osx-arm64) → GitHub Release tagged `v<version>`.
  The `release` job writes `notes.md` from `git log` of the commits since the previous release
  (`gh release create --notes-file` / `gh release edit` on a rerun) — release notes are generated, never hand-written.
- No lint/formatter config.
- All projects: `<ImplicitUsings>enable</ImplicitUsings>`, `<Nullable>enable</Nullable>`.
- **ISA docs:** `doc/RiscA.MD` (spec) and `doc/README.md` (encoding reference with examples & limits).
  `doc/RiscA.MD` is a copy of the spec from the RTL repo `dima-umkk/risca`, with our own fixes applied
  (stale instruction lists, `func1` label, branch range, typos). When syncing it with upstream, re-apply those fixes;
  never edit the upstream repo from this SDK.
- **`Instruction` struct** in `ISA/Instruction.cs` — immutable `readonly struct` with `(mask, shift)` tuple fields and `with*()` builder methods.
- **Assembler CLI** (`src/RiscA.Assembler/Program.cs`): `rasm -i <file.rasm>` plus optional `-v` (verbose), `-vp` (parser match debug), `-vi` (instruction dump). Sample: `samples/base.test.rasm`.
- **Verbose flags** (`src/RiscA.Core/Verbose.cs`): `Verbose.ParserMatches` (`-vp`) and `Verbose.AssemblerInstructions` (`-vi`) control debug output.
