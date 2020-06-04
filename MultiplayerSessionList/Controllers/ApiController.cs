using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MultiplayerSessionList.Models;
using MultiplayerSessionList.Modules;
using Newtonsoft.Json;
using MultiplayerSessionList.Services;

namespace MultiplayerSessionList.Controllers
{
    public class ApiController : Controller
    {
        private readonly ILogger<ApiController> _logger;
        private readonly GameListModuleManager gameListModuleManager;

        public ApiController(ILogger<ApiController> logger, GameListModuleManager gameListModuleManager)
        {
            _logger = logger;
            this.gameListModuleManager = gameListModuleManager;
        }

        [Route("api/sessions")]
        public async Task<IActionResult> Sessions(string game, bool raw)
        {
            if (!gameListModuleManager.GameListPlugins.ContainsKey(game))
                return NotFound();

            var Data = await gameListModuleManager.GameListPlugins[game].GetGameList();

            JsonSerializerSettings settings = new JsonSerializerSettings();
            settings.NullValueHandling = NullValueHandling.Ignore;
            return Content(JsonConvert.SerializeObject(new { SessionDefault = Data.Item1, Sessions = Data.Item2, Raw = raw ? Data.Item3 : null }, settings), "application/json");
        }

        [Route("api/games")]
        public IActionResult Games()
        {
            JsonSerializerSettings settings = new JsonSerializerSettings();
            settings.NullValueHandling = NullValueHandling.Ignore;
            return Content(JsonConvert.SerializeObject(gameListModuleManager.GameListPlugins.Values.Select(dr => new { Key = dr.GameID, Name = dr.Title }), settings), "application/json");
        }
    }
}
