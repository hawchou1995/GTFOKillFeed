using System.Runtime.CompilerServices;

namespace HawChow.GTFOKillFeed;

public sealed class EnemyNameRule
{
	public uint DataBlockId
	{
		get;
		set;
	}

	public string RawName
	{
		get;
		set;
	} = string.Empty;


	public string DisplayName
	{
		get;
		set;
	} = string.Empty;

}
