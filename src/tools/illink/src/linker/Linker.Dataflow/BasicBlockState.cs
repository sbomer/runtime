// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using ILLink.Shared.DataFlow;
using ILLink.Shared.TrimAnalysis;
using Mono.Cecil.Cil;

namespace Mono.Linker.Dataflow
{
	public readonly struct LocalKey : IEquatable<LocalKey>
	{
		readonly VariableDefinition variableDefinition;

		public LocalKey (VariableDefinition variableDefinition) => this.variableDefinition = variableDefinition;

		public bool Equals (LocalKey other)
		{
			return variableDefinition == other.variableDefinition;
		}
	}

	public struct BasicBlockState<TValue, TContext> : IEquatable<BasicBlockState<TValue, TContext>>
		where TValue : IEquatable<TValue>
		where TContext : IEquatable<TContext>
	{
		public DefaultValueDictionary<LocalKey, TValue> Locals;

		public ValueStack<TValue> Stack;

		public bool Equals (BasicBlockState<TValue, TContext> other)
		{
			return Locals.Equals (other.Locals) && Stack.Equals (other.Stack);
		}

		public BasicBlockState (TValue defaultValue)
			: this (new DefaultValueDictionary<LocalKey, TValue> (defaultValue), new ValueStack<TValue> ())
		{
		}

		public BasicBlockState (DefaultValueDictionary<LocalKey, TValue> dictionary, ValueStack<TValue> stack)
		{
			Locals = dictionary;
			Stack = stack;
		}

		public BasicBlockState (DefaultValueDictionary<LocalKey, TValue> dictionary)
			: this (dictionary, new ValueStack<TValue> ())
		{
		}

		public TValue GetLocal (LocalKey key) => Locals.Get (key);

		public void SetLocal (LocalKey key, TValue value) => Locals.Set (key, value);

		public void Push (TValue value) => Stack.Push (value);

		public TValue Pop () => Stack.Pop ();

		public TValue Pop (int count) => Stack.Pop (count);
	}

	public readonly struct BlockStateLattice<TValue, TContext, TValueLattice, TContextLattice> : ILattice<BasicBlockState<TValue, TContext>>
		where TValue : IEquatable<TValue>
		where TContext : struct, IEquatable<TContext>
		where TValueLattice : ILattice<TValue>
	{
		public readonly DictionaryLattice<LocalKey, TValue, TValueLattice> LocalsLattice;
		public readonly StackLattice<TValue, TValueLattice> StackLattice;
		public BasicBlockState<TValue, TContext> Top { get; }

		public BlockStateLattice (TValueLattice valueLattice)
		{
			LocalsLattice = new DictionaryLattice<LocalKey, TValue, TValueLattice> (valueLattice);
			StackLattice = new StackLattice<TValue, TValueLattice> (valueLattice);
			Top = new (LocalsLattice.Top);
		}

		public BasicBlockState<TValue, TContext> Meet (BasicBlockState<TValue, TContext> left, BasicBlockState<TValue, TContext> right)
		{
			var dictionary = LocalsLattice.Meet (left.Locals, right.Locals);
			var stack = StackLattice.Meet (left.Stack, right.Stack);
			return new BasicBlockState<TValue, TContext> (dictionary, stack);
		}
	}
}
