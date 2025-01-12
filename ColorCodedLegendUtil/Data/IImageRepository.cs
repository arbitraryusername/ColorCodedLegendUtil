namespace ColorCodedLegendUtil.Data;

public interface IImageRepository
{
    Task<IEnumerable<ImageRecord>> GetAllAsync();
    Task<ImageRecord?> GetAsync(string imageName);
    Task<ImageRecord> CreateOrUpdateAsync(ImageRecord record);
    Task SaveChangesAsync();
}
