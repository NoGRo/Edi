using Edi.Core.Device.DgLab;

namespace Edi.Core.Tests.DgLab;

public class DgLabProtocolTests
{
    [Fact]
    public void PowerPacketUsesOfficialBitLayoutAndLittleEndian()
    {
        Assert.Equal(
            [0x02, 0x08, 0x00],
            DgLabProtocol.EncodePower(channelA: 1, channelB: 2));
    }

    [Fact]
    public void WaveformPacketMatchesOfficialBreathingExample()
    {
        Assert.Equal(
            [0x21, 0x01, 0x0A],
            DgLabProtocol.EncodeWaveform(
                new DgLabWaveform(X: 1, Y: 9, Z: 20)));
    }

    [Fact]
    public void FrequencyConversionProducesRequestedPeriodAndSafeWidth()
    {
        var waveform = DgLabProtocol.FromFrequency(
            frequencyHz: 100,
            pulseWidthMicroseconds: 500);

        Assert.Equal(10, waveform.X + waveform.Y);
        Assert.Equal(20, waveform.Z);
    }
}
