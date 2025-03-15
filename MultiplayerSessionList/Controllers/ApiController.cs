using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MultiplayerSessionList.Models;
using MultiplayerSessionList.Modules;
using MultiplayerSessionList.Services;
using Ndjson.AsyncStreams.AspNetCore.Mvc;

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
        public async Task<IActionResult> Sessions2([FromQuery] string[] game, string admin_password, int? simulate_delay, bool? mock, CancellationToken cancellationToken)
        {
            string[] games = game.Distinct().Where(g => _gameListModuleManager.GameListPlugins.ContainsKey(g)).ToArray();
            if (games.Length == 0)
            {
                return NotFound();
                //Response.StatusCode = StatusCodes.Status404NotFound;
                //yield break;
            }

            string AdminDataPassword = _configuration["AdminDataPassword"];
            bool Admin = AdminDataPassword == admin_password;
            bool Mock = mock ?? false;

            if (!Admin)
            {
                simulate_delay = 0;
                Mock = false;
                games = games.Where(g => _gameListModuleManager.GameListPlugins[g].IsPublic).ToArray();
            }
            if (games.Length == 0)
            {
                return Unauthorized();
                //Response.StatusCode = StatusCodes.Status401Unauthorized;
                //yield break;
            }
            if (simulate_delay > 5000)
                simulate_delay = 5000;
            //GameListData data = await _gameListModuleManager.GameListPlugins[game].GetGameList(Admin);
            //return Ok(data);
            //await foreach(var item in _gameListModuleManager.GameListPlugins[game].GetGameListChunksAsync(Admin, cancellationToken))
            //{
            //    yield return item;
            //}

            ////return new NdjsonAsyncEnumerableResult<Datum>(_gameListModuleManager.GameListPlugins[game[0]].GetGameListChunksAsync(Admin, cancellationToken));
            return new NdjsonAsyncEnumerableResult<Datum>(SelectManyAsync(games.Select(g => _gameListModuleManager.GameListPlugins[g].GetGameListChunksAsync(games.Length > 1, Admin, Mock, cancellationToken)), simulate_delay ?? 0));
            //return new NdjsonAsyncEnumerableResult<Datum>(_gameListModuleManager.GameListPlugins[game[0]].GetGameListChunksAsync(Admin, cancellationToken));
            //return Sessions2Internal(games, Admin, cancellationToken);
            //yield break;
            //return new NdjsonAsyncEnumerableResult<dynamic>(_gameListModuleManager.GameListPlugins[game].GetGameListChunksAsync(Admin, cancellationToken));
        }

        /// <summary>
        /// Starts all inner IAsyncEnumerable and returns items from all of them in order in which they come.
        /// </summary>
        public static async IAsyncEnumerable<TItem> SelectManyAsync<TItem>(IEnumerable<IAsyncEnumerable<TItem>> source, int testDelay = 0)
        {
            // Get enumerators from all inner IAsyncEnumerable
            var enumerators = source.Select(x => x.GetAsyncEnumerator()).ToList();
            var runningTasks = new List<Task<(IAsyncEnumerator<TItem>, bool)>>();

            try
            {
                // Start all enumerators
                foreach (var enumerator in enumerators)
                    runningTasks.Add(MoveNextWrapped(enumerator));

                // Process items as they arrive
                while (runningTasks.Any())
                {
                    var finishedTask = await Task.WhenAny(runningTasks);
                    runningTasks.Remove(finishedTask);

                    var (enumerator, hasItem) = await finishedTask;

                    if (hasItem)
                    {
                        yield return enumerator.Current;
                        runningTasks.Add(MoveNextWrapped(enumerator));

                        if (testDelay > 0)
                            await Task.Delay(testDelay);
                    }
                }
            }
            finally
            {
                // Ensure enumerators are disposed
                foreach (var asyncEnumerator in enumerators)
                {
                    await asyncEnumerator.DisposeAsync();
                }
            }
        }

        /// <summary>
        /// Helper method that returns Task with tuple of IAsyncEnumerable and it's result of MoveNextAsync.
        /// </summary>
        private static async Task<(IAsyncEnumerator<TItem>, bool)> MoveNextWrapped<TItem>(IAsyncEnumerator<TItem> enumerator)
        {
            return (enumerator, await enumerator.MoveNextAsync());
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

            return Ok(_gameListModuleManager
                .GameListPlugins
                .Values
                .Where(dr => Admin || dr.IsPublic)
                .Select(dr => new { Key = dr.GameID, Name = dr.Title })
                .OrderBy(dr => dr.Name));
        }
    }
}
