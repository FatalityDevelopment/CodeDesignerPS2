using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>One structured reference found by SCANREFERENCES.</summary>
    public class ReferenceEntry
    {
        [JsonPropertyName("address")]
        public string Address { get; set; } = string.Empty;

        [JsonPropertyName("addressValue")]
        public uint AddressValue { get; set; }

        [JsonPropertyName("kind")]
        public string Kind { get; set; } = string.Empty;

        [JsonPropertyName("kindLabel")]
        public string KindLabel { get; set; } = string.Empty;

        [JsonPropertyName("targetAddress")]
        public string TargetAddress { get; set; } = string.Empty;

        [JsonPropertyName("instruction")]
        public string Instruction { get; set; } = string.Empty;

        [JsonPropertyName("summary")]
        public string Summary { get; set; } = string.Empty;

        [JsonPropertyName("detail")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Detail { get; set; }

        [JsonPropertyName("matchedValue")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? MatchedValue { get; set; }

        [JsonPropertyName("matchLength")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int MatchLength { get; set; }

        [JsonPropertyName("sourceAddresses")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? SourceAddresses { get; set; }
    }
}
