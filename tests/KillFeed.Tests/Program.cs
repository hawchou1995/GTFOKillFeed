using System;

namespace KillFeed.Tests;

internal static class Program
{
	private static int Main(string[] args)
	{
		Console.WriteLine("KillFeed production-logic tests (linked sources, no Unity runtime)");
		Console.WriteLine("scratch=" + Scratch.Root());
		Console.WriteLine();
		SoundTests.Run();
		ConfigTests.Run();
		CombinedConfigTests.Run();
		MarkerTests.Run();
		CorpseTests.Run();
		SourceGateTests.Run();
		return Harness.Finish();
	}
}
