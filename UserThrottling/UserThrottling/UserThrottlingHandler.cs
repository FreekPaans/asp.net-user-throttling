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
		const string UserThrottlingDisabledActiveCookieName= "__user_throttling_disabled";
		const string UserIsThrottledCookieName= "__user_is_throttled";

		const string AjaxThrottlingMessage = "You are being throttled due to excessive usage, please refresh the page";

		readonly TimeSpan _timeStep;
		readonly long _requestsPerTimeStep;
		//readonly TimeSpan _overrideCookieTimeout;
		private ActionExecutingContext _filterContext;
		readonly TimeSpan _throttleBackoff;
		
		public UserThrottlingHandler(TimeSpan timeStep,long requestsPerTimeStep, TimeSpan throttleBackoff) {
			_timeStep = timeStep;
			_requestsPerTimeStep = requestsPerTimeStep;
			_throttleBackoff = throttleBackoff;
			//_overrideCookieTimeout = overrideCookieTimeout;
		}

		internal void Handle(ActionExecutingContext context) {
			_filterContext= context;

			if(!IsAuthenticated()) {
				return;
			}

			if(!HasTimeStep()) {
				return;
			}

			if(IsThrottlingDisabledByUserRequest()) {
				return;
			}
						
			if(UserIsBeingThrottled()) {
				SetThrottlingResult();
				return;
			}

			if(IncrementCounterForUserAndCheckIfLimitReached(GetAuthenticatedUsername())) {
				StartThrottling();
				return;
			}
		}

		private bool UserIsBeingThrottled() {
			return _filterContext.HttpContext.Request.Cookies[UserIsThrottledCookieName]!=null;
		}

		private void StartThrottling() {
			_filterContext.HttpContext.Response.Cookies.Add(new HttpCookie(UserIsThrottledCookieName,"true") {
				Expires = DateTime.Now.Add(_throttleBackoff),
				Path = "/"
			});

			SetThrottlingResult();
		}

		private void SetThrottlingResult() {
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
			FlushThrottleCounterIfNecessary();

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

		private bool IsThrottlingDisabledByUserRequest() {
			if(DoesUserWantToActivateOverrideThrottling()) {
				DeletActivationCookie();
				SetOverrideCookieActive();
				return true;
			}

			
			return IsThrottleOverrideActive();
			
		}

		private bool DoesUserWantToActivateOverrideThrottling() {
			return GetActivateOverrideThrottlingCookie()!=null;
		}

		private HttpCookie GetActivateOverrideThrottlingCookie() {
			return _filterContext.HttpContext.Request.Cookies[ActivateUserThrottlingCookieName];
		}

		private void SetOverrideCookieActive() {
			_filterContext.HttpContext.Response.Cookies.Add(
				new HttpCookie(UserThrottlingDisabledActiveCookieName, "true") {
					Expires = DateTime.Now.Add(CalculateMaximumThrottledForPeriod()),
					Path = "/"
				}
			);
		}

		private TimeSpan CalculateMaximumThrottledForPeriod() {
			return _throttleBackoff.Add(_timeStep);
		}

		private void DeletActivationCookie() {
			var overrideCookie = GetActivateOverrideThrottlingCookie();
			overrideCookie.Expires = DateTime.Now.AddHours(-1);
			_filterContext.HttpContext.Response.Cookies.Add(overrideCookie);
		}

		private bool IsThrottleOverrideActive() {
			return _filterContext.HttpContext.Request[UserThrottlingDisabledActiveCookieName]!=null;
		}

		private void FlushThrottleCounterIfNecessary() {
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
