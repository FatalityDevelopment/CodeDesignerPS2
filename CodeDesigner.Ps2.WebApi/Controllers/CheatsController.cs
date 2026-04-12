using CodeDesigner.Ps2.Database;
using CodeDesigner.Ps2.WebApi.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CodeDesigner.Ps2.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CheatsController : ControllerBase
{
    private readonly CodeDesignerDbContext _db;

    public CheatsController(CodeDesignerDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetCheatLists()
    {
        var lists = await _db.CheatLists
            .Include(cl => cl.Game)
            .Select(cl => new CheatListDto
            {
                Id = cl.Id,
                Title = cl.Title,
                GameTitle = cl.Game != null ? cl.Game.Title : null,
                CheatCount = cl.Cheats.Count
            })
            .ToListAsync();

        return Ok(ApiResponse<List<CheatListDto>>.Ok(lists));
    }

    [HttpGet("{id}/cheats")]
    public async Task<IActionResult> GetCheats(int id)
    {
        var cheatList = await _db.CheatLists
            .Include(cl => cl.Cheats)
            .ThenInclude(c => c.CheatLines)
            .FirstOrDefaultAsync(cl => cl.Id == id);

        if (cheatList == null)
            return NotFound(ApiResponse<List<CheatDto>>.Fail("Cheat list not found"));

        var cheats = cheatList.Cheats
            .OrderBy(c => c.SortOrder)
            .Select(c => new CheatDto
            {
                Id = c.Id,
                Label = c.Label,
                Description = c.Description,
                Author = c.Author,
                Enabled = c.IsEnabled,
                Lines = c.CheatLines
                    .OrderBy(l => l.SortOrder)
                    .Select(l => l.Code)
                    .ToList()
            })
            .ToList();

        return Ok(ApiResponse<List<CheatDto>>.Ok(cheats));
    }
}
