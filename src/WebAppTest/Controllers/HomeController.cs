using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace WebAppTest.Controllers
{
    public class HomeController : Controller
    {
        public ActionResult Index()
        {
            if (this.HttpContext.Session["TValue"] == null)
                this.HttpContext.Session["TValue"] = 0;
            var cnt = (int)this.HttpContext.Session["TValue"];
            this.HttpContext.Session["TValue"] = cnt + 1;
            return View();
        }

        public ActionResult About()
        {
            ViewBag.Message = "Your application description page.";

            return View();
        }

        public ActionResult Contact()
        {
            ViewBag.Message = "Your contact page.";

            return View();
        }
    }
}