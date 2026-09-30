// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Xunit;

// The tests share the static LanguageConfigRegistry (and call Clear() on it), so test classes
// must not run in parallel: one class clearing the registry would break another class's test.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
