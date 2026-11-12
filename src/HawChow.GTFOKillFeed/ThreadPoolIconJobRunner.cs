using System;
using System.Threading;

namespace HawChow.GTFOKillFeed;

internal sealed class ThreadPoolIconJobRunner : IIconJobRunner
{
	internal static readonly ThreadPoolIconJobRunner Instance = new ThreadPoolIconJobRunner();

	public void Run(Action job)
	{
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Expected O, but got Unknown
		if (job == null)
		{
			return;
		}
		ThreadPool.QueueUserWorkItem((WaitCallback)delegate
		{
			try
			{
				job.Invoke();
			}
			catch
			{
			}
		});
	}
}
