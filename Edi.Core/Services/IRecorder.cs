namespace Edi.Core
{
    public interface IRecorder
    {
        RecorderConfig config { get; set; }
        bool IsRecording { get; set; }

        void AddChapter(string name, long seek = 0, int? addPointAtPosition = null);
        void AddPoint(int position);
        void AdjustByFrames(int frameOffset);
        void EndChapter(int? addPointAtPosition = null);
        void StartRecord();
        void Stop();
    }
}