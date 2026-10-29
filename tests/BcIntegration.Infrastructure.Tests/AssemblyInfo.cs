// Every test starts its own WireMock server on a free port. Started in parallel, two servers could end up on the same port and a request
// could be answered by the scenario of another test (an invalid answer then showed up as a transient failure). One after the other is
// reliable and the whole project still runs in seconds.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
