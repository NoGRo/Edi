using PropertyChanged;

namespace Edi.Core
{
    //[AddINotifyPropertyChangedInterface]
    [UserConfig]
    public class UserAssistConfig
    {
        public string? Model { get; set; } = "gpt4-o";
        public string ApiEndpoint { get; set; } = "https://api.openai.com/v1";
        public string ApiKey { get; set; }
        public string? SessionId { get; set; }
    }
    public class AssistConfig
    {
        public string AssistantId { get; set; } 
        public double? Temperature { get; set; }
        public double? FrequencyPenalty { get; set; }
        public int? MaxTokens { get; set; }
        public int? MaxMesagesHistory { get; set; }
        public IEnumerable<string> Prompts { get; set; }
    }
}