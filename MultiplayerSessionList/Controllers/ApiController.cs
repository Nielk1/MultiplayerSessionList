using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MultiplayerSessionList.Modules;
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

        [EnableCors("Sessions")]
        [Route("api/1.0/sessions")]
        public async Task<IActionResult> Sessions(string game, string admin_password)
        {
            if (!_gameListModuleManager.GameListPlugins.ContainsKey(game))
                return NotFound();

            string AdminDataPassword = _configuration["AdminDataPassword"];
            bool Admin = AdminDataPassword == admin_password;

            if (!Admin && !_gameListModuleManager.GameListPlugins[game].IsPublic)
                return Unauthorized();

            GameListData data = await _gameListModuleManager.GameListPlugins[game].GetGameList(Admin);
            return Ok(data);
        }

        [EnableCors("Games")]
        [Route("api/1.0/games")]
        public IActionResult Games(string admin_password)
        {
            string AdminDataPassword = _configuration["AdminDataPassword"];
            bool Admin = AdminDataPassword == admin_password;

            return Ok(_gameListModuleManager
                .GameListPlugins
                .Values
                .Where(dr => Admin || dr.IsPublic)
                .Select(dr => new { Key = dr.GameID, Name = dr.Title })
                .OrderBy(dr => dr.Name));
        }
    }
}
