using Xunit;

// CredentialsWebFactory configures the app through process-global environment variables — the only
// channel Program.cs reads before builder.Build(), which is earlier than any WebApplicationFactory
// hook runs. Parallel classes would overwrite each other's settings, so the assembly runs serially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
