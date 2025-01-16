using System.Text.Json.Serialization;

namespace ColorCodedLegendUtil.JSON;

public class LegendBoxEntry
{
    [JsonPropertyName("file_name")]
    public string FileName { get; set; } = default!;

    [JsonPropertyName("legend_bbox")]
    public int[] LegendBBox { get; set; } = new int[4]; // [X1, Y1, X2, Y2]
}
