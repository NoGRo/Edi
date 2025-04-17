using Microsoft.AspNetCore.Mvc;

namespace Edi.Rest.Controllers
{
    public class PlayerController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
