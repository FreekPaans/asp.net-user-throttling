using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Mvc;

namespace UserThrottling {
	public class UserThrottlingActionFilterAttribute : ActionFilterAttribute{
		readonly long _requestsPerTimeStep;
		readonly TimeSpan _timeStep;

		readonly static object _lockObject = new object();
		ConcurrentDictionary<string,ConcurrentLong> _throttlePerUser = new ConcurrentDictionary<string,ConcurrentLong>();
		DateTime _lastClean = DateTime.Now;

		public string _resetThrottleUrl = "/reset-throttle.axd";

		public UserThrottlingActionFilterAttribute(long requestsPerTimeStep, TimeSpan timeStep) {
			_timeStep = timeStep;
			_requestsPerTimeStep = requestsPerTimeStep;
		}

		public override void OnActionExecuting(ActionExecutingContext filterContext) {
			if(!filterContext.HttpContext.Request.IsAuthenticated) {
				return;
			}

			if(_timeStep==TimeSpan.Zero) {
				return;
			}

			ExpireThrottlingIfNecessary();

			var username = filterContext.HttpContext.User.Identity.Name;

			var counter = _throttlePerUser.GetOrAdd(username,ConcurrentLong.Zero);

			var lastValue = counter.Increment();

			if(lastValue <= _requestsPerTimeStep) {
				return;
			}

			filterContext.HttpContext.Response.StatusCode = (int)HttpStatusCode.Conflict;

			if(filterContext.HttpContext.Request.IsAjaxRequest()) {
				filterContext.Result = new ContentResult { Content = string.Format("You are being throttled, please go to {0} to reset throttling", _resetThrottleUrl) };
				return;
			}

			filterContext.Result = 
			new ViewResult() { 
				ViewName = "Throttling",
				ViewData = new ViewDataDictionary()
			};
			//ViewEngines.Engines.FindView(filterContext.Controller.ControllerContext,"throttling",null);

			//filterContext.Result = new RedirectResult(_resetThrottleUrl,false);
		}

		private void ExpireThrottlingIfNecessary() {
			lock(_lockObject) {
				if((DateTime.Now -  _lastClean) < _timeStep) {
					return;
				}

				_throttlePerUser = new ConcurrentDictionary<string,ConcurrentLong>();
				_lastClean = DateTime.Now;
			}
		}

		
	}
}
