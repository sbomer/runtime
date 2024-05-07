// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using ILLink.Shared.DataFlow;

namespace Mono.Linker.DataFlow
{
	public struct FeatureContext : IEquatable<FeatureContext>, IDeepCopyValue<FeatureContext>
	{
		public bool Equals (FeatureContext other) => true;
		public override bool Equals (object? obj) => obj is FeatureContext other && Equals (other);
		public override int GetHashCode () => typeof (FeatureContext).GetHashCode ();

		public FeatureContext DeepCopy () => default;
	}

	public readonly struct FeatureContextLattice : ILattice<FeatureContext>
	{
		public FeatureContextLattice () { }

		public FeatureContext Top { get; } = default;

		public FeatureContext Meet (FeatureContext left, FeatureContext right) => default;
	}
}
