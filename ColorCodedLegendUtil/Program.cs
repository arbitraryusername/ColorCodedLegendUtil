using ColorCodedLegendUtil.Components;
using ColorCodedLegendUtil.Data;
using ColorCodedLegendUtil.DTO;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Text;

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
    db.Database.EnsureCreated();

    // TODO: add seed logic for the starting images
    // seed if needed
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

    // 2) Verify the image file actually exists on disk
    var fullPath = Path.Combine(env.WebRootPath, "images", imageName);
    if (!File.Exists(fullPath))
    {
        return Results.NotFound($"Image file {imageName} not found on disk.");
    }

    // 3) Use ImageSharp to read the image size
    using var image = SixLabors.ImageSharp.Image
                        .Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(fullPath);
    int width = image.Width;
    int height = image.Height;

    // 4) Generate the SVG
    var sb = new StringBuilder();
    sb.AppendLine($$"""
<svg width="{{width}}" height="{{height}}" viewBox="0 0 {{width}} {{height}}" xmlns="http://www.w3.org/2000/svg">
    <!-- Marker at clicked location -->
    <circle cx="{{click.X}}" cy="{{click.Y}}" r="5" fill="red" />
""");

    // If there's a bounding box, draw it
    if (record.LegendBoundingBox is { Length: 4 })
    {
        var x1 = record.LegendBoundingBox[0];
        var y1 = record.LegendBoundingBox[1];
        var x2 = record.LegendBoundingBox[2];
        var y2 = record.LegendBoundingBox[3];
        var rectWidth = x2 - x1;
        var rectHeight = y2 - y1;

        sb.AppendLine($$"""
    <rect x="{{x1}}" y="{{y1}}" width="{{rectWidth}}" height="{{rectHeight}}"
          fill="none" stroke="lime" stroke-width="3" />
""");
    }

    sb.AppendLine("</svg>");

    // 5) Return the SVG as "image/svg+xml"
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
