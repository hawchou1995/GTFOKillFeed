using System;

namespace HawChow.GTFOKillFeed;

internal sealed class HexColorCache
{
	private string _source;

	private bool _has;

	private float _red;

	private float _green;

	private float _blue;

	private float _alpha;

	private bool _parsed;

	internal string Source => _source;

	internal float Red => _red;

	internal float Green => _green;

	internal float Blue => _blue;

	internal float Alpha => _alpha;

	internal bool IsFallback => !_parsed;

	internal void Resolve(string value, float fallbackRed, float fallbackGreen, float fallbackBlue, float fallbackAlpha)
	{
		if (!_has || !string.Equals(value, _source, (StringComparison)4))
		{
			_has = true;
			_source = value;
			if (BarPolicy.TryParseHexColor(value, out var red, out var green, out var blue, out var alpha))
			{
				_red = red;
				_green = green;
				_blue = blue;
				_alpha = alpha;
				_parsed = true;
			}
			else
			{
				_red = fallbackRed;
				_green = fallbackGreen;
				_blue = fallbackBlue;
				_alpha = fallbackAlpha;
				_parsed = false;
			}
		}
	}

	internal void Reset()
	{
		_source = null;
		_has = false;
		_parsed = false;
		_red = (_green = (_blue = (_alpha = 1f)));
	}
}
