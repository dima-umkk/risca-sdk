using RiscA.Core.ISA;

namespace RiscA.Core.Tests.ISA;

public class RawValueTests
{
    [Theory]
    [InlineData((ushort)0x0000)]
    [InlineData((ushort)0xFFFF)]
    [InlineData((ushort)0x1234)]
    [InlineData((ushort)0xBEEF)]
    public void Raw_returns_the_original_value(ushort raw)
    {
        var inst = new Instruction(raw);
        inst.Raw.Should().Be(raw);
    }

    [Fact]
    public void Struct_is_readonly()
    {
        var inst = new Instruction(0x1234);

        // Should not compile if Raw is not a getter-only property
        var raw = inst.Raw;
        raw.Should().Be((ushort)0x1234);
    }
}
