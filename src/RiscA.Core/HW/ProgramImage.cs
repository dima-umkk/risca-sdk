using RiscA.Core.ISA;
using System;
using System.Collections.Generic;

namespace RiscA.Core.HW
{
    //Loads one image at one origin into the flat bus, like rasm -o writes it.
    public static class ProgramImage
    {
        public static int Load(Bus space, uint origin, ReadOnlySpan<byte> image)
        {
            if (image.IsEmpty)
                return 0;
            space.CheckRange(origin, image.Length);
            for (int i = 0; i < image.Length; i++)
                space.WriteByte(origin + (uint)i, image[i]);
            return image.Length;
        }

        //build an image out of instructions, the short way to write a test
        public static int Load(Bus space, uint origin, IReadOnlyList<Instruction> code)
        {
            var bytes = new byte[code.Count * 2];
            for (int i = 0; i < code.Count; i++)
            {
                bytes[i * 2] = (byte)code[i].Raw;
                bytes[i * 2 + 1] = (byte)(code[i].Raw >> 8);
            }
            return Load(space, origin, bytes.AsSpan());
        }
    }
}
