using CodeDesigner.Ps2.WebApi.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace CodeDesigner.Ps2.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ResearchController : ControllerBase
{
    [HttpGet]
    public IActionResult GetEntries()
    {
        // Placeholder — will eventually be backed by a memories database
        var entries = new List<ResearchEntryDto>
        {
            new()
            {
                Id = 1,
                Title = "Welcome to Research",
                Content = "This section will be a collection of everyone's memories and research notes about PlayStation 2 game internals, reverse engineering findings, and community knowledge.",
                Author = "CodeDesigner Team",
                CreatedAt = DateTime.UtcNow
            }
        };

        return Ok(ApiResponse<List<ResearchEntryDto>>.Ok(entries));
    }

    [HttpGet("{id}")]
    public IActionResult GetEntry(int id)
    {
        if (id != 1)
            return NotFound(ApiResponse<ResearchEntryDto>.Fail("Research entry not found"));

        var entry = new ResearchEntryDto
        {
            Id = 1,
            Title = "Welcome to Research",
            Content = "This section will be a collection of everyone's memories and research notes about PlayStation 2 game internals, reverse engineering findings, and community knowledge.",
            Author = "CodeDesigner Team",
            CreatedAt = DateTime.UtcNow
        };

        return Ok(ApiResponse<ResearchEntryDto>.Ok(entry));
    }
}
