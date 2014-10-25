using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace UserThrottling.MvcExample.Controllers {
	public class HomeController:Controller {
		[UserThrottlingActionFilter(5,10,10)]
		public ActionResult Index() {
			ViewBag.Message = "Welcome to ASP.NET MVC!";

			return View();
		}

		public ActionResult About() {
			return View();
		}
	}
}
