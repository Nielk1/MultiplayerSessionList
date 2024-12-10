using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MultiplayerSessionList.Models;

namespace MultiplayerSessionList.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IWebHostEnvironment _env;
        public HomeController(ILogger<HomeController> logger, IWebHostEnvironment env)
        {
            _logger = logger;
            _env = env;
        }

        [Route("/")]
        public IActionResult Index()
        {
            var filePath = Path.Combine(_env.ContentRootPath, "wwwroot", "index.html");
            return PhysicalFile(filePath, "text/html");
            //return View();
        }

        [Route("/bz98r")]
        public IActionResult IndexBZ98R()
        {
            var filePath = Path.Combine(_env.ContentRootPath, "wwwroot", "bz98r.html");
            return PhysicalFile(filePath, "text/html");
        }
    }

    /*public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }*/
}
