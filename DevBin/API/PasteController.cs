﻿#nullable disable
using DevBin.Attributes;
using DevBin.Data;
using DevBin.UserModels;
using DevBin.Utils;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DevBin.API;

[Route("api/v3/[controller]")]
[ApiController]
[RequireApiKey(ApiPermission.None)]
public class PasteController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IConfiguration _configuration;

    public PasteController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IConfiguration configuration)
    {
        _context = context;
        _userManager = userManager;
        _configuration = configuration;

        PasteSpace = User != null && signInManager.IsSignedIn(User)
            ? _configuration.GetValue<int>("Paste:MaxContentSize:Member")
            : _configuration.GetValue<int>("Paste:MaxContentSize:Guest", 1024 * 2);
    }

    public int PasteSpace { get; set; }

    /// <summary>
    /// Get information about a paste 
    /// </summary>
    /// <param name="code">Paste code</param>
    /// <returns></returns>
    [HttpGet("{code}")]
    [RequireApiKey(ApiPermission.Get)]
    [ProducesResponseType(200, Type = typeof(string))]
    public async Task<ActionResult<ResultPaste>> GetPaste(string code)
    {
        var paste = await _context.GetPartialPasteAsync(code);
        if (paste == null)
        {
            return NotFound();
        }

        var user = await _userManager.GetUserAsync(User);
        if (paste.Author != null && paste.Exposure.IsAuthorOnly && paste.AuthorId != user.Id)
        {
            return NotFound();
        }

        return ResultPaste.From(paste);
    }

    /// <summary>
    /// Get the content of the paste
    /// </summary>
    /// <param name="code">Paste code</param>
    /// <returns>The paste content</returns>
    [HttpGet("{code}/content")]
    [RequireApiKey(ApiPermission.Get)]
    public async Task<ActionResult> GetPasteContent(string code)
    {
        var paste = await _context.GetFullPasteAsync(code);
        if (paste == null)
        {
            return NotFound();
        }

        var user = await _userManager.GetUserAsync(User);
        if (paste.Author != null && paste.Exposure.IsAuthorOnly && paste.AuthorId != user.Id)
        {
            return NotFound();
        }

        return new FileContentResult(paste.PasteContent.Content, "text/plain");
    }

    /// <summary>
    /// Upload a new paste
    /// </summary>
    /// <param name="userPaste">Filled paste</param>
    /// <returns></returns>
    [HttpPost]
    [RequireApiKey(ApiPermission.Create)]
    public async Task<ActionResult<ResultPaste>> UploadPaste(UserPaste userPaste)
    {
        if (userPaste.Content.Length > PasteSpace)
            return BadRequest("Maximum content length exceeded.");

        if (userPaste.Content.Length == 0)
            return BadRequest("Content cannot be empty.");
        
        var pasteContent = await _context.GetOrCreateContentAsync(userPaste.ByteContent);

        var paste = new Paste
        {
            Title = userPaste.Title ?? "Unnamed Paste",
            Cache = PasteUtils.GetShortContent(userPaste.Content, 250),
            PasteContent = pasteContent,
            DateTime = DateTime.UtcNow,
            UploaderIPAddress = HttpContext.Connection.RemoteIpAddress.ToString(),
            Views = 0,
            ExposureId = 1,
        };

        var user = await _userManager.GetUserAsync(User);
        if (await _context.Syntaxes.AnyAsync(q => q.Name == userPaste.SyntaxName))
            paste.SyntaxName = userPaste.SyntaxName;

        if (userPaste.ExposureId.HasValue &&
            await _context.Exposures.AnyAsync(q => q.Id == userPaste.ExposureId))
            paste.ExposureId = userPaste.ExposureId.Value;

        string code;
        do
        {
            code = PasteUtils.GenerateRandomCode(_configuration.GetValue<int>("Paste:CodeLength"));
        } while (await _context.Pastes.AnyAsync(q => q.Code.ToLower() == code.ToLower()));

        paste.Code = code;

        if (!userPaste.AsGuest.Value)
        {
            paste.AuthorId = user.Id;
            if (userPaste.FolderId.HasValue)
            {
                if (_context.Folders.Any(q => q.Id == userPaste.FolderId && q.OwnerId == user.Id))
                    paste.FolderId = userPaste.FolderId.Value;
            }
        }

        _context.Pastes.Add(paste);
        await _context.SaveChangesAsync();

        return CreatedAtAction("GetPaste", new { code = paste.Code }, ResultPaste.From(paste));
    }

    /// <summary>
    /// Update information and/or content of your paste
    /// </summary>
    /// <param name="code">Paste code</param>
    /// <param name="userPaste">Updated parameters</param>
    /// <returns></returns>
    [HttpPatch("{code}")]
    [RequireApiKey(ApiPermission.Update)]
    public async Task<IActionResult> UpdatePaste(string code, UserPaste userPaste)
    {
        var paste = await _context.GetFullPasteAsync(code);
        if (paste == null)
        {
            return NotFound();
        }

        var user = await _userManager.GetUserAsync(User);
        if (paste.Author != null && paste.Exposure.IsAuthorOnly && paste.AuthorId != user.Id)
        {
            return NotFound();
        }

        if (paste.AuthorId != user.Id)
            return Unauthorized();
        
        paste.UpdateDatetime = DateTime.UtcNow;
        
        if (!string.IsNullOrWhiteSpace(userPaste.Content))
        {
            if (userPaste.Content.Length > PasteSpace)
                return BadRequest("Maximum content length exceeded.");

            if (userPaste.Content.Length == 0)
                return BadRequest("Content cannot be empty.");
            
            var pasteContent = await _context.GetOrCreateContentAsync(userPaste.ByteContent);

            paste.PasteContent = pasteContent;
            paste.Cache = PasteUtils.GetShortContent(paste.StringContent, 250);
        }

        paste.Title = userPaste.Title ?? paste.Title;

        if (!string.IsNullOrWhiteSpace(userPaste.SyntaxName) &&
            await _context.Syntaxes.AnyAsync(q => q.Name == userPaste.SyntaxName))
            paste.SyntaxName = userPaste.SyntaxName;

        if (userPaste.ExposureId.HasValue &&
            await _context.Exposures.AnyAsync(q => q.Id == userPaste.ExposureId))
            paste.ExposureId = userPaste.ExposureId.Value;

        if (userPaste.FolderId != 0 &&
            userPaste.FolderId != null && user.Folders.Any(q => q.Id == userPaste.FolderId))
            paste.FolderId = userPaste.FolderId;

        if (userPaste.FolderId == 0)
            paste.FolderId = null;


        _context.Entry(paste).State = EntityState.Modified;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!PasteExists(code))
            {
                return NotFound();
            }
            else
            {
                throw;
            }
        }

        return CreatedAtAction("GetPaste", new { code = paste.Code }, ResultPaste.From(paste));
    }

    /// <summary>
    /// Delete your own paste
    /// </summary>
    /// <param name="code">Paste code</param>
    /// <returns></returns>
    [HttpDelete("{code}")]
    [RequireApiKey(ApiPermission.Delete)]
    public async Task<IActionResult> DeletePaste(string code)
    {
        var paste = await _context.GetFullPasteAsync(code);
        if (paste == null)
        {
            return NotFound();
        }

        var user = await _userManager.GetUserAsync(User);
        if (paste.AuthorId != user.Id)
            return Unauthorized();

        _context.Pastes.Remove(paste);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    private bool PasteExists(string code)
    {
        return _context.Pastes.Any(e => e.Code == code);
    }
}