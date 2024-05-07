// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using ILLink.Shared.DataFlow;

namespace Mono.Linker.DataFlow
{
	public struct FeatureChecksValue : INegate<FeatureChecksValue>, IDeepCopyValue<FeatureChecksValue>
	{
        public FeatureChecksValue Negate () => default;

        public FeatureChecksValue DeepCopy () => default;
	}
}
