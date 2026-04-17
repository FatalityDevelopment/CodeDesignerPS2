using CodeDesigner.Ps2.WebApi.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace CodeDesigner.Ps2.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ServersController : ControllerBase
{
    [HttpGet]
    public IActionResult GetServers()
    {
        // Placeholder — will eventually query live Multiplayer server status
        var servers = new List<ServerStatusDto>
        {
            new()
            {
                Name = "SOCOM II: U.S. Navy SEALs",
                Status = "Online",
                PlayersOnline = 0,
                GameTitle = "SOCOM II",
                Region = "North America"
            },
            new()
            {
                Name = "Star Wars Battlefront II",
                Status = "Online",
                PlayersOnline = 0,
                GameTitle = "Star Wars Battlefront II",
                Region = "North America"
            },
            new()
            {
                Name = "Ratchet: Deadlocked",
                Status = "Online",
                PlayersOnline = 0,
                GameTitle = "Ratchet: Deadlocked",
                Region = "North America"
            }
        };

        return Ok(ApiResponse<List<ServerStatusDto>>.Ok(servers));
    }

    [HttpGet("{name}")]
    public IActionResult GetServer(string name)
    {
        // Return a specific server status (placeholder)
        var server = new ServerStatusDto
        {
            Name = name,
            Status = "Online",
            PlayersOnline = 0,
            GameTitle = name,
            Region = "North America"
        };

        return Ok(ApiResponse<ServerStatusDto>.Ok(server));
    }
}
