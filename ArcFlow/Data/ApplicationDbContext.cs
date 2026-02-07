using ArcFlow.Data.EntityMapping;
using ArcFlow.Features.YouTubePlayer.Models;
using Microsoft.EntityFrameworkCore;

namespace ArcFlow.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<Playlist> Playlists { get; set; }
    public DbSet<VideoItem> VideoItems { get; set; }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new PlaylistMapping());
        modelBuilder.ApplyConfiguration(new VideoItemMapping());
    }
}