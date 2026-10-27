using System;

namespace RiscA.Core.HW
{
    //Reports the one hang worth catching: polling the console after the input has ended.
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

        //why it stopped, with the hint for the person running rasim
        public static string Hint(bool inputWasFile)
            => inputWasFile
                ? $"{StuckMessage}: the input file ends before the program gets its byte"
                : $"{StuckMessage} (stdin is closed). Pass --stdin <file> for scripted input, " +
                  "and just press the key when the session is interactive.";
    }
}
