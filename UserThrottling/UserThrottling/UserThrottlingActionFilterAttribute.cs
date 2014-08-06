using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Web;
using System.Web.Mvc;

namespace UserThrottling {
	public class UserThrottlingActionFilterAttribute : ActionFilterAttribute{
		readonly UserThrottlingHandler _throttlingHandler;

		public UserThrottlingActionFilterAttribute(long requestsPerTimeStep, TimeSpan timeStep, TimeSpan overrideCookieTimeout) {
			_throttlingHandler = new UserThrottlingHandler(timeStep,requestsPerTimeStep,overrideCookieTimeout);

		}

		public override void OnActionExecuting(ActionExecutingContext filterContext) {
			_throttlingHandler.Handle(filterContext);
		}
	}
}
