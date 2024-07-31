// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using ILLink.Shared.DataFlow;
using Mono.Cecil;
using Mono.Linker.Dataflow;
using MultiValue = ILLink.Shared.DataFlow.ValueSet<ILLink.Shared.DataFlow.SingleValue>;


namespace ILLink.Shared.TrimAnalysis
{
	public partial record ArrayHeapValue : SingleValue
	{
		private static ValueSetLattice<SingleValue> MultiValueLattice => default;
		static int nextId;

		// TODO: ensure IDs are _unique_ within n entire method body!
		// TODO: probably should be indexed by its operation, NOT by an id.
		// because we will _revisit_ the operation, and need to find the previously
		// allocated array.

		// TODO: can use DefaultValueDictionary?
		Dictionary<int, ArrayValue> arrayValues = new Dictionary<int, ArrayValue> ();

		public ArrayHeapValue ()
		{
		}

		private ArrayHeapValue (Dictionary<int, ArrayValue> arrayValues)
		{
			this.arrayValues = arrayValues;
		}

		public ArrayValue? GetArray (MultiValue reference)
		{
			if (reference.AsSingleValue () is not ArrayReferenceValue arrayRef)
				return null;

			return GetArray (arrayRef);
		}

		ArrayValue GetArray (ArrayReferenceValue reference)
		{
			if (arrayValues.TryGetValue (reference.Id, out var array)) {
				return array;
			}

			// TODO
			throw new ArgumentException ($"Array reference {reference.Id} not found");
		}

		public ArrayReferenceValue CreateArray (int size, TypeReference elementType)
		{
			// TODO: prevent creating multiple
			return Allocate (new ConstIntValue (size), elementType);
		}

		// TODO: this really should track "one array of multiple possible sizes"
		// as different than "a valueset of arrays of different sizes"
		public MultiValue CreateArray (MultiValue size, TypeReference elementType)
		{
			MultiValue result = MultiValueLattice.Top;
			MultiValue arrayRef = MultiValueLattice.Top;
			foreach (var sizeValue in size.AsEnumerable ()) {
				var array = new ArrayValue (sizeValue, elementType);
				arrayValues[nextId] = array;
				result = MultiValueLattice.Meet (result, new MultiValue (array));
				arrayRef = MultiValueLattice.Meet (arrayRef, new MultiValue (new ArrayReferenceValue (nextId++)));
			}

			return arrayRef;
		}

		private ArrayReferenceValue Allocate (SingleValue size, TypeReference elementType)
		{
			var array = new ArrayValue (size, elementType);
			arrayValues[nextId] = array;
			return new ArrayReferenceValue (nextId++);
		}

		public override SingleValue DeepCopy ()
		{
			var newArrayValues = new Dictionary<int, ArrayValue> ();
			foreach (var entry in arrayValues) {
				newArrayValues[entry.Key] = (ArrayValue) entry.Value.DeepCopy ();
			}
			return new ArrayHeapValue (newArrayValues);
		}


		private static ArrayValue Meet (ArrayValue left, ArrayValue right)
		{
			var elementType = left.ElementType;
			var size = left.Size;
			Debug.Assert (elementType == right.ElementType);
			Debug.Assert (size == right.Size);

			var newArr = new ArrayValue (size, elementType);
			foreach (var (index, value) in left.IndexValues) {
				if (right.IndexValues.TryGetValue (index, out var rightValue)) {
					// Arbitrarily use the left basic block index.
					// This is only used for assignments within a basic block, so the subsequent basic block
					// that sees the merged state will have a different index either way.
					newArr.IndexValues[index] = new ValueBasicBlockPair (MultiValueLattice.Meet (value.Value, rightValue.Value), value.BasicBlockIndex);
				} else {
					newArr.IndexValues[index] = new ValueBasicBlockPair (value.Value.DeepCopy (), value.BasicBlockIndex);
				}
			}

			foreach (var (index, value) in right.IndexValues) {
				if (!left.IndexValues.ContainsKey (index)) {
					newArr.IndexValues[index] = new ValueBasicBlockPair (value.Value.DeepCopy (), value.BasicBlockIndex);
				}
			}

			return newArr;
		}

		// Effectively union
		public ArrayHeapValue Meet (ArrayHeapValue other)
		{
			// NOTE: should never see same array ID in both. Because that would mean we're trying to track two separately
			// allocated arrays as one value. When a variable is assigned one new array in one branch, and another new array in another branch,
			// those should be merged as a multivalue of arrayref, to two different arrays, each with a singlevalue as its size (not multivalue).
			// Actually...
			// We _could_ see the same ID in both, if different branches _mutated_ the same array.
			var newArrayValues = new Dictionary<int, ArrayValue> ();
			foreach (var (id, arr) in arrayValues) {
				if (other.arrayValues.TryGetValue (id, out var otherArr)) {
					newArrayValues[id] = Meet (arr, otherArr);
				} else {
					newArrayValues[id] = (ArrayValue) arr.DeepCopy ();
				}
			}
			foreach (var (id, arr) in other.arrayValues) {
				if (!arrayValues.ContainsKey (id)) {
					newArrayValues[id] = (ArrayValue) arr.DeepCopy ();
				}
			}
			return new ArrayHeapValue (newArrayValues);
		}
	}
}
