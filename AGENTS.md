## RiscA SDK

| Project | Path | Type |
|---------|------|------|
| `RiscA.Core` | `src/RiscA.Core/` | Library — `RiscA.Core.ISA` (`Instruction`, `OpCode`, func enums) + `RiscA.Core.Asm` (`Assembler`, `Preprocessor`, `Parser`, `Tokenizer`, `TK`) |
| `RiscA.Assembler` | `src/RiscA.Assembler/` | CLI (`Program.cs`) — references Core. Builds as `rasm.exe`. |
| `RiscA.Core.Tests` | `tests/RiscA.Core.Tests/` | xunit + FluentAssertions — references Core |
| `RiscA.Machine` | `src/RiscA.Machine/` | Library — emulator core: `AddressSpace`, `RamRegion`, `DeviceRegion`, `IDevice`, `UartDevice`, `IConsole`, `InputWaitDetector`, `Machine`, `Registers`, `ProgramImage`, `BusFault`, `MachineOptions`. References Core. |
| `RiscA.Rsim` | `src/RiscA.Rsim/` | CLI (`Program.cs`, `AssemblyName=rsim`, `PublishAot`) — one tool, subcommands `run`, `disasm`, `debug` (stub until M2). References Core + Machine. |
| `RiscA.Machine.Tests` | `tests/RiscA.Machine.Tests/` | xunit + FluentAssertions — references RiscA.Machine |

## Essentials

- **Build:** `dotnet build` — .NET 10.0 target throughout. Do not downgrade.
- **Test:** `dotnet test` — xunit v2, FluentAssertions v8, coverlet collector v6.
  Filter: `dotnet test --filter "FullyQualifiedName~ClassName"`.
- **Solution:** `RiscASDK.slnx` (new XML solution format, not Visual Studio `.sln`).
- **Native build:** `RiscA.Assembler` and `RiscA.Rsim` have `<PublishAot>true</PublishAot>` (no runtime
  dependency, ~2 MB exe each); `RiscA.Core` and `RiscA.Machine` have `<IsAotCompatible>true</IsAotCompatible>`.
  Use `.\build.ps1` (Windows)
  / `./build.sh` (Linux/macOS) at repo root — they publish AOT binaries into `artifacts/<rid>/`
  and zip them as `artifacts/rasm-<version>-<rid>.zip` and `artifacts/rsim-<version>-<rid>.zip`. AOT cannot be cross-built between OSes:
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
  `rasm --version` prints `rasm <major.minor.patch>`, `rsim --version` prints `rsim <major.minor.patch>`.
- **CI:** `.github/workflows/release.yml` — push to `main` (or manual run) → version → test →
  build matrix (win-x64, win-arm64, linux-x64, linux-arm64, osx-arm64) → GitHub Release tagged `v<version>`.
  The `release` job writes `notes.md` from `git log` of the commits since the previous release
  (`gh release create --notes-file` / `gh release edit` on a rerun) — release notes are generated, never hand-written.
- No lint/formatter config.
- **Workflow:** never commit directly to `main` — work on a feature branch and open a
  pull request; `main` moves only via merged PRs.
- All projects: `<ImplicitUsings>enable</ImplicitUsings>`, `<Nullable>enable</Nullable>`.
- **ISA docs:** `doc/RiscA.MD` is the spec and authority (a copy of the spec from the RTL repo
  `dima-umkk/risca`, with our own fixes applied — stale instruction lists, `func1` label, branch
  range, typos; when syncing with upstream, re-apply those fixes, never edit the upstream repo
  from this SDK). `doc/instruction-set.md` is the detailed encoding reference (bit layouts,
  examples, limits) with the ABI section. `doc/README.md` is a slim navigator: a map of the
  architecture docs plus a one-screen cheat sheet (opcode table, ABI, assembler directives,
  quick start).
- **`Instruction` struct** in `ISA/Instruction.cs` — immutable `readonly struct` with `(mask, shift)` tuple fields and `with*()` builder methods.
- **Assembler CLI** (`src/RiscA.Assembler/Program.cs`): `rasm -i <file.rasm>` plus optional `-v` (verbose), `-vp` (parser match debug), `-vi` (instruction dump). Sample: `samples/base.test.rasm`.
- **Assembler directives** (`src/RiscA.Core/Asm/Preprocessor.cs`, runs before the two assembler passes):
  `.func name:` (entry, may insert a `NOP` for word alignment), `.equ NAME, value` (integer constant in
  decimal/`0x`/`0b`, substituted into any number position), `.include "file"` (spliced in, resolved relative
  to the including file first, each file included once, cycles ignored). Constants are collected in a second
  pass over the spliced lines, so a library included at the end of a file still provides its constants to the
  code above it (the image starts with the program body, so libraries go last — see `samples/uart.echo.rasm` and
  `samples/lib/uart.rasm`). Directive names are matched at string level, because the tokenizer cannot read `.name`.
  Directive syntax and caveats are documented in `doc/README.md` ("Директивы ассемблера").
- **Assembler design record:** `doc/assembler-architecture.md` — the assembler pipeline
  (Preprocessor → Parser rule engine → addressing + a label-resolution/relaxation loop to a
  fixed point) and the implemented milestone **A1**: pseudo-ops `li/la/j` expanded by parser
  rules (`mv` deliberately not added — `MOV` is a real instruction), plus the stack pseudo-ops
  `push rN`/`pop rN` (the ABI macro of `doc/RiscA.MD` verbatim: `SUB R15,4`+`STW [R15+0],rN` /
  `LDW rN,[R15+0]`+`ADD R15,4`, order fixed so a pair balances R15; 2 halfwords each, no fixups,
  no scratch, `push r15`/`pop r15` are errors), a `List<Fixup { Slot; Label; FixupKind }>` (Branch/Call/Jr/PoolRef/DataAddr; mutable class, also `Target` = pool slot, `Reg` for `j rN`) on `ParsedInstruction` replacing `RefLabel`, per-function constant pools (`dw label ;pool`, marked `IsPool`, keyed by region+label), and the relaxation loop (invert-and-JR
  for far branches, `la r7`+`CALL r7` for far calls; for an unreachable pool slot `la` expands
  to an inline mini-pool — `ldi` + `jr` over the data + `dw label` — so every absolute address
  stays in a `dw` entry, which later doubles as the relocation table for load-at-any-address).
  Acceptance: all tests green, `samples/` images byte-identical to pre-A1, far jumps/calls
  assemble and execute in the emulator (`RunTest` assembles with `origin` = load address).
  The accepted **A2** plan (section 7, not implemented yet): named frame slots with generated
  prologue/epilogue (`.func name(a, b)` / `.end` — save only the R8–R13 actually clobbered,
  none for a leaf), `call f(a, b)` argument-list sugar (permutation via r6/r7; >6 args on the
  stack per the fixed ABI in `doc/instruction-set.md`), and virtual-name register allocation in
  `rasm` (linear-scan over the 16 physical registers, single-def SSA liveness, spill to auto
  slots, mov/phi copy coalescing; the relaxation scratch r7 stays physical) — register
  allocation lives in the assembler, not in the compiler (`rcc` stays a near-transliterator of
  IL). New mechanisms stay silent for sources that use only physical registers. Inside a
  named region physical registers are pinned point-uses only: `r14`/`r15`/`r7` are
  assembly-time errors there, every exit is written as `ret x` (expands to `mov r0, x` plus a
  jump to the region's single epilogue; `ret` with no operand skips the `mov`), the epilogue is
  emitted once per region and must never restore into `r0` (it already holds the exit value),
  and the register pool for names is `r0..r6` (caller-saved class)
  plus `r8..r13` (intervals live across calls) — never `r7/r14/r15`.
- **Machine design record:** `doc/emulator-architecture.md` — the agreed architecture of the emulator,
  debugger and compiler (one flat address space — every address reachable both from the fetch
  path and from `LDB/LDW/STB/STW`, a single load image, memory map with device blocks of 256 bytes at low
  addresses, UART0 = 4 registers, no interrupts yet, one `rsim` CLI, symbol map format, milestones M0…M4).
  The emulator core (milestone M0) and the console uart layer (M1) live in `src/RiscA.Machine`, the
  `rsim` CLI (M1: `run` and `disasm` work) in `src/RiscA.Rsim`; the debugger and the
  compiler are not implemented yet. The spec `doc/RiscA.MD` is the authority, `artifacts/rtl/`
  is a read-only upstream reference and must not be edited.
- **Compiler design record:** `doc/compiler-architecture.md` — the agreed architecture of the
  C compiler (milestone M3): reuse a C frontend via QBE IL (textual SSA-like IR) + our own
  RiscA back-end `rcc` in C# emitting one `.rasm` (no object files/linker); prerequisite
  milestone A1 in the assembler: pseudo-ops `li/la/j`, a fixup list on `ParsedInstruction`,
  per-function constant pools and an iterative branch/CALL relaxation loop; QBE 1.3 and cproc
  both build in WSL (cproc: author's mirror `github.com/michaelforney/cproc`, download tarballs
  with Windows curl — github is unreachable from WSL; the `michg/cproc` fork's `rv32` target
  emits the pure-`w` ILP32 IL our back-end consumes; `-d A` and bundled `minic` show the IL).
  Register allocation is decided to live in the assembler (accepted milestone **A2** plan,
  `doc/assembler-architecture.md` section 7), so `rcc` consumes the IL's virtual temporaries
  as named operands and stays a near-transliterator.
  The debugger and the compiler are not implemented yet.
- **Emulator core** (`src/RiscA.Machine`, namespace `RiscA.Machine`, block-style namespaces):
  one flat `AddressSpace` — `RamRegion` (byte array, little-endian, unaligned words allowed) plus
  `DeviceRegion` wrappers around `IDevice` at `addr[31:16] == 0`, device blocks of 256 bytes
  (`AddDevice(name, block, device)`). `ResetVector` defaults to the start of the RAM
  (`0x0001_0000`), `MachineOptions.ResetVector` overrides it. `Machine.Step()`/`Run(limit)` retire one
  instruction at a time, every device gets exactly one `Tick()` per retired instruction; a `BusFault`
  or an illegal instruction does not retire, the `PC` stays on the faulting instruction and the machine
  halts. `StopReason`: `Running`, `Reti` (stop convention), `IntNotImplemented`, `IllegalInstruction`,
  `BusFault`, `InstructionLimit`, `Halted`. `OnRetire` (`Action<StepResult>`) is the trace hook, called
  for retired steps only. The library never touches `System.Console` (the `rsim` CLI owns I/O).
- **Console uart** (`src/RiscA.Machine/UartDevice.cs`): UART0 sits in device block 0 and has four word
  registers — `rx_data` (slot 0, a read pops the receive fifo), `rx_ready` (slot 1), `tx_data` (slot 2, a
  write takes the low byte and is dropped while the transmitter is busy), `tx_ready` (slot 3, `1` when idle).
  The receive fifo is 16 bytes deep, at most one console byte is pulled per retired instruction, and a write
  to `rx_data` injects a byte (scripted input in tests). `txCyclesPerByte` > 0 models the baud rate in
  retired instructions. `InputWaitDetector` catches a program polling an empty `rx_ready` after the input has
  ended (`IConsole.InputEnded`), counted by `UartDevice.EmptyReadyReads`.
- **rsim CLI** (`src/RiscA.Rsim/Program.cs`): `rsim run <prog.bin>` executes an image (`--origin`, `--reset`,
  `--ram-size`, `--stdin <file>`, `--baud`, `--cpu-hz`, `--limit`, `--patience`, `--trace <file>`, `-v`,
  `--permissive`), `rsim disasm <prog.bin>` prints decoded halfwords, `rsim debug` is an M2 stub. Program
  bytes go to stdout, every `rsim` message to stderr. Exit codes: 0 `reti`, 1 usage or file error,
  2 instruction limit, 3 bus fault, 4 illegal instruction, 5 `INT`, 6 waiting for input that will not come.
  Inside `namespace RiscA.Rsim` the cpu class needs `using Machine = RiscA.Machine.Machine;` written inside
  the namespace body, because the bare name otherwise resolves to the `RiscA.Machine` namespace.
- **Verbose flags** (`src/RiscA.Core/Verbose.cs`): `Verbose.ParserMatches` (`-vp`) and `Verbose.AssemblerInstructions` (`-vi`) control debug output.
