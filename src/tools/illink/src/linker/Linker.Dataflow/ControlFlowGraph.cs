// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Tracing;
using System.Linq;
using ILLink.Shared.DataFlow;
using Mono.Cecil.Cil;
using ControlFlowBranch = ILLink.Shared.DataFlow.IControlFlowGraph<
	Mono.Linker.Dataflow.BasicBlock,
	Mono.Linker.Dataflow.Region
>.ControlFlowBranch;

namespace Mono.Linker.Dataflow
{
	public record struct Region : IRegion<Region>
	{
		public RegionKind Kind => throw new NotImplementedException ();
	}

	public struct BasicBlock : IBlock<BasicBlock>
	{
		private readonly int _id;

		private readonly MethodIL _methodIL;

		private readonly int _startInstructionIndex;

		private readonly int _endInstructionIndex;

		public BasicBlock (MethodIL methodIL, int id, int start, int end)
		{
			_methodIL = methodIL;
			_startInstructionIndex = start;
			_endInstructionIndex = end;
			_id = id;
		}

		public ConditionKind ConditionKind => throw new NotImplementedException ();

		public MethodIL MethodIL => _methodIL;

		public int Id => _id;

		public Instruction? FirstInstruction => _startInstructionIndex >= 0 ? _methodIL.Instructions[_startInstructionIndex] : null;

		public Instruction? LastInstruction => _endInstructionIndex >= 0 ? _methodIL.Instructions[_endInstructionIndex] : null;

		public IEnumerable<Instruction> GetInstructions ()
		{
			for (int i = _startInstructionIndex; i <= _endInstructionIndex; i++) {
				yield return _methodIL.Instructions[i];
			}
		}

		public bool Equals (BasicBlock other)
		{
			return _startInstructionIndex == other._startInstructionIndex && _endInstructionIndex == other._endInstructionIndex;
		}

		public override bool Equals (object? obj) => obj is BasicBlock other && Equals (other);
		public override int GetHashCode () => HashCode.Combine (_methodIL, _startInstructionIndex, _endInstructionIndex, _id);

		public override string ToString ()
		{
			if (_endInstructionIndex == -1) return "Empty";

			return $"[IL_{_methodIL.Instructions[_startInstructionIndex].Offset:X4}, IL_{_methodIL.Instructions[_endInstructionIndex].Offset:X4}]";
		}
	}

	public struct ControlFlowGraph : IControlFlowGraph<BasicBlock, Region>
	{
		private readonly List<BasicBlock> _blocks;
		private readonly List<List<(int Source, ConditionKind ConditionKind)>> _predecessors;
		private readonly List<List<(int Target, ConditionKind ConditionKind)>> _successors;

		public override string ToString ()
		{
			if (_blocks.Count == 0) return "";

			var nodes = new List<string> ();

			foreach (var block in Blocks) {
				var predecessors = GetPredecessors (block).Select (o => o.Source.Id).ToList ();
				nodes.Add ($"Id: {block.Id}, Range: {block}, Predecessors: [{string.Join (",", predecessors)}]");
			}
			return string.Join (" | ", nodes);
		}

		public IEnumerable<BasicBlock> Blocks => _blocks;

		public BasicBlock Entry => _blocks[0];

		private ControlFlowGraph (List<BasicBlock> blocks, (List<List<(int Source, ConditionKind ConditionKind)>> Predecessors, List<List<(int Target, ConditionKind ConditionKind)>> Successors) edges)
		{
			_blocks = blocks;
			_predecessors = edges.Predecessors;
			_successors = edges.Successors;
		}

		public static bool TryCreate (MethodIL method, out ControlFlowGraph cfg)
		{
			cfg = default;
			if (CanCreateControlFlowGraph (method)) {
				cfg = Create (method);
				return true;
			}
			return false;
		}

		private static ControlFlowGraph Create (MethodIL method)
		{
			var firstInstructionToBlock = new Dictionary<int, BasicBlock> ();
			var blocks = ConstructBasicBlocks (method, firstInstructionToBlock);
			var edges = GetEdges (firstInstructionToBlock, blocks);

			return new ControlFlowGraph (blocks, edges);
		}

		private static bool CanCreateControlFlowGraph (MethodIL method)
		{
			return !method.HasExceptionHandlers;
		}

		public IEnumerable<ControlFlowBranch> GetPredecessors (BasicBlock block)
		{
			foreach (var pred in _predecessors[block.Id]) {
				yield return new ControlFlowBranch (_blocks[pred.Source], block, ImmutableArray<Region>.Empty, pred.ConditionKind);
			}
		}

		public IEnumerable<ControlFlowBranch> GetSuccessors (BasicBlock block)
		{
			foreach (var succ in _successors[block.Id]) {
				yield return new ControlFlowBranch (block, _blocks[succ.Target], ImmutableArray<Region>.Empty, succ.ConditionKind);
			}
		}

		public bool TryGetEnclosingTryOrCatchOrFilter (BasicBlock block, [NotNullWhen (true)] out Region tryOrCatchOrFilterRegion)
		{
			return false;
		}

		public bool TryGetEnclosingTryOrCatchOrFilter (Region region, [NotNullWhen (true)] out Region tryOrCatchOrFilterRegion)
		{
			return false;
		}

		public bool TryGetEnclosingFinally (BasicBlock block, [NotNullWhen (true)] out Region region)
		{
			return false;
		}

		public Region GetCorrespondingTry (Region cathOrFilterOrFinallyRegion)
		{
			throw new NotImplementedException ();
		}

		public IEnumerable<Region> GetPreviousFilters (Region catchOrFilterRegion)
		{
			throw new NotImplementedException ();
		}

		public bool HasFilter (Region catchRegion)
		{
			throw new NotImplementedException ();
		}

		public BasicBlock FirstBlock (Region region)
		{
			throw new NotImplementedException ();
		}

		public BasicBlock LastBlock (Region region)
		{
			throw new NotImplementedException ();
		}

		public static (List<List<(int Source, ConditionKind ConditionKind)>>, List<List<(int Target, ConditionKind ConditionKind)>>) GetEdges (Dictionary<int, BasicBlock> firstInstructionToBlock, List<BasicBlock> blocks)
		{
			var predecessors = new List<List<(int Source, ConditionKind ConditionKind)>> (blocks.Count);
			var successors = new List<List<(int Target, ConditionKind ConditionKind)>> (blocks.Count);

			foreach (var _ in blocks) {
				predecessors.Add (new List<(int Source, ConditionKind ConditionKind)> ());
				successors.Add (new List<(int Target, ConditionKind ConditionKind)> ());
			}

			//Add initial block connections
			AddEdge (0, 1, ConditionKind.Unconditional);

			foreach (var basicBlock in blocks) {

				if (basicBlock.LastInstruction == null) {
					continue;
				}

				var conditionKind = ConditionKind.Unconditional;
				// Handle branches
				if (basicBlock.LastInstruction.OpCode.IsControlFlowInstruction ()) {
					var jumpTargets = basicBlock.LastInstruction.GetJumpTargets ();
					bool isConditionalBranch = basicBlock.LastInstruction.OpCode.FlowControl == FlowControl.Cond_Branch;
					if (isConditionalBranch) {
						conditionKind = basicBlock.LastInstruction.OpCode.Code switch {
							Code.Brtrue => ConditionKind.WhenTrue,
							Code.Brtrue_S => ConditionKind.WhenTrue,
							Code.Brfalse => ConditionKind.WhenFalse,
							Code.Brfalse_S => ConditionKind.WhenFalse,
							_ => ConditionKind.Unknown
						};
					}

					foreach (var jumpTarget in jumpTargets) {
						var targetId = firstInstructionToBlock[jumpTarget.Offset].Id;
						AddEdge (basicBlock.Id, targetId, conditionKind);
					}

					if (isConditionalBranch && basicBlock.LastInstruction.Next != null) {
						var targetId = firstInstructionToBlock[basicBlock.LastInstruction.Next.Offset].Id;
						AddEdge (basicBlock.Id, targetId, conditionKind switch {
							ConditionKind.WhenTrue => ConditionKind.WhenFalse,
							ConditionKind.WhenFalse => ConditionKind.WhenTrue,
							_ => ConditionKind.Unknown
						});
					}
				}
				// Handle last block predecessors
				else if (basicBlock.LastInstruction.OpCode.FlowControl == FlowControl.Return) {
					AddEdge (basicBlock.Id, blocks.Count - 1, conditionKind);
				}
				// Handle fall through
				else if ((basicBlock.LastInstruction.OpCode.FlowControl == FlowControl.Next || basicBlock.LastInstruction.OpCode.FlowControl == FlowControl.Call) && basicBlock.LastInstruction.Next != null) {
					var targetId = firstInstructionToBlock[basicBlock.LastInstruction.Next.Offset].Id;
					AddEdge (basicBlock.Id, targetId, conditionKind);
				}
			}

			return (predecessors, successors);

			void AddEdge (int source, int target, ConditionKind conditionKind)
			{
				if (conditionKind is ConditionKind.WhenTrue or ConditionKind.WhenFalse) {
					// WhenTrue/WhenFalse branches should have exactly two targets
					if (successors[source].Count == 1) {
						var existingEdge = successors[source][0];
						Debug.Assert (existingEdge.ConditionKind is ConditionKind.WhenTrue or ConditionKind.WhenFalse);
						Debug.Assert (existingEdge.ConditionKind != conditionKind);
					}
				}
				predecessors[target].Add ((source, conditionKind));
				successors[source].Add ((target, conditionKind));
			}
		}

		private static List<BasicBlock> ConstructBasicBlocks (MethodIL methodBody, Dictionary<int, BasicBlock> firstInstructionToBlock)
		{
			var blocks = new List<BasicBlock> ();
			int blockStart = 0;

			// Add extra first block
			AddBlock (-1, -1);

			var leaders = methodBody.GetInitialBasicBlockInstructions ();

			for (int i = 1; i < methodBody.Instructions.Count; i++) {
				if (leaders.Contains (methodBody.Instructions[i].Offset)) {
					AddBlock (blockStart, i - 1);
					blockStart = i;
				}
			}

			AddBlock (blockStart, methodBody.Instructions.Count - 1);

			// Add extra last block
			AddBlock (-1, -1);

			return blocks;

			void AddBlock (int ilStart, int ilEnd)
			{
				var block = new BasicBlock (methodBody, blocks.Count, ilStart, ilEnd);
				blocks.Add (block);

				if (ilStart >= 0) {
					firstInstructionToBlock[methodBody.Instructions[ilStart].Offset] = block;
				}
			}
		}
	}
}
