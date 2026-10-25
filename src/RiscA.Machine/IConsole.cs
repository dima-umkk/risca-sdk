using System;

namespace RiscA.Machine
{
    //Where the console UART of the model sends its bytes and takes them from. The library never
    //touches System.Console, the rsim CLI and the debugger own the process input and output
    //(doc/emulator-architecture.md, §5).
    public interface IConsole
    {
        //the next input byte, or -1 when there is nothing to read. -1 covers both "no key yet"
        //and "the input has ended", for the machine those two look the same: the receive FIFO
        //stays empty. InputEnded tells which one it is, so rsim can tell an interactive wait
        //from a program that spins forever on a closed stdin.
        int ReadChar();

        //true once the input has ended: ReadChar() will not return a byte anymore
        bool InputEnded { get; }

        //send one byte, only the low 8 bits of value are used
        void WriteChar(int value);

        //push whatever is buffered out, called when the machine stops
        void Flush();
    }
}
