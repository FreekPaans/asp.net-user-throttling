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
		readonly long _requestsPerTimeStep;
		readonly TimeSpan _timeStep;
		readonly TimeSpan _overrideCookieTimeout;

		readonly static object _lockObject = new object();
		ConcurrentDictionary<string,ConcurrentLong> _throttlePerUser = new ConcurrentDictionary<string,ConcurrentLong>();
		DateTime _lastClean = DateTime.Now;

		const string ActivateUserThrottlingCookieName = "__activate_no_user_throttling";
		const string UserThrottlingDisabledActiveCookieName= "__no_user_throttling_active";
		const string AjaxThrottlingMessage = "You are being throttled due to excessive usage, please refresh the page";

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

			if(IsThrottlingDisabledByUser(filterContext)) {
				return;
			}

			filterContext.HttpContext.Response.StatusCode = (int)HttpStatusCode.Conflict;

			if(filterContext.HttpContext.Request.IsAjaxRequest()) {
				filterContext.Result = new ContentResult { Content = AjaxThrottlingMessage };
				return;
			}

			var result = new ViewResult { 
				ViewName = "Throttling",
				ViewData = new ViewDataDictionary(),
			};

			result.ViewBag.ActivateUserThrottlingCookieName = ActivateUserThrottlingCookieName;

			filterContext.Result = result;
		}

		private bool IsThrottlingDisabledByUser(ActionExecutingContext filterContext) {
			var overrideCookie = filterContext.HttpContext.Request.Cookies[ActivateUserThrottlingCookieName];
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
