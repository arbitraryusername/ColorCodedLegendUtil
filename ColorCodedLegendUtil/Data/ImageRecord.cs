public class ImageRecord
{
    public int Id { get; set; }

    // This is the unique name of the image (e.g., "image1.png")
    public string Name { get; set; } = default!;

    // We store the bounding box coordinates as four nullable floats
    // This is handy for EF Core column mapping
    public float? X1 { get; set; }
    public float? Y1 { get; set; }
    public float? X2 { get; set; }
    public float? Y2 { get; set; }

    /// <summary>
    /// Returns a float[] array of [X1, Y1, X2, Y2] if all four are non-null,
    /// otherwise returns null. Setter also updates X1..Y2 accordingly.
    /// </summary>
    public float[]? LegendBoundingBox
    {
        get
        {
            if (X1.HasValue && Y1.HasValue && X2.HasValue && Y2.HasValue)
            {
                return new float[] { X1.Value, Y1.Value, X2.Value, Y2.Value };
            }
            return null;
        }
        set
        {
            if (value == null || value.Length != 4)
            {
                X1 = Y1 = X2 = Y2 = null;
            }
            else
            {
                X1 = value[0];
                Y1 = value[1];
                X2 = value[2];
                Y2 = value[3];
            }
        }
    }
}
