namespace HawChow.GTFOKillFeed;

internal interface IIconSink
{
	object Create(byte[] bytes);

	void Release(object texture);
}
