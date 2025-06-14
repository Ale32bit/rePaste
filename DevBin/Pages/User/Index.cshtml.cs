using DevBin.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace DevBin.Pages.User
{
    public class UserModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IStringLocalizer _localizer;

        public UserModel(ApplicationDbContext context, UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, IStringLocalizer<UserModel> localizer)
        {
            _context = context;
            _userManager = userManager;
            _signInManager = signInManager;
            _localizer = localizer;
        }

        public IEnumerable<Paste> Pastes { get; set; }
        public IEnumerable<Folder> Folders { get; set; }
        public bool IsOwn { get; set; }
        public async Task<IActionResult> OnGetAsync(string username)
        {
            var user = await _userManager.FindByNameAsync(username);
            if (user == null)
                return NotFound();

            ViewData["Username"] = user.UserName;

            var pastes = _context.GetUserPastes(user.Id).Where(q => q.FolderId == null);
            var folders = _context.GetUserFolders(user.Id);

            var loggedInUser = await _userManager.GetUserAsync(User);
            if (_signInManager.IsSignedIn(User) && user.Id == loggedInUser.Id)
            {
                IsOwn = true;
            }
            else
            {
                pastes = pastes.Where(q => q.Exposure.IsListed);
                folders = folders.Where(q => q.Pastes.Any(x => x.Exposure.IsListed));
            }

            Pastes = await pastes.OrderByDescending(q => q.DateTime).ToListAsync();
            Folders = await folders.ToListAsync();

            return Page();
        }

        [PageRemote(
            // TODO
            //ErrorMessageResourceName =
            AdditionalFields = "__RequestVerificationToken",
            HttpMethod = "post",
            PageHandler = "VerifyFolder"
        )]
        [BindProperty]
        public string FolderName { get; set; }

        public async Task<IActionResult> OnPostAddFolderAsync()
        {
            var user = await _userManager.GetUserAsync(User);

            var folder = new Folder
            {
                Name = FolderName,
                DateTime = DateTime.UtcNow,
                OwnerId = user.Id,
                Link = Folder.GenerateLink(FolderName),
            };

            _context.Add(folder);
            await _context.SaveChangesAsync();

            return RedirectToPage("./Index");
        }

        public async Task<JsonResult> OnPostVerifyFolder(string folderName)
        {
            var user = await _userManager.GetUserAsync(User);
            var friendlyFolderName = Folder.GenerateLink(folderName);
            var folders = _context.GetUserFolders(user.Id);

            if (folders.Any(q => q.Link == friendlyFolderName))
            {
                return new JsonResult(false);
            }

            return new JsonResult(true);
        }
    }
}
