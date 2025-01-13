using ColorCodedLegendUtil.Components;
using ColorCodedLegendUtil.Data;
using ColorCodedLegendUtil.DTO;
using ColorCodedLegendUtil.JSON;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

// Top-level statements only below
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient("ServerAPI", client =>
{
    // For local dev, you might do:
    client.BaseAddress = new Uri("https://localhost:7166/");
    // or whichever port your app is actually using
});

// 1. EF Core (SQLite)
builder.Services.AddDbContext<MyDbContext>(options =>
{
    var dbPath = Path.Combine(builder.Environment.ContentRootPath, "images.db");
    options.UseSqlite($"Data Source={dbPath}");
});

// 2. Repositories, etc.
builder.Services.AddScoped<IImageRepository, EFImageRepository>();

// 3. Razor Components
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// EnsureCreated + seeding if desired:
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();
    var env = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();

    db.Database.EnsureCreated();

    // Check if the repository is empty, seed with data if it's empty
    if (!db.ImageRecords.Any())
    {
        var imagesPath = Path.Combine(env.WebRootPath, "images");
        var seedFilePath = Path.Combine(env.WebRootPath, "legend_bounding_boxes_seed_data.json");

        if (Directory.Exists(imagesPath) && File.Exists(seedFilePath))
        {
            // Helper method to check for supported image formats
            bool IsSupportedImage(string filePath)
            {
                var supportedExtensions = new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp" };
                var extension = Path.GetExtension(filePath).ToLowerInvariant();
                return supportedExtensions.Contains(extension);
            }

            // Read and deserialize JSON seed data
            var jsonContent = await File.ReadAllTextAsync(seedFilePath);
            var seedData = JsonSerializer.Deserialize<LegendBBoxSeedData>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            var legendBoxDict = seedData?.Data.ToDictionary(entry => entry.FileName, entry => entry.LegendBBox);

            // Get all image files in the images directory and filter out unsupported formats
            var imageFiles = Directory.GetFiles(imagesPath)
                                      .Where(file => IsSupportedImage(file))
                                      .ToList();

            foreach (var filePath in imageFiles)
            {
                var fileName = Path.GetFileName(filePath);
                if (legendBoxDict != null && legendBoxDict.TryGetValue(fileName, out var bbox) && bbox.Length == 4)
                {
                    var record = new ImageRecord
                    {
                        Name = fileName,
                        X1 = bbox[0],
                        Y1 = bbox[1],
                        X2 = bbox[2],
                        Y2 = bbox[3]
                    };
                    db.ImageRecords.Add(record);
                }
                else
                {
                    // Handle cases where bounding box data is missing or invalid
                    var record = new ImageRecord
                    {
                        Name = fileName,
                    };
                    db.ImageRecords.Add(record);
                }
            }

            db.SaveChanges();
        }
        else
        {
            // Optionally handle the case where the images directory doesn't exist
            Console.WriteLine("ERROR! Images directory or JSON seed data not found!");
        }
    }
}

// Minimal APIs
// Endpoint #1: Get all image names
app.MapGet("api/images", async (IImageRepository repo) =>
{
    var all = await repo.GetAllAsync();
    // Return a list of just the names (strings)
    return all.Select(i => i.Name).ToList();
});

// Endpoint #2: Get a specific image file from wwwroot/images/
app.MapGet("api/images/{imageName}", async (string imageName, IWebHostEnvironment env) =>
{
    var fullPath = Path.Combine(env.WebRootPath, "images", imageName);
    if (!File.Exists(fullPath))
    {
        return Results.NotFound($"Image {imageName} not found on server.");
    }

    // Decide the content type based on extension
    var contentType = imageName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
        ? "image/png"
        : "image/jpeg";

    return Results.File(fullPath, contentType);
});

// Endpoint #3: Process a click, generate & return an SVG overlay
app.MapPost("api/images/{imageName}/click", async (
    string imageName,
    ImageClickRequest click,
    IImageRepository repo,
    IWebHostEnvironment env) =>
{
    // 1) Check we have metadata for this image
    var record = await repo.GetAsync(imageName);
    if (record is null)
    {
        return Results.NotFound($"No metadata found for image {imageName}.");
    }

    // 2) Verify the image file exists
    var fullPath = Path.Combine(env.WebRootPath, "images", imageName);
    if (!File.Exists(fullPath))
    {
        return Results.NotFound($"Image file {imageName} not found on disk.");
    }

    // 3) Load the image using ImageSharp
    using var image = Image.Load<Rgba32>(fullPath);
    int width = image.Width;
    int height = image.Height;

    // Ensure click coordinates are within image bounds
    if (click.X < 0 || click.X >= width || click.Y < 0 || click.Y >= height)
    {
        return Results.BadRequest("Click coordinates are outside the image bounds.");
    }

    // Get the color of the clicked pixel
    var pixelColor = image[click.X, click.Y];

    // 4) Determine if the color is a shade of gray
    bool IsGray(Rgba32 color)
    {
        int r = color.R;
        int g = color.G;
        int b = color.B;

        int max = Math.Max(r, Math.Max(g, b));
        int min = Math.Min(r, Math.Min(g, b));

        int threshold = 15; // Adjust as needed
        return (max - min) < threshold;
    }

    bool isGray = IsGray(pixelColor);

    // Start building the SVG
    var sb = new StringBuilder();
    sb.AppendLine($$"""
    <svg width="{{width}}" height="{{height}}" viewBox="0 0 {{width}} {{height}}" xmlns="http://www.w3.org/2000/svg">
        <!-- Marker at clicked location -->
        <circle cx="{{click.X}}" cy="{{click.Y}}" r="5" fill="black" />
    """);

    // If there's a bounding box, process further
    if (record.LegendBoundingBox is { Length: 4 })
    {
        float x1 = record.LegendBoundingBox[0];
        float y1 = record.LegendBoundingBox[1];
        float x2 = record.LegendBoundingBox[2];
        float y2 = record.LegendBoundingBox[3];
        float rectWidth = x2 - x1;
        float rectHeight = y2 - y1;

        // Draw the legend bounding box
        sb.AppendLine($$"""
        <rect x="{{x1}}" y="{{y1}}" width="{{rectWidth}}" height="{{rectHeight}}"
              fill="none" stroke="black" stroke-width="3" />
        """);

        if (!isGray)
        {
            bool ColorsAreSimilar(Rgba32 color1, Rgba32 color2)
            {
                int threshold = 10; // Adjust as needed to determine color similarity
                int diffR = Math.Abs(color1.R - color2.R);
                int diffG = Math.Abs(color1.G - color2.G);
                int diffB = Math.Abs(color1.B - color2.B);

                return diffR < threshold && diffG < threshold && diffB < threshold;
            }

            // 5) Search within the legend bounding box for matching color pixels
            var matchingPixels = new List<(int x, int y)>();
            for (int y = (int)y1; y < (int)y2; y++)
            {
                for (int x = (int)x1; x < (int)x2; x++)
                {
                    if (x >= 0 && x < width && y >= 0 && y < height)
                    {
                        var currentColor = image[x, y];
                        if (ColorsAreSimilar(pixelColor, currentColor))
                        {
                            matchingPixels.Add((x, y));
                        }
                    }
                }
            }

            if (matchingPixels.Count > 0)
            {
                // Calculate the center of the matching region
                double avgX = matchingPixels.Average(p => p.x);
                double avgY = matchingPixels.Average(p => p.y);

                // 6) Determine the arrow orientation and direction
                bool isLegendWider = rectWidth >= rectHeight;
                bool legendInRightHalf = (x1 + rectWidth / 2) > (width / 2);
                bool legendInBottomHalf = (y1 + rectHeight / 2) > (height / 2);

                // Arrow marker definition
                sb.AppendLine($$"""
        <!-- Arrow marker definition -->
        <defs>
            <marker id="arrowhead" markerWidth="10" markerHeight="7" 
                    refX="0" refY="3.5" orient="auto">
                <polygon points="0 0, 10 3.5, 0 7" fill="black" />
            </marker>
        </defs>
        """);

                // Draw the arrow
                sb.AppendLine($$"""
        <!-- Arrow from clicked point to legend -->
        <line x1="{{click.X}}" y1="{{click.Y}}" x2="{{avgX}}" y2="{{avgY}}"
              stroke="black" stroke-width="2" marker-end="url(#arrowhead)" />
        """);
            }
        }
    }

    sb.AppendLine("</svg>");

    // 7) Return the SVG as "image/svg+xml"
    var svgBytes = Encoding.UTF8.GetBytes(sb.ToString());
    return Results.File(svgBytes, "image/svg+xml");
});


app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

// Map Razor Components
app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();


app.Run();
