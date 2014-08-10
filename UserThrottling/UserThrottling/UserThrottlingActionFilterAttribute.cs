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
		readonly UserRequestsCounter _counter;
		readonly UserThrottlingConfiguration _config;
		
		public UserThrottlingActionFilterAttribute(UserThrottlingConfiguration config) {
			_config = config;
			_counter = new UserRequestsCounter(config.RequestsPerTimeStep,config.TimeStep);
		}

		public override void OnActionExecuting(ActionExecutingContext filterContext) {
			if(!HasTimeStep()) {
				return;
			}
			new UserThrottlingHandler(_config.ThrottleBackoff, filterContext,_counter).Handle();
		}

		private bool HasTimeStep() {
			return _config.TimeStep!=TimeSpan.Zero;
		}
	}
}
