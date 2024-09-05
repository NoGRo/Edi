using Microsoft.AspNetCore.Mvc;

namespace Edi.Controllers
{
    public class PlayerController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
