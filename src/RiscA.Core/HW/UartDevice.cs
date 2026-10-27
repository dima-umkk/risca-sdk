using System;
using System.Collections.Generic;
using System.IO;

namespace RiscA.Core.HW
{
    //Console uart, polling only: slot0 rx_data pops the receive fifo, slot1 rx_ready, slot2 tx_data sends the low byte, slot3 tx_ready.
    public sealed class UartDevice : IDevice
    {
        public const string DeviceName = "uart0";
        //the console uart lives at the start of the device window, device block 0 of the ISA memory map
        public const uint DefaultBase = 0x0000_0000;

        //register numbers: [r+N] addresses word slot N
        public const int RxDataSlot = 0;
        public const int RxReadySlot = 1;
        public const int TxDataSlot = 2;
        public const int TxReadySlot = 3;

        //how many bytes the receive FIFO holds before the input stream is not asked for more
        public const int RxFifoDepth = 16;

        //four word registers
        public const int RegisterBytes = 4 * 4;

        private readonly Stream? _input;
        private readonly Stream? _output;
        private readonly Queue<byte> _rx = new Queue<byte>();
        private bool _inputEnded;

        public string Name { get; }
        public uint Base { get; }

        //how many bytes are waiting in the receive FIFO
        public int RxCount => _rx.Count;

        //null streams mean the side never moves: a write to rx_data still injects a byte (scripted input in tests)
        public UartDevice(Stream? input = null, Stream? output = null, string name = DeviceName, uint bas = DefaultBase)
        {
            Name = name;
            Base = bas;
            _input = input;
            _output = output;
        }

        public bool Contains(uint address, int length)
            => address >= Base && address - Base + (uint)length <= RegisterBytes;

        private int Offset(uint address, int length)
        {
            if (!Contains(address, length))
                throw new Bus.Fault(address, Bus.FaultKind.Unmapped,
                    $"{length} byte access at 0x{address:X8} is outside the registers of {Name} (0x{Base:X8}..0x{Base + RegisterBytes - 1:X8})");
            return (int)(address - Base);
        }

        //the input stream is asked only when the program looks, one byte per look
        private void Pull()
        {
            if (_rx.Count > 0 || _inputEnded || _input is null)
                return;
            int c = _input.ReadByte();
            if (c >= 0)
                _rx.Enqueue((byte)c);
            else
                _inputEnded = true;
        }

        public uint ReadWord(uint address)
        {
            int off = Offset(address, 4);
            if (off % 4 != 0)
                throw new Bus.Fault(address, Bus.FaultKind.Unaligned,
                    $"word access at 0x{address:X8} is not aligned to a register of {Name}");
            switch (off / 4)
            {
                case RxDataSlot:
                    //an empty FIFO reads as 0, that is the agreed convention, not random data
                    Pull();
                    return _rx.Count > 0 ? _rx.Dequeue() : 0u;
                case RxReadySlot:
                    Pull();
                    return _rx.Count > 0 ? 1u : 0u;
                case TxReadySlot:
                    return 1u;        //the transmitter never sleeps: there is no wire timing in the model
                default:
                    return 0u;        //tx_data has no meaningful read
            }
        }

        public void WriteWord(uint address, uint value)
        {
            int off = Offset(address, 4);
            if (off % 4 != 0)
                throw new Bus.Fault(address, Bus.FaultKind.Unaligned,
                    $"word access at 0x{address:X8} is not aligned to a register of {Name}");
            switch (off / 4)
            {
                case RxDataSlot:
                    //writing rx_data injects a byte: the scripted input of tests and scripts
                    if (_rx.Count < RxFifoDepth)
                        _rx.Enqueue((byte)value);
                    break;
                case TxDataSlot:
                    _output?.WriteByte((byte)value);
                    break;
                //rx_ready and tx_ready are read only, writing them does nothing
            }
        }

        //a byte access is a read modify write of the whole register, so a character must go through the word like in samples/lib/uart.rasm
        public byte ReadByte(uint address)
        {
            int off = Offset(address, 1);
            return (byte)(ReadWord(Base + (uint)(off & ~3)) >> (8 * (off & 3)));
        }

        public void WriteByte(uint address, byte value)
        {
            int off = Offset(address, 1);
            uint reg = Base + (uint)(off & ~3);
            uint merged = (ReadWord(reg) & ~(0xFFu << (8 * (off & 3)))) | ((uint)value << (8 * (off & 3)));
            WriteWord(reg, merged);
        }
    }
}
