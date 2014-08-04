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

			filterContext.Result = new ContentResult { Content = "You are being throttled" };
			
			filterContext.HttpContext.Response.StatusCode = (int)HttpStatusCode.Conflict;
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
