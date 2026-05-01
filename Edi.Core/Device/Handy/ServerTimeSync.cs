using System.Text.Json;

namespace Edi.Core.Device.Handy
{
    public static class ServerTimeSync
    {
        private static double _estimatedAverageOffset = 0;
        private static double _estimatedAverageRtd = 0;
        public static long timeSyncAvrageOffset;
        public static long timeSyncAvragetRtd ;
        public static async Task<long> SyncServerTimeAsync()
        {
            var client = new HttpClient();

            client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            //_ =  await GetServerTimeAsync(); //warmup
            var syncTries = 30;
            var offsetAggregated = new List<double>();
            var RtdAggregated = new List<double>();
            for (int i = 0; i < syncTries; i++)
            {
                var tStart = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var response = await client.GetAsync("https://www.handyfeeling.com/api/handy-rest/v2/servertime");
                var tEnd = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var tServer = data.RootElement.GetProperty("serverTime").GetInt64();

                var tRtd = tEnd - tStart;
                var tOffset = tServer + tRtd / 2.0 - tEnd;
                offsetAggregated.Add(tOffset);
                RtdAggregated.Add(tRtd);
            }
            offsetAggregated.Sort();
            RtdAggregated.Sort();
            var trimmedOffsets = offsetAggregated.Skip(4).Take(offsetAggregated.Count - 8).ToList();
            var trimmedRtd = RtdAggregated.Skip(4).Take(RtdAggregated.Count - 8).ToList();

            // Calcular el promedio de los offsets sin los extremos
            _estimatedAverageOffset = Math.Round(trimmedOffsets.Average());
            timeSyncAvrageOffset = Convert.ToInt64(_estimatedAverageOffset);
            _estimatedAverageRtd = Math.Round(trimmedRtd.Average());
            timeSyncAvragetRtd = Convert.ToInt64(_estimatedAverageRtd);
            return timeSyncAvrageOffset;
        }

    }

}

