namespace Edi.Core.Device;

public static class DeviceOffset
{
    public const int MinimumMilliseconds = -2000;
    public const int MaximumMilliseconds = 2000;
    public const int StepMilliseconds = 10;

    public static int Normalize(int value)
    {
        var clamped = Math.Clamp(
            value,
            MinimumMilliseconds,
            MaximumMilliseconds);
        var rounded = clamped >= 0
            ? ((clamped + StepMilliseconds / 2) / StepMilliseconds)
                * StepMilliseconds
            : ((clamped - StepMilliseconds / 2) / StepMilliseconds)
                * StepMilliseconds;

        return Math.Clamp(
            rounded,
            MinimumMilliseconds,
            MaximumMilliseconds);
    }
}
