using System;
using System.Collections.Generic;

namespace HawChow.GTFOKillFeed.Tests;

internal static class Program
{
	internal static int Passes;

	internal static int Failures;

	private static int Main(string[] args)
	{
		ConfigTests.Run();
		ViewModeTests.Run();
		OpacityTests.Run();
		PlacementTests.Run();
		CandidateTests.Run();
		ProjectionTests.Run();
		SwitchTests.Run();
		ConfigIntegrationTests.Run();
		OccluderRecoveryTests.Run();
		Console.WriteLine("RESULT pass=" + Passes + " fail=" + Failures);
		return (Failures == 0) ? 0 : 1;
	}

	internal static void Eq<T>(string name, T expected, T actual)
	{
		if (EqualityComparer<T>.Default.Equals(expected, actual))
		{
			Passes++;
			return;
		}
		Failures++;
		Console.WriteLine("FAIL " + name + ": expected=" + expected + " actual=" + actual);
	}

	internal static void True(string name, bool condition)
	{
		Eq(name, expected: true, condition);
	}

	internal static void Near(string name, float expected, float actual)
	{
		if (Math.Abs(expected - actual) <= 0.0001f)
		{
			Passes++;
			return;
		}
		Failures++;
		Console.WriteLine("FAIL " + name + ": expected=" + expected + " actual=" + actual);
	}
}
