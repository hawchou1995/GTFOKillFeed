using UnityEngine;

namespace HawChow.GTFOKillFeed;

internal sealed class UnityIconSink : IIconSink
{
	public object Create(byte[] bytes)
	{
		if (bytes == null || bytes.Length == 0)
		{
			return null;
		}
		Texture2D texture2D = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
		if (!texture2D.LoadImage(bytes))
		{
			Object.Destroy(texture2D);
			return null;
		}
		texture2D.hideFlags = HideFlags.HideAndDontSave;
		texture2D.wrapMode = TextureWrapMode.Clamp;
		texture2D.filterMode = FilterMode.Bilinear;
		return texture2D;
	}

	public void Release(object texture)
	{
		if (texture is Texture2D texture2D && texture2D != null)
		{
			Object.Destroy(texture2D);
		}
	}
}
