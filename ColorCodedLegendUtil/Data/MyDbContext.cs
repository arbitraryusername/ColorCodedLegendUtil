namespace ColorCodedLegendUtil.Data;

using Microsoft.EntityFrameworkCore;

public class MyDbContext : DbContext
{
    public MyDbContext(DbContextOptions<MyDbContext> options)
        : base(options)
    {
    }

    public DbSet<ImageRecord> ImageRecords => Set<ImageRecord>();
}
