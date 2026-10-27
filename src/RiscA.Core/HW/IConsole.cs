using System;

namespace RiscA.Core.HW
{
    //Where the console uart reads and writes; the library never touches System.Console.
    public interface IConsole
    {
        //the next input byte or -1; InputEnded tells "not yet" from "never"
        int ReadChar();

        //true once the input has ended: ReadChar() will not return a byte anymore
        bool InputEnded { get; }

        //send one byte, only the low 8 bits of value are used
        void WriteChar(int value);

        //push whatever is buffered out, called when the machine stops
        void Flush();
    }
}
