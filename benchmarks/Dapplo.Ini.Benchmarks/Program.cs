// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using BenchmarkDotNet.Running;

// Run all:            dotnet run -c Release -- --filter *
// Run one class:      dotnet run -c Release -- --filter *ParserBenchmarks*
// Quick (less exact): dotnet run -c Release -- --filter * --job short
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
