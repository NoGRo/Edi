using Serilog;
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

            var _log = $"[{DateTime.Now:T}] {log}";
            _logQueue.Enqueue(_log);
            OnLogReceived?.Invoke(_log);
        }

        public IEnumerable<string> GetLogs()
        {
            while (_logQueue.TryDequeue(out var log))
                yield return log;
        }
    }
}
