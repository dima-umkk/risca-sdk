using System;

namespace RiscA.Machine
{
    //The one hang worth reporting instead of waiting for Ctrl+C: the program polls the console for
    //a byte while the input of the process has already ended ("doc/emulator-architecture.md", §5).
    //An interactive session where the user simply has not typed yet looks exactly the same, except
    //that its input has not ended, and there the wait is legitimate.
    public sealed class InputWaitDetector(UartDevice uart, IConsole console, int patience = 4096)
    {
        public const string StuckMessage =
            "the program is waiting for a byte from the console while the input has ended";

        private readonly UartDevice _uart = uart;
        private readonly IConsole _console = console;
        private readonly int _patience = patience < 1 ? 1 : patience;

        //checked after every retired instruction: true when polling the empty FIFO can no longer help
        public bool Stuck
            => _console.InputEnded
            && !_uart.RxAvailable
            && _uart.EmptyReadyReads >= _patience;

        //why it stopped, with the hint for the person running rsim
        public static string Hint(bool inputWasFile)
            => inputWasFile
                ? $"check the {StuckMessage}: the input file is shorter than the program expects"
                : $"{StuckMessage} (stdin is closed). Pass --stdin <file> for scripted input, " +
                  "and just press the key when the session is interactive.";
    }
}
