using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Web;
using System.Web.Mvc;

namespace UserThrottling {
	class UserThrottlingHandler {
		readonly static object _lockObject = new object();
		ConcurrentDictionary<string,ConcurrentLong> _throttlePerUser = new ConcurrentDictionary<string,ConcurrentLong>();
		DateTime _lastClean = DateTime.Now;

		const string ActivateUserThrottlingCookieName = "__activate_no_user_throttling";
		const string UserThrottlingDisabledActiveCookieName= "__no_user_throttling_active";
		const string AjaxThrottlingMessage = "You are being throttled due to excessive usage, please refresh the page";

		readonly TimeSpan _timeStep;
		readonly long _requestsPerTimeStep;
		readonly TimeSpan _overrideCookieTimeout;
		private ActionExecutingContext _filterContext;
		
		public UserThrottlingHandler(TimeSpan timeStep,long requestsPerTimeStep,TimeSpan  overrideCookieTimeout) {
			_timeStep = timeStep;
			_requestsPerTimeStep = requestsPerTimeStep;
			_overrideCookieTimeout = overrideCookieTimeout;
		}

		internal void Handle(ActionExecutingContext context) {
			_filterContext= context;
			if(!IsAuthenticated()) {
				return;
			}

			if(!HasTimeStep()) {
				return;
			}

			if(IsThrottlingDisabledByUserRequest(_filterContext)) {
				return;
			}

			RefreshThrottlingIfNecessary();

			if(IncrementCounterForUserAndCheckIfLimitReached(GetAuthenticatedUsername())) {
				ApplyThrottling();
			}
		}

		private void ApplyThrottling() {
			BuildResult();
		}

		private void BuildResult() {
			_filterContext.HttpContext.Response.StatusCode = (int)HttpStatusCode.Conflict;

			if(_filterContext.HttpContext.Request.IsAjaxRequest()) {
				BuildAjaxResult();
				return;
			}

			BuildViewResult();
		}

		private void BuildViewResult() {
			var result = new ViewResult {
				ViewName = "Throttling",
				ViewData = new ViewDataDictionary(),
			};

			result.ViewBag.ActivateUserThrottlingCookieName = ActivateUserThrottlingCookieName;

			_filterContext.Result = result;
		}

		private void BuildAjaxResult() {
			_filterContext.Result = new ContentResult { Content = AjaxThrottlingMessage };
		}

		private bool IncrementCounterForUserAndCheckIfLimitReached(string username) {
			var counter = _throttlePerUser.GetOrAdd(username,ConcurrentLong.Zero);

			var lastValue = counter.Increment();

			if(lastValue <= _requestsPerTimeStep) {
				return false;
			}

			return true;
		}

		private string GetAuthenticatedUsername() {
			return _filterContext.HttpContext.User.Identity.Name;
		}

		private bool HasTimeStep() {
			return _timeStep!=TimeSpan.Zero;
		}

		private bool IsAuthenticated() {
			return _filterContext.HttpContext.Request.IsAuthenticated;
		}

		private bool IsThrottlingDisabledByUserRequest(ActionExecutingContext filterContext) {
			var overrideCookie = filterContext.HttpContext.Request.Cookies[ActivateUserThrottlingCookieName];

			if(overrideCookie==null) {
				return IsThrottleOverrideActive(filterContext);
			}

			DeletOverrideCookie(overrideCookie);
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

		private void DeletOverrideCookie(HttpCookie overrideCookie) {
			overrideCookie.Expires = DateTime.Now.AddHours(-1);
			_filterContext.HttpContext.Response.Cookies.Add(overrideCookie);
		}

		private bool IsThrottleOverrideActive(ActionExecutingContext filterContext) {
			return filterContext.HttpContext.Request[UserThrottlingDisabledActiveCookieName]!=null;
		}

		private void RefreshThrottlingIfNecessary() {
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
