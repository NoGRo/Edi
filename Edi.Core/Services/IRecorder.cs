namespace Edi.Core
{
    public interface IRecorder
    {
        RecorderConfig config { get; set; }
        bool IsRecording { get; set; }
        string CurrentChapter { get; }
        void AddChapter(string name, long seek = 0, int? addPointAtPosition = null);
        
        void AddPoint(int position, long seek = 0);
        void EndChapter(int? addPointAtPosition = null, long seek = 0);
        void Start();
        void Stop();
    }
}