using System;

namespace HawChow.GTFOKillFeed;

internal interface IIconJobRunner
{
	void Run(Action job);
}
