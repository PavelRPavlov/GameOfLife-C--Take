// These are heavyweight integration tests: each spins up an in-memory ASP.NET host plus real
// SignalR long-polling connections. Running them in parallel saturates the machine and makes the
// timing-sensitive SignalR delivery assertions flaky. Serial execution keeps them reliable.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
