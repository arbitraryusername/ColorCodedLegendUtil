using Microsoft.EntityFrameworkCore;

namespace ColorCodedLegendUtil.Data;

public class EFImageRepository : IImageRepository
{
    private readonly MyDbContext _db;

    public EFImageRepository(MyDbContext db)
    {
        _db = db;
    }

    public async Task<IEnumerable<ImageRecord>> GetAllAsync()
        => await _db.ImageRecords.ToListAsync();

    public async Task<ImageRecord?> GetAsync(string imageName)
        => await _db.ImageRecords.FirstOrDefaultAsync(i => i.Name == imageName);

    public async Task<ImageRecord> CreateOrUpdateAsync(ImageRecord record)
    {
        var existing = await GetAsync(record.Name);
        if (existing is null)
        {
            _db.ImageRecords.Add(record);
        }
        else
        {
            existing.LegendBoundingBox = record.LegendBoundingBox;
        }
        return record;
    }

    public async Task SaveChangesAsync()
    {
        await _db.SaveChangesAsync();
    }
}
