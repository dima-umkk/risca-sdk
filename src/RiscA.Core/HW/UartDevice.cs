using System;
using System.Collections.Generic;

namespace RiscA.Core.HW
{
    //Console uart, polling only: slot0 rx_data pops the rx fifo, slot1 rx_ready, slot2 tx_data sends the low byte, slot3 tx_ready.
    public sealed class UartDevice : IDevice
    {
        public const string DeviceName = "uart0";
        //the block number, block 0 is the first one in the device window
        public const int Block = 0;

        //register numbers, [r+N] addresses word slot N (the bus shifts the offset by 2)
        public const int RxDataSlot = 0;
        public const int RxReadySlot = 1;
        public const int TxDataSlot = 2;
        public const int TxReadySlot = 3;

        //how many bytes the receive FIFO holds before the console is not asked for more
        public const int RxFifoDepth = 16;

        private readonly IConsole _console;
        private readonly int _txCyclesPerByte;
        private readonly Queue<byte> _rx = new Queue<byte>();
        //retired instructions the current transmitted byte still occupies, 0 means ready
        private int _txBusy;
        //how many times in a row the program asked rx_ready and got 0, any arriving byte resets it
        private int _emptyReadyReads;

        public string Name => DeviceName;
        public int Size => 4 * 4;

        //a byte is waiting for the program, either in the FIFO or not read out yet
        public bool RxAvailable => _rx.Count > 0;
        //how many bytes are still in the receive FIFO
        public int RxCount => _rx.Count;
        //rx_ready polls that answered "nothing yet" since the last byte arrived, used by InputWaitDetector
        public int EmptyReadyReads => _emptyReadyReads;

        //txCyclesPerByte=0 sends instantly; a positive value is the wire time of one byte in retired instructions (rasim --baud)
        public UartDevice(IConsole console, int txCyclesPerByte = 0)
        {
            _console = console;
            _txCyclesPerByte = txCyclesPerByte < 0 ? 0 : txCyclesPerByte;
        }

        public uint ReadWord(int offset)
        {
            switch (offset)
            {
                case RxDataSlot * 4:
                    //an empty FIFO reads as 0, that is the agreed convention, not random data
                    if (_rx.Count == 0)
                        return 0u;
                    _emptyReadyReads = 0;
                    return _rx.Dequeue();
                case RxReadySlot * 4:
                    if (_rx.Count > 0)
                    {
                        _emptyReadyReads = 0;
                        return 1u;
                    }
                    _emptyReadyReads++;
                    return 0u;
                case TxReadySlot * 4:
                    //the transmitter is ready when the current byte is off the wire
                    return _txBusy == 0 ? 1u : 0u;
                default:
                    //tx_data has no meaningful read, it is the write side of the register
                    return 0u;
            }
        }

        public void WriteWord(int offset, uint value)
        {
            switch (offset)
            {
                case RxDataSlot * 4:
                    //writing rx_data injects a byte: the scripted input of tests and scripts
                    if (_rx.Count < RxFifoDepth)
                        Push((byte)value);
                    break;
                case TxDataSlot * 4:
                    //a write while busy is a programming error: the byte is dropped so the gap is visible
                    if (_txBusy == 0)
                    {
                        _console.WriteChar((byte)value);
                        _txBusy = _txCyclesPerByte;
                    }
                    break;
                default:
                    //rx_ready and tx_ready are read only, writing them does nothing
                    break;
            }
        }

        public void Tick()
        {
            if (_txBusy > 0)
                _txBusy--;

            if (_rx.Count < RxFifoDepth)
            {
                //at most one console byte per retired instruction; a full fifo stops asking the console
                int c = _console.ReadChar();
                if (c >= 0)
                    Push((byte)c);
            }
        }

        //one byte arrives, from the console or written by the program itself
        private void Push(byte value)
        {
            _rx.Enqueue(value);
            _emptyReadyReads = 0;
        }
    }
}
