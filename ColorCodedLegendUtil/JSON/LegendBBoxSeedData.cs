using System.Text.Json.Serialization;

namespace ColorCodedLegendUtil.JSON;

// Classes to represent the JSON seed data
public class LegendBBoxSeedData
{
    [JsonPropertyName("data")]
    public List<LegendBoxEntry> Data { get; set; } = new();
}
