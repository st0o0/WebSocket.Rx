using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

var config = DefaultConfig.Instance
    .AddJob(Job.ShortRun
        .WithWarmupCount(1)
        .WithIterationCount(3)
        .WithInvocationCount(1)
        .WithUnrollFactor(1));

BenchmarkSwitcher
    .FromAssembly(typeof(Program).Assembly)
    .Run(args, config);