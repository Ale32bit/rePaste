using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DevBin.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser, IdentityRole<int>, int>, IDataProtectionKeyContext
    {
        public DbSet<Paste> Pastes { get; set; }
        public DbSet<PasteContent> Contents { get; set; }
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

        public async Task<PasteContent> GetOrCreateContentAsync(byte[] content)
        {
            var hash = PasteContent.Hash(content);
            var pasteContent = await Contents.FirstOrDefaultAsync(q => q.HashId.SequenceEqual(hash));
            if (pasteContent != null)
                return pasteContent;
            
            pasteContent = new PasteContent
            {
                HashId = hash,
                Content = content
            };
            await Contents.AddAsync(pasteContent);
            await SaveChangesAsync();
            return pasteContent;
        }

#pragma warning disable CS0618 // Type or member is obsolete
        private async Task MigratePasteContentAsync(Paste? paste)
        {
            if (paste?.Content is null)
                return;
            
            var pasteContent = await GetOrCreateContentAsync(paste.Content);
            paste.PasteContent = pasteContent;
            paste.Content = null;
            Pastes.Update(paste);
            await SaveChangesAsync();
        }
        
        /// <summary>
        /// Fetch the paste with the given code, skip loading content.
        /// </summary>
        /// <param name="code">Paste code</param>
        /// <returns>Partial paste</returns>
        public async Task<Paste?> GetPartialPasteAsync(string code)
        {
            var paste =  await Pastes
                .Include(q => q.Author)
                .Include(q => q.Folder)
                .Include(q => q.Exposure)
                .Include(q => q.Syntax)
                .FirstOrDefaultAsync(p => p.Code == code);

            await MigratePasteContentAsync(paste);

            return paste;
        } 

        /// <summary>
        /// Fetch the full paste with the given code, including content.
        /// </summary>
        /// <param name="code"></param>
        /// <returns></returns>
        public async Task<Paste?> GetFullPasteAsync(string code)
        {
            var paste = await Pastes
                .Include(q => q.Author)
                .Include(q => q.Folder)
                .Include(q => q.Exposure)
                .Include(q => q.Syntax)
                .Include(q => q.PasteContent)
                .FirstOrDefaultAsync(p => p.Code == code);
            
            await MigratePasteContentAsync(paste);

            return paste;
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