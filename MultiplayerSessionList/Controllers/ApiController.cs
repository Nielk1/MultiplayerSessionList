using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MultiplayerSessionList.Services;

namespace MultiplayerSessionList.Controllers
{
    [ApiController]
    public class ApiController : Controller
    {
        private readonly ILogger<ApiController> _logger;
        private readonly GameListModuleManager _gameListModuleManager;
        private readonly IConfiguration _configuration;

        public ApiController(ILogger<ApiController> logger, GameListModuleManager gameListModuleManager, IConfiguration configuration)
        {
            _logger = logger;
            _gameListModuleManager = gameListModuleManager;
            _configuration = configuration;
        }

        [Route("api/sessions")]
        public async Task<IActionResult> Sessions(string game, bool raw, string admin_password)
        {
            if (!_gameListModuleManager.GameListPlugins.ContainsKey(game))
                return NotFound();

            string AdminDataPassword = _configuration["AdminDataPassword"];

            var (metadata, defaultSessionItem, dataCache, sessionItems, jToken) = await _gameListModuleManager.GameListPlugins[game].GetGameList(AdminDataPassword == admin_password);
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
