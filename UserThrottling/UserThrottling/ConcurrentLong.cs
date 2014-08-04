using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace UserThrottling {
	class ConcurrentLong {
		long _counter;

		public long Increment() {
			return Interlocked.Increment(ref _counter);
		}

		internal static ConcurrentLong Zero {
			get {
				return new ConcurrentLong();
			}
		}
	}
}
