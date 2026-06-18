## RiscA SDK

Two projects in `src/`:

| Project | Type | Entry point |
|---------|------|-------------|
| `RiscA.Core` | Library | `src/RiscA.Core/` — ISA structs (OpCode, Instruction) in `ISA/` namespace |
| `RiscA.Assembler` | CLI | `src/RiscA.Assembler/Program.cs` — references Core |

## Essentials

- Builds with: dotnet build — .NET **10.0** target on both projects. Do not downgrade.
- All projects use ImplicitUsings + Nullable enabled.
- No test project exists; no CI, no lint/config files beyond the default .gitignore.
- The RISC-A architecture specification is in `doc/RiscA.MD` — consult it before implementing ISA logic.
- `RiscASDK.slnx` lists projects and documentation folders (not a Visual Studio solution).
