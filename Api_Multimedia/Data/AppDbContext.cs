using Api_Multimedia.Models;
using Microsoft.EntityFrameworkCore;

namespace Api_Multimedia.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Producto> Productos => Set<Producto>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Producto>(entity =>
        {
            entity.Property(p => p.Nombre).IsRequired().HasMaxLength(200);
            entity.Property(p => p.ImagenRuta).HasMaxLength(500);
            entity.Property(p => p.ImagenContentType).HasMaxLength(50);
            entity.Property(p => p.VideoRuta).HasMaxLength(500);
        });
    }
}
