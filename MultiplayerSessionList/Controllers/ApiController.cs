using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MultiplayerSessionList.Extensions;
using MultiplayerSessionList.Models;
using MultiplayerSessionList.Modules;
using MultiplayerSessionList.Services;

namespace MultiplayerSessionList.Controllers
{
    [ApiController]
    public class ApiController : Controller
    {
        private readonly ILogger<ApiController> _logger;
        private readonly GameListModuleManager _gameListModuleManager;
        private readonly ScopedGameListModuleManager _scopedGameListModuleManager;
        private readonly IConfiguration _configuration;

        public ApiController(ILogger<ApiController> logger, GameListModuleManager gameListModuleManager, ScopedGameListModuleManager scopedGameListModuleManager, IConfiguration configuration)
        {
            _logger = logger;
            _gameListModuleManager = gameListModuleManager;
            _scopedGameListModuleManager = scopedGameListModuleManager;
            _configuration = configuration;
        }

        [EnableCors("Sessions")]
        [Route("api/1.0/sessions")]
        public async Task<IActionResult> Sessions(string game, string admin_password)
        {
            if (!_gameListModuleManager.GameListPluginsOld.ContainsKey(game))
                return NotFound();

            string AdminDataPassword = _configuration["AdminDataPassword"];
            bool Admin = AdminDataPassword == admin_password;

            if (!Admin && !_gameListModuleManager.GameListPluginsOld[game].IsPublic)
                return Unauthorized();

            GameListData data = await _gameListModuleManager.GameListPluginsOld[game].GetGameList(Admin);
            return Ok(data);
        }


        [EnableCors("Sessions")]
        [Route("api/2.0/sessions")]
        public async Task Sessions2(
            [FromQuery] string[] game,
            string admin_password,
            int? simulate_delay,
            bool? mock,
            string? mode, // "chunked", "event", "websock" (handle "websock" later)
            CancellationToken cancellationToken)
        {
            string[] games = game.Distinct().Where(g => _gameListModuleManager.HasPlugin(g)).ToArray();
            if (games.Length == 0)
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            string AdminDataPassword = _configuration["AdminDataPassword"];
            bool Admin = AdminDataPassword == admin_password;
            bool Mock = mock ?? false;

            if (!Admin)
            {
                simulate_delay = 0;
                Mock = false;
                games = games.Where(g => _gameListModuleManager.IsPublic(g)).ToArray();
            }
            if (games.Length == 0)
            {
                Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            if (simulate_delay > 5000)
                simulate_delay = 5000;

            var pluginStreams = games
                .Select(g => _scopedGameListModuleManager.GetPlugin(g)?.GetGameListChunksAsync(games.Length > 1, Admin, Mock, cancellationToken))
                .Where(s => s != null);

            Response.Headers.Add("Cache-Control", "no-store");

            if (mode == "event")
            {
                Response.ContentType = "text/event-stream";
                await foreach (var datum in pluginStreams.SelectManyAsync().DelayAsync(simulate_delay ?? 0))
                {
                    var json = JsonSerializer.Serialize(datum);
                    await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                }
            }
            else // Default: chunked NDJson
            {
                Response.ContentType = "application/x-ndjson";
                await foreach (var datum in pluginStreams.SelectManyAsync().DelayAsync(simulate_delay ?? 0))
                {
                    var json = JsonSerializer.Serialize(datum);
                    await Response.WriteAsync(json + "\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                }
            }
        }

        [EnableCors("Games")]
        [Route("api/1.0/games")]
        public IActionResult Games(string admin_password)
        {
            string AdminDataPassword = _configuration["AdminDataPassword"];
            bool Admin = AdminDataPassword == admin_password;

            return Ok(_gameListModuleManager
                .GameListPluginsOld
                .Values
                .Where(dr => Admin || dr.IsPublic)
                .Select(dr => new { Key = dr.GameID, Name = dr.Title })
                .OrderBy(dr => dr.Name));
        }

        [EnableCors("Games")]
        [Route("api/2.0/games")]
        public IActionResult Games2(string admin_password)
        {
            string AdminDataPassword = _configuration["AdminDataPassword"];
            bool Admin = AdminDataPassword == admin_password;

            //return Ok(_gameListModuleManager
            //    .GameListPlugins
            //    .Values
            //    .Where(dr => Admin || dr.IsPublic)
            //    .Select(dr => new { Key = dr.GameID, Name = dr.Title })
            //    .OrderBy(dr => dr.Name));
            return Ok(_gameListModuleManager
                .GetPluginList(Admin)
                .Select(dr => new { Key = dr.GameID, Name = dr.Title })
                .OrderBy(dr => dr.Name));
        }
    }
}
