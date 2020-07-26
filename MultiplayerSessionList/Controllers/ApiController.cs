using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MultiplayerSessionList.Services;

namespace MultiplayerSessionList.Controllers
{
    [ApiController]
    public class ApiController : Controller
    {
        private readonly ILogger<ApiController> _logger;
        private readonly GameListModuleManager _gameListModuleManager;

        public ApiController(ILogger<ApiController> logger, GameListModuleManager gameListModuleManager)
        {
            _logger = logger;
            _gameListModuleManager = gameListModuleManager;
        }

        [Route("api/sessions")]
        public async Task<IActionResult> Sessions(string game, bool raw)
        {
            if (!_gameListModuleManager.GameListPlugins.ContainsKey(game))
                return NotFound();

            var (metadata, defaultSessionItem, dataCache, sessionItems, jToken) = await _gameListModuleManager.GameListPlugins[game].GetGameList();
            return Ok(new { Metadata = metadata, SessionDefault = defaultSessionItem, DataCache = dataCache, Sessions = sessionItems, Raw = raw ? jToken : null });
        }

        [Route("api/games")]
        public IActionResult Games()
        {
            return Ok(_gameListModuleManager
                .GameListPlugins
                .Values
                .Select(dr => new { Key = dr.GameID, Name = dr.Title }));
        }
    }
}
