using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace UserThrottling {
	public class UserThrottlingConfiguration {
		public long RequestsPerTimeStep{get;set;}
		public TimeSpan TimeStep{get;set;} 
		public TimeSpan ThrottleBackoff{get;set;}
	}
}
