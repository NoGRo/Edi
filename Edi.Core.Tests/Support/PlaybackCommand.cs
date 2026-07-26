namespace Edi.Core.Tests.Support;

internal enum PlaybackCommandKind
{
    Play,
    Stop
}

internal sealed record PlaybackCommand(
    long Sequence,
    PlaybackCommandKind Kind,
    string? GalleryName = null,
    long Seek = 0);
