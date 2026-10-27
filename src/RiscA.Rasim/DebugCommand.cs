namespace RiscA.Rasim
{
    //Milestone M2 debugger: not implemented yet, the command says so instead of pretending to be run.
    internal static class DebugCommand
    {
        public static int Execute(string[] args)
        {
            Cli.Error("debug is not implemented yet, it is milestone M2 of doc/emulator-architecture.md");
            Cli.Error("run gives everything M2 will need to be built on top of: CPU.Step(), the OnRetire trace hook and the -v listing");
            return Cli.Exit.Usage;
        }
    }
}
