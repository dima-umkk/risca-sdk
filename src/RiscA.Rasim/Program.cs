using RiscA.Rasim;

//one command: the image is the first argument, no subcommands
if (args.Length == 0)
{
    Console.Error.WriteLine(Cli.Usage);
    return Cli.Exit.Usage;
}
if (args[0] is "--version" or "-V")
{
    Console.WriteLine($"rasim {Cli.Version}");
    return Cli.Exit.Ok;
}
if (args[0] is "--help" or "-h" or "help")
{
    Console.WriteLine(Cli.Usage);
    return Cli.Exit.Ok;
}
return RunCommand.Execute(args);
