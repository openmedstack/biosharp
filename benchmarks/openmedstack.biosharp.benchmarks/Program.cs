using System;
using System.Linq;
using BenchmarkDotNet.Running;
using OpenMedStack.BioSharp.Benchmarks;

if (args.Length > 0 && string.Equals(args[0], "merge-report", StringComparison.OrdinalIgnoreCase))
{
	var exitCode = BenchmarkComparisonReportPostProcessor.Run(args.Skip(1).ToArray());
	Environment.Exit(exitCode);
}

if (args.Length > 0 && string.Equals(args[0], "warm-report", StringComparison.OrdinalIgnoreCase))
{
	var exitCode = WarmBenchmarkReportRunner.Run(args.Skip(1).ToArray());
	Environment.Exit(exitCode);
}

if (args.Length > 0 && string.Equals(args[0], "linux-report", StringComparison.OrdinalIgnoreCase))
{
	var exitCode = LinuxBenchmarkMarkdownReportGenerator.Run(args.Skip(1).ToArray());
	Environment.Exit(exitCode);
}

if (args.Length > 0 && string.Equals(args[0], "variant-smoke", StringComparison.OrdinalIgnoreCase))
{
	var exitCode = await VariantCallingSmokeCommand.Run(args.Skip(1).ToArray());
	Environment.Exit(exitCode);
}

if (args.Length > 0 && string.Equals(args[0], "bcl-realdata", StringComparison.OrdinalIgnoreCase))
{
	var exitCode = await BclRealDataCommand.Run(args.Skip(1).ToArray());
	Environment.Exit(exitCode);
}

BenchmarkSwitcher.FromAssembly(typeof(PipelineBenchmarks).Assembly).Run(args: args);
