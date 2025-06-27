using PropertyChanged;

namespace Edi.Core
{
    //[AddINotifyPropertyChangedInterface]
    public class AssistConfig
    {
        public string? Model { get;  set; } = "gpt4-o";
        public object ApiEndpoint { get; set; } = "https://api.openai.com/v1";
        public object ApiKey { get; set; } 
        public object AssistantId { get; set; } 
        public string? SessionId { get; set; }

        public int? MaxTokens { get;  set; }
        public double? Temperature { get; set; }
        public double? FrequencyPenalty { get; set; }
        public int? MaxHistory { get; set; }
        public IEnumerable<string> Prompts { get; set; }
    }
}