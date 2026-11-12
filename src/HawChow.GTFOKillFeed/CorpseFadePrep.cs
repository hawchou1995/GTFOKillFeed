using System;

namespace HawChow.GTFOKillFeed;

internal static class CorpseFadePrep
{
	internal static bool Attempt(Func<object> selectRenderer, Func<object, bool> begin, Action<System.Exception> onError)
	{
		object obj;
		try
		{
			obj = selectRenderer?.Invoke();
		}
		catch (System.Exception error)
		{
			Report(onError, error);
			return false;
		}
		if (obj == null)
		{
			return false;
		}
		try
		{
			return begin?.Invoke(obj) ?? false;
		}
		catch (System.Exception error2)
		{
			Report(onError, error2);
			return false;
		}
	}

	private static void Report(Action<System.Exception> onError, System.Exception error)
	{
		if (onError == null)
		{
			return;
		}
		try
		{
			onError.Invoke(error);
		}
		catch (System.Exception)
		{
		}
	}
}
