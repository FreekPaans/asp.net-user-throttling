using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Web;
using System.Web.Mvc;

namespace UserThrottling {
	public class UserThrottlingActionFilterAttribute : ActionFilterAttribute{
		readonly long _requestsPerTimeStep;
		readonly TimeSpan _timeStep;
		readonly TimeSpan _overrideCookieTimeout;

		public string AjaxThrottlingMessage = "You are being throttled due to excessive usage, please refresh the page";

		readonly static object _lockObject = new object();
		ConcurrentDictionary<string,ConcurrentLong> _throttlePerUser = new ConcurrentDictionary<string,ConcurrentLong>();
		DateTime _lastClean = DateTime.Now;


		public string _resetThrottleUrl = "/reset-throttle.axd";

		public const string DisableUserThrottlingCookieName = "__nouserthrottling";
		public const string UserThrottlingDisabledActiveCookieName= "__nouserthrottlingactive";

		public UserThrottlingActionFilterAttribute(long requestsPerTimeStep, TimeSpan timeStep, TimeSpan overrideCookieTimeout) {
			_timeStep = timeStep;
			_requestsPerTimeStep = requestsPerTimeStep;
			_overrideCookieTimeout = overrideCookieTimeout;

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

			if(IsThrottleOverriden(filterContext)) {
				return;
			}

			filterContext.HttpContext.Response.StatusCode = (int)HttpStatusCode.Conflict;

			if(filterContext.HttpContext.Request.IsAjaxRequest()) {
				filterContext.Result = new ContentResult { Content = string.Format(AjaxThrottlingMessage, _resetThrottleUrl) };
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

		private bool IsThrottleOverriden(ActionExecutingContext filterContext) {
			var overrideCookie = filterContext.HttpContext.Request.Cookies[DisableUserThrottlingCookieName];
			if(overrideCookie==null) {
				return IsThrottleOverrideActive(filterContext);
			}

			DeletOverrideCookie(filterContext,overrideCookie);
			SetOverrideCookieActive(filterContext);

			return true;
		}

		private void SetOverrideCookieActive(ActionExecutingContext filterContext) {
			filterContext.HttpContext.Response.Cookies.Add(
				new HttpCookie(UserThrottlingDisabledActiveCookieName, "true") { 
					Expires = DateTime.Now.Add(_overrideCookieTimeout),
					Path = "/"
				}
			);
		}

		private static void DeletOverrideCookie(ActionExecutingContext filterContext,HttpCookie overrideCookie) {
			overrideCookie.Expires = DateTime.Now.AddHours(-1);
			filterContext.HttpContext.Response.Cookies.Add(overrideCookie);
		}

		private bool IsThrottleOverrideActive(ActionExecutingContext filterContext) {
			return filterContext.HttpContext.Request[UserThrottlingDisabledActiveCookieName]!=null;
		}

		private void ExpireThrottlingIfNecessary() {
			lock(_lockObject) {
				if((DateTime.Now - _lastClean) < _timeStep) {
					return;
				}

				_throttlePerUser = new ConcurrentDictionary<string,ConcurrentLong>();
				_lastClean = DateTime.Now;
			}
		}

		
	}
}
