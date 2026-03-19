namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>One EE OS thread entry returned by GETTHREADS.</summary>
    public class ThreadEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public int Id { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("pc")]
        public uint Pc { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("sp")]
        public uint Sp { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("priority")]
        public int Priority { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("entryPc")]
        public uint EntryPc { get; set; }
    }
}
