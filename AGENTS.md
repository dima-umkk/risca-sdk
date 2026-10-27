## RiscA SDK

| Project | Path | Type |
|---------|------|------|
| `RiscA.Core` | `src/RiscA.Core/` | Library — `RiscA.Core.ISA` (`Instruction`, `OpCode`, func enums) + `RiscA.Core.Asm` (`Assembler`, `Preprocessor`, `Parser`, `Tokenizer`, `TK`) + `RiscA.Core.HW` (`CPU`, `Bus` with nested `Options`/`Fault`/`FaultKind`, `Ram`, `Rom`, `IDevice`, `UartDevice`, `ProgramImage`, `StepResult`, `StopReason`) |
| `RiscA.Assembler` | `src/RiscA.Assembler/` | CLI (`Program.cs`) — references Core. Builds as `rasm.exe`. |
| `RiscA.Core.Tests` | `tests/RiscA.Core.Tests/` | xunit + FluentAssertions — references Core; `HW/` holds the emulator tests |
| `RiscA.Rasim` | `src/RiscA.Rasim/` | CLI (`Program.cs` → `RunCommand.cs`, `DebugSession.cs`, `Cli.cs`; `AssemblyName=rasim`, `PublishAot`) — single entry, no subcommands: script run or interactive debugger (M2). References Core. |

## Essentials

- **Build:** `dotnet build` — .NET 10.0 target throughout. Do not downgrade.
- **Test:** `dotnet test` — xunit v2, FluentAssertions v8, coverlet collector v6.
  Filter: `dotnet test --filter "FullyQualifiedName~ClassName"`.
- **Solution:** `RiscASDK.slnx` (new XML solution format, not Visual Studio `.sln`).
- **Native build:** `RiscA.Assembler` and `RiscA.Rasim` have `<PublishAot>true</PublishAot>` (no runtime
  dependency, ~2 MB exe each); `RiscA.Core` has `<IsAotCompatible>true</IsAotCompatible>`.
  Use `.\build.ps1` (Windows)
  / `./build.sh` (Linux/macOS) at repo root — they publish AOT binaries into `artifacts/<rid>/`
  and pack one SDK archive per RID: `artifacts/risca-sdk-<version>-<rid>.zip` — published tools at the
  archive root plus `doc/` (repo docs) and `lib/` (from `samples/lib`). AOT cannot be cross-built between OSes:
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
  `rasm --version` prints `rasm <major.minor.patch>`, `rasim --version` prints `rasim <major.minor.patch>`.
- **CI:** `.github/workflows/release.yml` — push to `main` (or manual run) → version → test →
  build matrix (win-x64, win-arm64, linux-x64, linux-arm64, osx-arm64) → GitHub Release tagged `v<version>`.
  The `release` job writes `notes.md` from `git log` of the commits since the previous release
  (`gh release create --notes-file` / `gh release edit` on a rerun) — release notes are generated, never hand-written.
- No lint/formatter config.
- **Comments:** code must be self-documenting — keep only comments that state what the code cannot
  say, one line each; never restate a name, a pattern or the grammar. Apply this in every change.
- **Workflow:** day-to-day work goes through `dev`: branch off `dev`, open the pull request
  against it (`gh pr create --base dev`); a `dev` → `main` pull request is opened manually by
  the owner, and only that push to `main` triggers a release. Never commit directly to `main`
  or `dev`.
- **CI:** `.github/workflows/ci.yml` runs `dotnet test` on pull requests into `dev` and on
  pushes to `dev`; the release (`.github/workflows/release.yml`) fires only on a push to `main`.
- All projects: `<ImplicitUsings>enable</ImplicitUsings>`, `<Nullable>enable</Nullable>`.
- **ISA docs:** `doc/RiscA.MD` is the spec and authority (a copy of the spec from the RTL repo
  `dima-umkk/risca`, with our own fixes applied — stale instruction lists, `func1` label, branch
  range, typos; when syncing with upstream, re-apply those fixes, never edit the upstream repo
  from this SDK). `doc/instruction-set.md` is the detailed encoding reference (bit layouts,
  examples, limits) with the ABI section. `doc/README.md` is a slim navigator: a map of the
  architecture docs plus a one-screen cheat sheet (opcode table, ABI, assembler directives,
  quick start).
- **`Instruction` struct** in `ISA/Instruction.cs` — immutable `readonly struct` with `(mask, shift)` tuple fields and `with*()` builder methods.
  LINK: `CALL` (imm and reg forms) sets `R14 = address + 2`, `ret` is `RET R14` (`PC = regs[14]`) — a non-leaf
  function saves/restores R14 itself (`samples/lib/uart.rasm`); the spec `EPC` is a separate register the
  model only moves with `MOV`. `CALL_IMM` computes its target from a word-aligned base:
  `((address >> 2) << 2) + ImmCallJr * 4`.
- **Assembler CLI** (`src/RiscA.Assembler/Program.cs`): `rasm -i <file.rasm>` plus optional `-v` (verbose), `-vp` (parser match debug), `-vi` (instruction dump). Sample: `samples/base.test.rasm`.
- **Assembler directives** (`src/RiscA.Core/Asm/Preprocessor.cs`, runs before the two assembler passes):
  `.equ NAME, value` (integer constant in decimal/`0x`/`0b` or a number expression, substituted into any
  number position) and `.include "file"` (spliced in, resolved relative
  to the including file first, each file included once, cycles ignored). A constant is defined the moment
  its `.equ` is spliced in, but substitution happens in the parser, after every file has been spliced, so a
  library included at the end of a file still provides its constants to the
  code above it (the image starts with the program body, so libraries go last — see `samples/uart.echo.rasm` and
  `samples/lib/uart.rasm`). Directive names are tokenized by the tokenizer (`TK.EQU`/`TK.INCLUDE`,
  any other `.name` → `TK.DOTNAME`; `TK.FUNC`/`TK.END` are parser tokens, not preprocessor ones) and `Preprocessor.directiveRules`
  describes the directive grammar in full — expression rules plus complete argument patterns — through the parser's
  rule engine (`Parser.ProcessToken<TCtx>`, shared via `RuleHandler<TCtx>` in `Asm/Rule.cs`); a line the grammar does
  not accept is ordinary code, an unknown `.name` errors there. `.func` is **not** a preprocessor directive:
  the line stays ordinary code, the parser rule `ParseFuncDirective` marks it (`ParsedInstruction.IsFunc`) and the
  assembler owns the entry and its `NOP` word alignment. Directive syntax and caveats are documented in `doc/README.md` ("Директивы ассемблера").
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
  The accepted **A2** plan (section 7). **A2.1 is implemented**: `.end` token and rule (`TK.END`,
  `ParsedInstruction.IsEnd`), `.func name(a, b)` parameter parsing (`ParseFuncParams`, `FuncParams`;
  the name becomes the entry label), `ret x` expansion (`ParseRetReg`: `mov r0, x` + `RET R14`), and
  `Asm/Regions.cs` — a pre-parse region scan (boundaries `.func` … `.end`/next `.func`/EOF, every 7.2
  prohibition of a named region reported by name) returning a `List<Regions.RegionInfo>`;
  **A2.2 is implemented**: `Asm/Frame.cs` lowers a named region before pass A — every name gets one
  4-byte frame slot, the `.func` line grows a prologue (`SUB R15`, the saves, the parameters from
  r0..r5 by position), body lines are rewritten to physical tokens (slot accesses over the scratches
  r7, then r6, which r6 is therefore forbidden in the body until the allocator), and one epilogue
  per region (label `@retN`, restore, `ADD R15`, `RET R14`) is inserted after the last body line;
  every `ret x` moves its value to r0 and jumps there. Interim until A2.3: every name lives in its
  slot (the frame is capped at 7 slots, the LDW/STW word-offset range), and the prologue saves R14
  plus the actually clobbered r8..r13 only when the body calls; `li`/`la` with a name operand are
  errors waiting for the allocator. Physical-only sources assemble byte-identically. The rest of
  A2 is not implemented: `call f(a, b)` argument-list sugar (permutation via r6/r7; >6 args on the
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
  addresses, UART0 = 4 registers, no interrupts yet, one `rasim` CLI, symbol map format, milestones M0…M4).
  The emulator core (milestone M0) and the console uart layer (M1) live in `src/RiscA.Core/HW`, the
  single-entry `rasim` CLI (M1 run, M2 debugger REPL) in `src/RiscA.Rasim`; the M2 symbol map
  (`rasm -s`, labels in debugger commands) and the compiler are not implemented yet. The spec `doc/RiscA.MD` is the authority, `artifacts/rtl/`
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
  The compiler is not implemented yet.
- **Emulator core** (`src/RiscA.Core/HW`, namespace `RiscA.Core.HW`, block-style namespaces):
  one flat `Bus` — a list of `IDevice`; everything is a device (`Ram`, `Rom`, `UartDevice`,
  test fakes), each decoding its own absolute addresses: `Name`, `Contains(address, length)`,
  `ReadByte/WriteByte/ReadWord/WriteWord`; `Bus.AddDevice(device)` is fluent, `Bus` composes
  halfwords from two byte accesses and reports `RegionName`. `Bus.Options`: `ResetVector`
  (null = base of the first `Ram`, `Ram.DefaultBase` = `0x0001_0000`) + `Permissive`. No
  `Tick()` and no device timing — devices advance when the program looks at them.
  `Bus.FaultKind`: `Unmapped`, `Unaligned`, `ReadOnly` (a write to `Rom`). `Ram` allows
  unaligned words (`DefaultSize` = 64 KiB); `Rom` takes a `byte[]` or a file path. `CPU`
  holds its own 16 registers (`cpu[i]` indexer, `cpu.EPC`, `CPU.ResultRegister/
  LinkRegister/StackPointer`). `CPU.Step()`/`Run(limit)` retire one
  instruction at a time; a `Bus.Fault`
  or an illegal instruction does not retire, the `PC` stays on the faulting instruction and the machine
  halts. `CPU` also owns the breakpoint addresses (`AddBreakpoint`/`RemoveBreakpoint`/`HasBreakpoint`,
  `Breakpoints`): `Run` checks them after each retire and stops with `StopReason.Breakpoint` before
  executing the instruction PC arrived at — continuing runs past the breakpoint; `Step` ignores them.
  `StopReason`: `Running`, `Reti` (stop convention), `IntNotImplemented`, `IllegalInstruction`,
  `BusFault`, `InstructionLimit`, `Breakpoint`, `Halted`. `OnRetire` (`Action<StepResult>`) is the trace hook, called
  for retired steps only. The library never touches `System.Console` — `UartDevice` takes
  `Stream?` input/output; there is no `IConsole` and no `Registers` class.
- **Console uart** (`src/RiscA.Core/HW/UartDevice.cs`): `UartDevice : IDevice` at `UartDevice.DefaultBase`
  (0x0000_0000), four word registers — `rx_data` (slot 0, a read pops the receive fifo, a write injects a
  byte — scripted input in tests), `rx_ready` (slot 1), `tx_data` (slot 2, a write emits the low byte),
  `tx_ready` (slot 3, always `1` — no baud/transmitter timing in the model). The receive fifo is 16 bytes
  deep; the model pulls one byte from the input `Stream` only when the fifo is empty and the program looks
  at `rx_ready`/`rx_data`, and EOF latches forever. Byte access is a read-modify-write of the whole
  register (the `STB` into `tx_data` trap). No `Tick`, no `IConsole`, no `InputWaitDetector` — a program
  waiting for input that will not come is caught by `--limit`.
- **rasim CLI** (`src/RiscA.Rasim/`): one entry, no subcommands — `rasim <prog.bin> [options]`
  (`--origin`, `--reset`, `--stdin <file>`, `--repl`, `--limit`, `--trace <file>`, `-v`, `--permissive`;
  `--help`/`--version`). Redirected stdin (or `--stdin`) runs the program to its end (auto mode, for tests
  and pipes); a terminal (or `--repl`) stops at the reset vector and opens the debugger REPL:
  `s [n]`, `c`, `b <addr>`, `bl`, `u <addr>`, `r`, `m <addr> [len]`, `h`, `q` — all REPL I/O goes to
  stderr. `disasm` is gone: showing the instruction at `PC` is the prompt's job. Breakpoints are numeric
  addresses for now; labels come with the M2 symbol map. The machine is built in code: default `Ram` +
  `UartDevice` wired to `Console.OpenStandardInput/Output` (or the `--stdin` file); a later device-settings
  file will describe several RAM regions and ROMs loaded from `rom.bin`. Program
  bytes go to stdout, every `rasim` message to stderr. Exit codes: 0 `reti`/quit, 1 usage or file error,
  2 instruction limit, 3 bus fault, 4 illegal instruction, 5 `INT`.
- **Verbose flags** (`src/RiscA.Core/Verbose.cs`): `Verbose.ParserMatches` (`-vp`) and `Verbose.AssemblerInstructions` (`-vi`) control debug output.
