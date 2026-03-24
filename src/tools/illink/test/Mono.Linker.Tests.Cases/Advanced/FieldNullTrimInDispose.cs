using System;
using Mono.Linker.Tests.Cases.Expectations.Assertions;
using Mono.Linker.Tests.Cases.Expectations.Metadata;

namespace Mono.Linker.Tests.Cases.Advanced
{
	[SetupCompileArgument("/optimize+")]
	class FieldNullTrimInDispose
	{
		public static void Main()
		{
			var owner = new DisposableOwner();
			owner.Dispose();
		}

		[Kept]
		[KeptMember(".ctor()")]
		class DisposableOwner : IDisposable
		{
			// This field is only null-checked and null-assigned in Dispose.
			// The optimization should remove it entirely.
			OnlyReferencedInDispose _resource;

			[Kept]
			[ExpectedInstructionSequence(new[] {
				"nop",
				"nop",
				"nop",
				"nop",
				"nop",
				"nop",
				"nop",
				"nop",
				"nop",
				"nop",
				"ret",
			})]
			public void Dispose()
			{
				_resource?.Dispose();
				_resource = null;
			}
		}

		// This type should be completely removed because the only field
		// referencing it (_resource) is trimmed by the FieldNullTrim optimization.
		class OnlyReferencedInDispose : IDisposable
		{
			public void Dispose()
			{
			}
		}
	}
}
