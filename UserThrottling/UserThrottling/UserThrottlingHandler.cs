using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Web;
using System.Web.Caching;
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
		private ActionExecutingContext _filterContext;
		readonly TimeSpan _throttleBackoff;
		
		public UserThrottlingHandler(TimeSpan timeStep,long requestsPerTimeStep, TimeSpan throttleBackoff) {
			_timeStep = timeStep;
			_requestsPerTimeStep = requestsPerTimeStep;
			_throttleBackoff = throttleBackoff;
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
				//_filterContext.
				SetThrottledBy("Counter");
				StartThrottling();
				return;
			}
		}

		private bool UserIsBeingThrottled() {
			//throttling is done 2 ways: via a node specific cache and via a cookie, this is necessary for 2 reasons: 
			// - we can't rely on cookies alone, because if the same request is being issued multiple times it won't contain the new cookie
			// - we do rely on a cookie to get a consistent experience across multiple nodes => either all will report the user is being throttled, or none will. This is necessary because this allows the user to bypass the throttling

			if(UserIsThrottledViaCookie()) {
				SetThrottledBy("Cookie");
				return true;
			}
			if(UserIsThrottledViaCache()) {
				SetThrottledBy("Cache");
				return true;
			}

			return false;
		}

		private void SetThrottledBy(string by) {
			_filterContext.HttpContext.Response.Headers["X-Throttled-By"] = by;
		}

		private bool UserIsThrottledViaCache() {
			if(HttpRuntime.Cache[GetAuthenticatedUsername()]!=null) {
				return true;
			}
			return false;
		}

		private bool UserIsThrottledViaCookie() {
			return _filterContext.HttpContext.Request.Cookies[UserIsThrottledCookieName]!=null;
		}

		private void StartThrottling() {
			SetThrottlingInCookie();
			SetThrottlingInCache();

			SetThrottlingResult();
		}

		private void SetThrottlingInCache() {
			HttpRuntime.Cache.Add(GetAuthenticatedUsername(), true,null,CalculateThrottleEnd(),Cache.NoSlidingExpiration,CacheItemPriority.Low,null);
		}

		private void SetThrottlingInCookie() {
			_filterContext.HttpContext.Response.Cookies.Add(new HttpCookie(UserIsThrottledCookieName,"true") {
				Expires = CalculateThrottleEnd(),
				Path = "/"
			});
		}

		private DateTime CalculateThrottleEnd() {
			return DateTime.Now.Add(_throttleBackoff);
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
