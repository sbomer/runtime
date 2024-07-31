// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Text;
using ILLink.Shared.DataFlow;
using Mono.Cecil;
using Mono.Linker.Dataflow;
using MultiValue = ILLink.Shared.DataFlow.ValueSet<ILLink.Shared.DataFlow.SingleValue>;


namespace ILLink.Shared.TrimAnalysis
{
	public sealed partial record ArrayReferenceValue(int Id) : SingleValue
	{
		public override SingleValue DeepCopy ()
		{
			return new ArrayReferenceValue (Id);
		}
	}
}
