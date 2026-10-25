using RiscA.Machine;
using System;
using System.IO;

namespace RiscA.Rsim
{
    //The console of the running process: the half of IConsole the emulator library refuses to own
    //(doc/emulator-architecture.md, §5). The program writes its bytes to stdout as they are, so a
    //redirect stays byte exact, while everything rsim itself says goes to stderr.
    internal sealed class HostConsole : IConsole, IDisposable
    {
        private readonly Stream _input;
        private readonly Stream _output;
        private readonly bool _interactive;
        private bool _eof;

        public bool InputEnded => _eof;

        private HostConsole(Stream input, Stream output, bool interactive)
        {
            _input = input;
            _output = output;
            _interactive = interactive;
        }

        //a session at the terminal: a byte is only there once a key is actually pressed, because the
        //machine keeps ticking while the person is thinking
        public static HostConsole Terminal()
            => new(Console.OpenStandardInput(), Console.OpenStandardOutput(), !Console.IsInputRedirected);

        //scripted input, the byte for byte version of what the person would have typed
        public static HostConsole FromFile(string path)
            => new(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read),
                Console.OpenStandardOutput(), false);

        //the byte the console has for the uart, or -1 when there is nothing to read right now
        public int ReadChar()
        {
            if (_eof)
                return -1;

            if (_interactive)
            {
                if (!Console.KeyAvailable)
                    return -1;              //not typed yet: legitimate, and the input has not ended
                char key = Console.ReadKey(true).KeyChar;
                if (key == '\0')
                    return -1;              //arrow keys and friends are not bytes
                return key == '\r' ? '\n' : key;    //the terminal sends CR, the program expects LF
            }

            int b = _input.ReadByte();
            if (b < 0)
                _eof = true;
            return b;
        }

        public void WriteChar(int value) => _output.WriteByte((byte)value);

        public void Flush()
        {
            try
            {
                _output.Flush();
            }
            catch (IOException)
            {
                //the output is gone (a closed pipe); the run report still has to be printed
            }
        }

        public void Dispose()
        {
            _input.Dispose();
            Flush();
        }
    }
}
