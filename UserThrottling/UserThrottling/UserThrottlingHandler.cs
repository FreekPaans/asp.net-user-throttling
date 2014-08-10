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
		const string ActivateUserThrottlingCookieName = "__activate_no_user_throttling";
		const string UserThrottlingDisabledActiveCookieName= "__user_throttling_disabled";
		const string UserIsThrottledCookieName= "__user_is_throttled";

		const string AjaxThrottlingMessage = "You are being throttled due to excessive usage, please refresh the page";

		readonly TimeSpan _throttleBackoff;

		readonly ActionExecutingContext _filterContext;
		private UserRequestsCounter _counter;
		
		public UserThrottlingHandler(TimeSpan throttleBackoff, ActionExecutingContext filterContext, UserRequestsCounter counter) {
			_throttleBackoff = throttleBackoff;
			_filterContext = filterContext;
			_counter = counter;
		}

		internal void Handle() {
			if(!IsAuthenticated()) {
				return;
			}

			if(IsThrottlingDisabledByUserRequest()) {
				return;
			}
						
			if(UserIsBeingThrottled()) {
				SetThrottlingResult();
				return;
			}

			if(_counter.IncrementCounterForUserAndCheckIfLimitReached(GetAuthenticatedUsername())) {
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
			_filterContext.HttpContext.Response.StatusCode = 429; //HTTP Too Many Requests

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

			_filterContext.Result = result;

			result.ViewBag.ActivateUserThrottlingCookieName = ActivateUserThrottlingCookieName;

			
		}

		private void BuildAjaxResult() {
			_filterContext.Result = new ContentResult { Content = AjaxThrottlingMessage };
		}

		

		private string GetAuthenticatedUsername() {
			return _filterContext.HttpContext.User.Identity.Name;
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
			return _counter.AddToTimeStep(_throttleBackoff);
		}

		private void DeletActivationCookie() {
			var overrideCookie = GetActivateOverrideThrottlingCookie();
			overrideCookie.Expires = DateTime.Now.AddHours(-1);
			_filterContext.HttpContext.Response.Cookies.Add(overrideCookie);
		}

		private bool IsThrottleOverrideActive() {
			return _filterContext.HttpContext.Request[UserThrottlingDisabledActiveCookieName]!=null;
		}

		
	}
}
