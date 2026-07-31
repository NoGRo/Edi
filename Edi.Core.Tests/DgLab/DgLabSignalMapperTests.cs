using Edi.Core.Device.DgLab;
using Edi.Core.Funscript.Command;
using Edi.Core.Funscript.FileJson;
using Edi.Core.Gallery.Funscript;

namespace Edi.Core.Tests.DgLab;

public class DgLabSignalMapperTests
{
    [Fact]
    public void DefaultFunscriptMapsMovementSpeedToFrequency()
    {
        var gallery = Gallery(
            Axis.Default,
            Segment(from: 0, to: 100, duration: 1000));
        var configuration = new DgLabChannelConfig
        {
            PowerMin = 500,
            PowerMax = 1500,
            FrequencyMinHz = 1,
            FrequencyMaxHz = 100,
            SpeedForMaximumFrequency = 100
        };

        var frame = DgLabSignalMapper.Map(
            gallery,
            time: 500,
            configuration,
            rangeMin: 0,
            rangeMax: 100);

        Assert.True(frame.IsActive);
        Assert.Equal(1500, frame.Power);
        Assert.Equal(10, frame.Waveform.X + frame.Waveform.Y);
    }

    [Fact]
    public void ReservedAxesControlFrequencyAndCalibratedPower()
    {
        var gallery = Gallery(
            Axis.Frequency,
            Segment(from: 0, to: 100, duration: 1000));
        gallery.AxesCommands[Axis.Volume] =
            Segment(from: 0, to: 50, duration: 1000);
        gallery.AxesCommands[Axis.PulseWidth] =
            Segment(from: 0, to: 50, duration: 1000);
        var configuration = new DgLabChannelConfig
        {
            PowerMin = 100,
            PowerMax = 1100,
            FrequencyMinHz = 1,
            FrequencyMaxHz = 100
        };

        var frame = DgLabSignalMapper.Map(
            gallery,
            time: 500,
            configuration,
            rangeMin: 0,
            rangeMax: 100);

        Assert.True(frame.IsActive);
        Assert.Equal(350, frame.Power);
        Assert.InRange(
            frame.Waveform.X + frame.Waveform.Y,
            19,
            21);
        Assert.Equal(5, frame.Waveform.Z);
    }

    [Fact]
    public void NoMovementMutesOrdinaryFunscript()
    {
        var gallery = Gallery(
            Axis.Default,
            Segment(from: 50, to: 50, duration: 1000));
        var configuration = new DgLabChannelConfig
        {
            PowerMax = 1000
        };

        var frame = DgLabSignalMapper.Map(
            gallery,
            time: 500,
            configuration,
            rangeMin: 0,
            rangeMax: 100);

        Assert.False(frame.IsActive);
        Assert.Equal(0, frame.Power);
        Assert.Equal(DgLabWaveform.Stopped, frame.Waveform);
    }

    [Fact]
    public void EdiIntensityScalesVolumeWithoutChangingWaveform()
    {
        var gallery = Gallery(
            Axis.Frequency,
            Segment(from: 50, to: 50, duration: 1000));
        gallery.AxesCommands[Axis.Volume] =
            Segment(from: 100, to: 100, duration: 1000);
        var configuration = new DgLabChannelConfig
        {
            PowerMin = 0,
            PowerMax = 2000,
            FrequencyMinHz = 1,
            FrequencyMaxHz = 100,
            PulseWidthMicroseconds = 100
        };

        var fullIntensity = DgLabSignalMapper.Map(
            gallery,
            time: 500,
            configuration,
            rangeMin: 0,
            rangeMax: 100);
        var halfIntensity = DgLabSignalMapper.Map(
            gallery,
            time: 500,
            configuration,
            rangeMin: 0,
            rangeMax: 50);

        Assert.Equal(2000, fullIntensity.Power);
        Assert.Equal(1000, halfIntensity.Power);
        Assert.Equal(fullIntensity.Waveform, halfIntensity.Waveform);
    }

    private static FunscriptGallery Gallery(
        Axis axis,
        List<CmdLinear> commands)
        => new()
        {
            Duration = 1000,
            AxesCommands =
            {
                [axis] = commands
            }
        };

    private static List<CmdLinear> Segment(
        int from,
        int to,
        int duration)
    {
        var initial = new CmdLinear
        {
            AbsoluteTime = 0,
            Value = from
        };
        var target = new CmdLinear
        {
            AbsoluteTime = duration,
            Millis = duration,
            Value = to,
            Prev = initial
        };
        initial.Next = target;
        return [initial, target];
    }
}
