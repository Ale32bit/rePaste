using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DevBin.Pages.Admin.Pastes
{
    public class IndexModel : PageModel
    {
        public static int PastesPerPage = 30;
        private readonly DevBin.Data.ApplicationDbContext _context;

        public IndexModel(DevBin.Data.ApplicationDbContext context)
        {
            _context = context;
        }

        public IList<Paste> Paste { get; set; } = default!;
        public int CurrentPage { get; set; }

        public async Task OnGetAsync([FromQuery] int page = 0)
        {
            page = Math.Max(page, 0);
            CurrentPage = page;
            Paste = await _context.Pastes
                .Include(p => p.Author)
                .Include(p => p.Folder)
                .OrderByDescending(q => q.DateTime)
                .Skip(page * PastesPerPage)
                .Take(PastesPerPage)
                .ToListAsync();
        }
    }
}