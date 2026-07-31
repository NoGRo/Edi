namespace Edi.Core.Device.DgLab;

public enum DgLabChannel
{
    A,
    B
}

public readonly record struct DgLabWaveform(int X, int Y, int Z)
{
    public static DgLabWaveform Stopped => new(0, 0, 0);
}

public static class DgLabProtocol
{
    public static byte[] EncodePower(int channelA, int channelB)
    {
        channelA = Math.Clamp(
            channelA,
            0,
            DgLabChannelConfig.MaximumPower);
        channelB = Math.Clamp(
            channelB,
            0,
            DgLabChannelConfig.MaximumPower);
        return EncodeLittleEndian24((channelA << 11) | channelB);
    }

    public static byte[] EncodeWaveform(DgLabWaveform waveform)
    {
        var x = Math.Clamp(waveform.X, 0, 31);
        var y = Math.Clamp(waveform.Y, 0, 1023);
        var z = Math.Clamp(waveform.Z, 0, 31);
        return EncodeLittleEndian24((z << 15) | (y << 5) | x);
    }

    public static DgLabWaveform FromFrequency(
        double frequencyHz,
        int pulseWidthMicroseconds)
    {
        var period = (int)Math.Round(
            1000d / Math.Clamp(frequencyHz, 1d, 100d),
            MidpointRounding.AwayFromZero);
        period = Math.Clamp(period, 10, 1000);

        var x = (int)Math.Round(
            Math.Sqrt(period / 1000d) * 15d,
            MidpointRounding.AwayFromZero);
        x = Math.Clamp(x, 1, Math.Min(31, period));
        var y = period - x;
        var z = Math.Clamp(
            pulseWidthMicroseconds / 5,
            0,
            DgLabChannelConfig.MaximumSafePulseWidthMicroseconds / 5);
        return new DgLabWaveform(x, y, z);
    }

    private static byte[] EncodeLittleEndian24(int value)
        =>
        [
            (byte)value,
            (byte)(value >> 8),
            (byte)(value >> 16)
        ];
}
