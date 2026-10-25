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
- **Versioning:** root `version.props` holds `<MajorVersion>` (manual) and root `Directory.Build.props`
  composes `Version = Major.Minor.Patch`, where Minor is the commit count (`git rev-list --count HEAD`)
  passed in as `-p:MinorVersion=` by `build.ps1`/`build.sh`/CI (env `MINOR` overrides; 0 when git is absent).
  `rasm --version` prints `rasm <major.minor.patch>`.
- **CI:** `.github/workflows/release.yml` — push to `main`/`master` (or manual run) → version → test →
  build matrix (win-x64, win-arm64, linux-x64, linux-arm64, osx-arm64) → GitHub Release tagged `v<version>`.
- No lint/formatter config.
- All projects: `<ImplicitUsings>enable</ImplicitUsings>`, `<Nullable>enable</Nullable>`.
- **ISA docs:** `doc/RiscA.MD` (spec) and `doc/README.md` (encoding reference with examples & limits).
- **`Instruction` struct** in `ISA/Instruction.cs` — immutable `readonly struct` with `(mask, shift)` tuple fields and `with*()` builder methods.
- **Assembler CLI** (`src/RiscA.Assembler/Program.cs`): `rasm -i <file.rasm>` plus optional `-v` (verbose), `-vp` (parser match debug), `-vi` (instruction dump). Sample: `samples/base.test.rasm`.
- **Verbose flags** (`src/RiscA.Core/Verbose.cs`): `Verbose.ParserMatches` (`-vp`) and `Verbose.AssemblerInstructions` (`-vi`) control debug output.
