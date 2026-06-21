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
- **No CI, no lint/formatter config, no `Directory.Build.props`.**
- All projects: `<ImplicitUsings>enable</ImplicitUsings>`, `<Nullable>enable</Nullable>`.
- **ISA docs:** `doc/RiscA.MD` (spec) and `doc/README.md` (encoding reference with examples & limits).
- **`Instruction` struct** in `ISA/Instruction.cs` — immutable `readonly struct` with `(mask, shift)` tuple fields and `with*()` builder methods.
- **Assembler CLI** (`src/RiscA.Assembler/Program.cs`): `rasm -i <file.rasm>` plus optional `-v` (verbose), `-vp` (parser match debug), `-vi` (instruction dump). Sample: `samples/base.test.rasm`.
- **Verbose flags** (`src/RiscA.Core/Verbose.cs`): `Verbose.ParserMatches` (`-vp`) and `Verbose.AssemblerInstructions` (`-vi`) control debug output.
