﻿using Microsoft.AspNetCore.Mvc;

namespace Edi.Mvc.Controllers
{
    public class PlayerController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
