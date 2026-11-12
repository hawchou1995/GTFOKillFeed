using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace KillFeed.Tests;

internal static class Harness
{
	private static int _pass;

	private static readonly List<string> Failures = new List<string>();

	internal static void Check(string name, bool ok, string detail = null)
	{
		if (ok)
		{
			_pass++;
			Console.WriteLine("PASS " + name);
			return;
		}
		Failures.Add(name + ((detail == null) ? string.Empty : (" :: " + detail)));
		Console.WriteLine("FAIL " + name + ((detail == null) ? string.Empty : (" :: " + detail)));
	}

	internal static void Eq<T>(string name, T expected, T actual)
	{
		Check(name, EqualityComparer<T>.Default.Equals(expected, actual), "expected=" + expected + ";actual=" + actual);
	}

	internal static void Near(string name, float expected, float actual, float tolerance = 1e-6f)
	{
		float num = expected - actual;
		if (num < 0f)
		{
			num = -num;
		}
		Check(name, num <= tolerance, "expected=" + expected.ToString("R", CultureInfo.InvariantCulture) + ";actual=" + actual.ToString("R", CultureInfo.InvariantCulture));
	}

	internal static void Differs(string name, float a, float b)
	{
		Check(name, a != b, "a=" + a.ToString("R", CultureInfo.InvariantCulture) + ";b=" + b.ToString("R", CultureInfo.InvariantCulture));
	}

	internal static int Finish()
	{
		Console.WriteLine();
		Console.WriteLine("SUMMARY pass=" + _pass + ";fail=" + Failures.Count);
		for (int i = 0; i < Failures.Count; i++)
		{
			Console.WriteLine("  FAILED: " + Failures[i]);
		}
		return (Failures.Count == 0) ? 0 : 1;
	}
}

internal static class Scratch
{
	internal static string Root()
	{
		string text = Environment.GetEnvironmentVariable("KILLFEED_TEST_SCRATCH");
		if (string.IsNullOrEmpty(text))
		{
			text = Path.Combine(AppContext.BaseDirectory, "scratch");
		}
		Directory.CreateDirectory(text);
		return text;
	}

	internal static string NewDir(string tag)
	{
		string text = Path.Combine(Root(), tag + "-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(text);
		return text;
	}
}
