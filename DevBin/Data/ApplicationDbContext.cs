using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DevBin.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser, IdentityRole<int>, int>, IDataProtectionKeyContext
    {
        public DbSet<Paste> Pastes { get; set; }
        public DbSet<Folder> Folders { get; set; }
        public DbSet<Syntax> Syntaxes { get; set; }
        public DbSet<Exposure> Exposures { get; set; }
        public DbSet<Report> Reports { get; set; }
        public DbSet<ApiToken> ApiTokens { get; set; }
        public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;

        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public async Task<Paste?> GetFullPasteAsync(string code)
        {
            return await Pastes
                .Include(q => q.Author)
                .Include(q => q.Folder)
                .Include(q => q.Exposure)
                .Include(q => q.Syntax)
                .FirstOrDefaultAsync(p => p.Code == code);
        }

        public IQueryable<Paste> GetUserPastes(int userId)
        {
            return Pastes
                .Include(q => q.Author)
                .Include(q => q.Folder)
                .Include(q => q.Exposure)
                .Include(q => q.Syntax)
                .Where(p => p.AuthorId == userId);
        }

        public IQueryable<Folder> GetUserFolders(int userId)
        {
            return Folders
                .Include(q => q.Pastes)
                .Include(q => q.Owner)
                .Where(p => p.OwnerId == userId);
        }
    }
}