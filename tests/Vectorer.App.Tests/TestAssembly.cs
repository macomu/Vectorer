using Xunit;

// App tests share Avalonia's process-wide dispatcher and binding parser state.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
