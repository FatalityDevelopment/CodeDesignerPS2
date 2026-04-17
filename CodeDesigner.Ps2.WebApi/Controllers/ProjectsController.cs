using CodeDesigner.Ps2.WebApi.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace CodeDesigner.Ps2.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProjectsController : ControllerBase
{
    [HttpGet]
    public IActionResult GetProjects()
    {
        // Hardcoded project for CodeDesigner
        var projects = new List<ProjectDto>
        {
            new()
            {
                Id = 1,
                Title = "CodeDesigner",
                Description = "A complete assembler, disassembler, instruction decoder/encoder, and execution engine for the Sony PlayStation 2 Emotion Engine (EE) MIPS64 architecture. Includes a VS Code extension, cheat engine, static recompiler, memory card manager, and online multiplayer server emulation.",
                ImageUrl = "/assets/logo.png",
                Url = "https://github.com/TylerDev888/CodeDesigner"
            }
        };

        return Ok(ApiResponse<List<ProjectDto>>.Ok(projects));
    }

    [HttpGet("{id}")]
    public IActionResult GetProject(int id)
    {
        if (id != 1)
            return NotFound(ApiResponse<ProjectDto>.Fail("Project not found"));

        var project = new ProjectDto
        {
            Id = 1,
            Title = "CodeDesigner",
            Description = "A complete assembler, disassembler, instruction decoder/encoder, and execution engine for the Sony PlayStation 2 Emotion Engine (EE) MIPS64 architecture. Includes a VS Code extension, cheat engine, static recompiler, memory card manager, and online multiplayer server emulation.",
            ImageUrl = "/assets/logo.png",
            Url = "https://github.com/TylerDev888/CodeDesigner"
        };

        return Ok(ApiResponse<ProjectDto>.Ok(project));
    }
}
