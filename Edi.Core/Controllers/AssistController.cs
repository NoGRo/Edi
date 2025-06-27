using Edi.Core;
using Edi.Core.Device;
using Edi.Core.Device.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using static System.Net.WebRequestMethods;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using Edi.Core.Gallery;
using System.IO;

namespace Edi.Core.Controllers
{
    [Route("[controller]")]
    public class AssistController : Controller
    {
        private readonly IEdi _edi;
        private readonly AssistConfig _config;
        private readonly GalleryConfig _ediConfig;
        private readonly HttpClient _http;
        private static List<(string Role, string Content)> _sessionHistories = new();
        public record PromptRequest([Required]string Message, string Memory);
        public AssistController(IEdi edi)
        {
            _config = edi.ConfigurationManager.Get<AssistConfig>();
            _ediConfig = edi.ConfigurationManager.Get<GalleryConfig>(); 
            _http = new HttpClient
            {
                DefaultRequestHeaders = {
                        { "Authorization", $"Bearer {_config.ApiKey}" },
                        { "OpenAI-Beta", "assistants=v2" }
                    }
            };
            _edi = edi;
        }

        [HttpPost("assistant")]
        public async Task<IActionResult> SendToAssistant([FromBody] PromptRequest input)
        {
            if (string.IsNullOrWhiteSpace(input?.Message))
                return BadRequest("Prompt is empty.");

            string message = input.Message;
            string memory = input.Memory;


            // Crear thread si no hay
            if (string.IsNullOrEmpty(_config.SessionId))
            {
                var threadRes = await _http.PostAsync($"{_config.ApiEndpoint}/threads", new StringContent("{}", Encoding.UTF8, "application/json"));
                if (!threadRes.IsSuccessStatusCode) return StatusCode((int)threadRes.StatusCode, await threadRes.Content.ReadAsStringAsync());

                var json = await threadRes.Content.ReadAsStringAsync();
                _config.SessionId = JsonDocument.Parse(json).RootElement.GetProperty("id").GetString();
            }

            // Enviar mensaje
            var msg = JsonSerializer.Serialize(new { role = "user", content = message });
            var res1 = await _http.PostAsync($"{_config.ApiEndpoint}/threads/{_config.SessionId}/messages", new StringContent(msg, Encoding.UTF8, "application/json"));
            if (!res1.IsSuccessStatusCode) return StatusCode((int)res1.StatusCode, await res1.Content.ReadAsStringAsync());

            // Iniciar run
            var run = JsonSerializer.Serialize(new { assistant_id = _config.AssistantId });
            var res2 = await _http.PostAsync($"{_config.ApiEndpoint}/threads/{_config.SessionId}/runs", new StringContent(run, Encoding.UTF8, "application/json"));
            if (!res2.IsSuccessStatusCode) return StatusCode((int)res2.StatusCode, await res2.Content.ReadAsStringAsync());

            var runId = JsonDocument.Parse(await res2.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString();

            // Esperar hasta que termine
            string status = "queued";
            while (status is "queued" or "in_progress")
            {
                await Task.Delay(1000);
                var res = await _http.GetAsync($"{_config.ApiEndpoint}/threads/{_config.SessionId}/runs/{runId}");
                status = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("status").GetString();
            }

            // Obtener última respuesta
            var res3 = await _http.GetAsync($"{_config.ApiEndpoint}/threads/{_config.SessionId}/messages");
            var messages = JsonDocument.Parse(await res3.Content.ReadAsStringAsync()).RootElement.GetProperty("data");

            var assistantMsg = messages.EnumerateArray().FirstOrDefault(m => m.GetProperty("role").GetString() == "assistant");
            var content = assistantMsg.GetProperty("content")[0].GetProperty("text").GetProperty("value").GetString();

            return Ok(content?.Trim());
        }


        [HttpPost("prompt")]
        public async Task<IActionResult> SendPrompt([FromBody] PromptRequest input)
        {
            if (string.IsNullOrWhiteSpace(input?.Message))
                return BadRequest("Prompt is empty.");

            string message = input.Message;
            string memory = input.Memory;

            // Configurable: límite de historial
            int maxHistory = _config.MaxHistory ?? 10;

            // Inicializa si es necesario
            if (_sessionHistories == null)
                _sessionHistories = new List<(string Role, string Content)>();

            // Agrega el nuevo mensaje al historial
            _sessionHistories.Add(("user", message));

            var messages = new List<object>();

            if (_config.Prompts != null && _config.Prompts.Any())
            {
                foreach (var filename in _config.Prompts)
                {
                    var fullPath = Path.Combine(_ediConfig.GalleryPath, filename);
                    if (System.IO.File.Exists(fullPath))
                    {
                        var extraPrompt = await System.IO.File.ReadAllTextAsync(fullPath);
                        messages.Add(new { role = "system", content = extraPrompt });
                    }
                }
            }

            // Filtra últimos N mensajes de usuario + IA, ordenados
            var recent = _sessionHistories
                .Where(m => m.Role == "user" || m.Role == "assistant")
                .Reverse()
                .Take(maxHistory * 2)
                .Reverse();
            var chatCount = _sessionHistories.Count(m => m.Role == "user" || m.Role == "assistant");
            if (chatCount >= maxHistory)
            {
                int excess = chatCount + 1 - maxHistory;
                if (excess > 0)
                {
                    _sessionHistories.RemoveRange(0, excess);
                    _sessionHistories.Insert(0, ("system", $"Notice: {excess} messages removed (limit: {maxHistory})."));

                }
            }
            messages.AddRange(
               recent.Select(m => new { role = m.Role, content = m.Content })
           );

            // Agrega el memory como bloque separado
            if (!string.IsNullOrWhiteSpace(memory))
            {
                messages.Add(new { role = "system", content = $"==Session Notes==\n{memory}" });
            }

            var firstWord = message.Split(':', 2)[0].Trim();
            if (!string.IsNullOrEmpty(firstWord))
            {
                var commandMdPath = Path.Combine(_ediConfig.GalleryPath, $"{firstWord}.md");
                if (System.IO.File.Exists(commandMdPath))
                {
                    var commandMdContent = await System.IO.File.ReadAllTextAsync(commandMdPath);
                    if(!string.IsNullOrEmpty(commandMdContent))
                    messages.Add(new { role = "system", content = commandMdContent });
                }
            }

            var requestBody = new
            {
                model = _config.Model,
                messages,
                max_tokens = _config.MaxTokens ?? 2048,
                temperature = _config.Temperature ?? 0.8,
                //frequency_penalty = _config.FrequencyPenalty
            };




            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{_config.ApiEndpoint}/chat/completions", content);

            if (!response.IsSuccessStatusCode)
                return StatusCode((int)response.StatusCode, await response.Content.ReadAsStringAsync());

            var jsonString = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(jsonString);

            var reply = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString()
                ?.Trim();

            // Guarda respuesta de IA en el historial
            _sessionHistories.Add(("assistant", reply));

            return Ok(reply);
        }
    }
}
