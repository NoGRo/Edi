using System;
using System.Diagnostics;

namespace Edi.Core
{
    public static class StaticTimeWatch
    {
        private static readonly Stopwatch StopwatchInstance = new Stopwatch();

        // Method to start the stopwatch
        public static void Start()
        {
            if (!StopwatchInstance.IsRunning)
            {
                StopwatchInstance.Start();
            }
        }    
        // Method to stop the stopwatch
        public static long GetElapse()
        {
            if (!StopwatchInstance.IsRunning)
            {
                return 0;
            }
            StopwatchInstance.Stop();
            return StopwatchInstance.ElapsedMilliseconds;

        }
    }
}
