using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace UserThrottling {
	class UserRequestsCounter {
		readonly static object _lockObject = new object();

		ConcurrentDictionary<string,ConcurrentLong> _throttlePerUser = new ConcurrentDictionary<string,ConcurrentLong>();
		DateTime _lastClean = DateTime.Now;

		readonly TimeSpan _timeStep;
		readonly long _requestsPerTimeStep;
		

		public UserRequestsCounter(long requests,TimeSpan timeStep) {
			_requestsPerTimeStep = requests;
			_timeStep = timeStep;
		}

		public bool IncrementCounterForUserAndCheckIfLimitReached(string username) {
			if(!HasTimeStep()) {
				return false;
			}

			FlushThrottleCounterIfNecessary();
			
			var counter = _throttlePerUser.GetOrAdd(username,ConcurrentLong.Zero);

			var lastValue = counter.Increment();

			if(lastValue <= _requestsPerTimeStep) {
				return false;
			}

			return true;
		}

		private bool HasTimeStep() {
			return _timeStep!=TimeSpan.Zero;
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


		internal TimeSpan AddToTimeStep(TimeSpan period) {
			return _timeStep.Add(period);
		}
	}
}
