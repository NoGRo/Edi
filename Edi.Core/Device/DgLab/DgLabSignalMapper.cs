using Edi.Core.Funscript.Command;
using Edi.Core.Funscript.FileJson;
using Edi.Core.Gallery.Funscript;

namespace Edi.Core.Device.DgLab;

public readonly record struct DgLabSignalFrame(
    int Power,
    DgLabWaveform Waveform,
    bool IsActive);

public static class DgLabSignalMapper
{
    public static DgLabSignalFrame Map(
        FunscriptGallery gallery,
        long time,
        DgLabChannelConfig configuration,
        int rangeMin,
        int rangeMax)
    {
        configuration.Normalize();
        var hasFrequencyAxis = gallery.AxesCommands.TryGetValue(
            Axis.Frequency,
            out var frequencyCommands);
        var defaultCommands = gallery.AxesCommands.GetValueOrDefault(
            Axis.Default);
        var frequencyCommand = FindCommand(
            hasFrequencyAxis ? frequencyCommands : defaultCommands,
            time);

        if (frequencyCommand is null)
            return Stopped();

        double frequencyRatio;
        if (hasFrequencyAxis)
        {
            frequencyRatio =
                InterpolateValue(frequencyCommand, time) / 100d;
        }
        else
        {
            if (frequencyCommand.Speed <= 0)
                return Stopped();

            frequencyRatio =
                frequencyCommand.Speed
                / (double)configuration.SpeedForMaximumFrequency;
        }

        frequencyRatio = Math.Clamp(frequencyRatio, 0d, 1d);
        var frequencyHz =
            configuration.FrequencyMinHz
            + (configuration.FrequencyMaxHz
               - configuration.FrequencyMinHz)
            * frequencyRatio;

        var volume = configuration.DefaultVolumePercent;
        if (gallery.AxesCommands.TryGetValue(
                Axis.Volume,
                out var volumeCommands))
        {
            var volumeCommand = FindCommand(volumeCommands, time);
            if (volumeCommand is not null)
            {
                volume = (int)Math.Round(
                    InterpolateValue(volumeCommand, time),
                    MidpointRounding.AwayFromZero);
            }
        }

        volume = Math.Clamp(volume, 0, 100);
        rangeMin = Math.Clamp(rangeMin, 0, 100);
        rangeMax = Math.Clamp(rangeMax, rangeMin, 100);
        var rangedVolume =
            rangeMin + (rangeMax - rangeMin) * volume / 100d;
        var power = (int)Math.Round(
            configuration.PowerMin
            + (configuration.PowerMax - configuration.PowerMin)
            * rangedVolume / 100d,
            MidpointRounding.AwayFromZero);

        if (power <= 0 || volume <= 0)
            return Stopped();

        var pulseWidth = configuration.PulseWidthMicroseconds;
        if (gallery.AxesCommands.TryGetValue(
                Axis.PulseWidth,
                out var pulseWidthCommands))
        {
            var pulseWidthCommand =
                FindCommand(pulseWidthCommands, time);
            if (pulseWidthCommand is not null)
            {
                pulseWidth = (int)Math.Round(
                    configuration.PulseWidthMicroseconds
                    * InterpolateValue(pulseWidthCommand, time)
                    / 100d,
                    MidpointRounding.AwayFromZero);
            }
        }

        return new DgLabSignalFrame(
            power,
            DgLabProtocol.FromFrequency(
                frequencyHz,
                pulseWidth),
            true);
    }

    private static CmdLinear FindCommand(
        IReadOnlyList<CmdLinear> commands,
        long time)
    {
        if (commands is null || commands.Count == 0)
            return null;

        return commands.FirstOrDefault(command =>
            command.AbsoluteTime >= time);
    }

    private static double InterpolateValue(
        CmdLinear command,
        long time)
    {
        if (command.Millis <= 0)
            return Math.Clamp(command.Value, 0d, 100d);

        var segmentStart = command.AbsoluteTime - command.Millis;
        var progress = Math.Clamp(
            (time - segmentStart) / (double)command.Millis,
            0d,
            1d);
        return Math.Clamp(
            command.InitialValue
            + (command.Value - command.InitialValue) * progress,
            0d,
            100d);
    }

    private static DgLabSignalFrame Stopped()
        => new(0, DgLabWaveform.Stopped, false);
}
