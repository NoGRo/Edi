using System.Collections.Concurrent;
// ...existing code...
namespace Edi.Core.Players
{
    public class PlayerLogService
    {
        private readonly ConcurrentQueue<string> _logQueue = new();
        public event Action<string> OnLogReceived;

        public void AddLog(string log)
        {
            _logQueue.Enqueue(log);
            OnLogReceived?.Invoke(log);
        }

        public IEnumerable<string> GetLogs()
        {
            while (_logQueue.TryDequeue(out var log))
                yield return log;
        }
    }
}
