using RiscA.Rsim;
using System;

//rsim: the one CLI of the emulator - run, disasm and, later, debug
//(doc/emulator-architecture.md, §7: one library and one tool, never a tool per feature).
if (args.Length == 0)
{
    Console.Error.WriteLine(Cli.Usage);
    return Cli.Exit.Usage;
}

switch (args[0])
{
    case "run":
        return RunCommand.Execute(args);
    case "disasm":
        return DisasmCommand.Execute(args);
    case "debug":
        return DebugCommand.Execute(args);
    case "--version":
    case "-V":
        Console.WriteLine($"rsim {Cli.Version}");
        return Cli.Exit.Ok;
    case "--help":
    case "-h":
    case "help":
        Console.WriteLine(Cli.Usage);
        return Cli.Exit.Ok;
    default:
        Cli.Error($"unknown command '{args[0]}'");
        Console.Error.WriteLine(Cli.Usage);
        return Cli.Exit.Usage;
}
