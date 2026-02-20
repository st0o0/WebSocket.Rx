using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.NativeAot;

var config = DefaultConfig.Instance
    //.AddFilter(new SimpleFilter(bc => !bc.Descriptor.Categories.Contains("E2E")))
    .AddJob(Job.Default
        .WithToolchain(
            NativeAotToolchain.CreateBuilder()
                .UseNuGet()
                .IlcOptimizationPreference()
                .DisplayName("AOT-Speed")
                .ToToolchain())
        .WithRuntime(NativeAotRuntime.Net10_0)
        .WithId("Speed"))
    .AddJob(Job.Default
        .WithRuntime(CoreRuntime.Core10_0)
        .WithId("CoreCLR"));

BenchmarkSwitcher
    .FromAssembly(typeof(Program).Assembly)
    .Run(args, config);