using Microsoft.AspNetCore.Mvc;

namespace Edi.Controllers
{
    public class Player : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
