using System;
using System.Globalization;

namespace HawChow.GTFOKillFeed;

// The host's lethal credit stays exact. Client estimates only affect these HUD totals.
internal sealed class KillCounterTotals
{
    internal const long ClientWindowMs = 750;
    private readonly object _gate = new();
    private readonly int[] _counts = new int[3], _estimates = new int[3];
    private readonly System.Collections.Generic.Dictionary<long, ClientLife> _client = new();
    private bool _inLevel;

    private struct ClientLife
    {
        internal long Damage, HitAt;
        internal ushort NetworkId;
        internal object Volley;
        internal float Health, Dealt;
        internal KillTier Tier;
        internal bool Closed, Estimated;
    }

    internal void Add(KillTier tier, long agent = 0)
    {
        int i = (int)tier;
        if ((uint)i >= 3) return;
        lock (_gate)
        {
            if (agent != 0 && _client.TryGetValue(agent, out var life))
            {
                if (life.Estimated) _estimates[(int)life.Tier]--;
                life.Estimated = false; life.Closed = true; life.Volley = null;
                _client[agent] = life;
            }
            if (_counts[i] < int.MaxValue) _counts[i]++;
        }
    }

    internal bool ObserveClientHit(long agent, long damage, ushort networkId, long now, float health, float dealt, object volley)
    {
        if (agent == 0 || damage == 0 || !float.IsFinite(health) || health <= 0 || !float.IsFinite(dealt) || dealt <= 0) return false;
        lock (_gate)
        {
            if (!_inLevel) return false;
            bool known = _client.TryGetValue(agent, out var life);
            bool same = known && life.Damage == damage && life.NetworkId == networkId;
            if (same && life.Closed) return false;
            if (!same || volley == null || !ReferenceEquals(life.Volley, volley) || life.Health != health
                || now < life.HitAt || now - life.HitAt > ClientWindowMs)
                life = new ClientLife { Damage = damage, NetworkId = networkId, Health = health, Volley = volley };
            // Keep the time of the first lethal candidate; extra pellets cannot extend the death window.
            if (life.Dealt < life.Health) life.HitAt = now;
            life.Dealt = (float)Math.Min(health, (double)life.Dealt + dealt);
            _client[agent] = life;
            return life.Dealt >= health;
        }
    }

    internal bool ConfirmClientDeath(long agent, long damage, ushort networkId, long now, KillTier tier, bool competingSource)
    {
        int i = (int)tier;
        lock (_gate)
        {
            if (!_inLevel || (uint)i >= 3 || !_client.TryGetValue(agent, out var life) || life.Closed
                || life.Damage != damage || life.NetworkId != networkId) return false;
            double age = (double)now - life.HitAt;
            bool credit = !competingSource && age >= 0 && age <= ClientWindowMs && life.Dealt >= life.Health;
            life.Closed = true; life.Volley = null; life.Tier = tier;
            life.Estimated = credit && _estimates[i] < int.MaxValue;
            if (life.Estimated) _estimates[i]++;
            _client[agent] = life;
            return life.Estimated;
        }
    }

    internal void ForgetClientLife(long agent) { lock (_gate) _client.Remove(agent); }
    internal void EnterLevel() { lock (_gate) _inLevel = true; }
    internal int Get(KillTier tier) => Get(tier, out _);
    internal int Get(KillTier tier, out bool estimated)
    {
        int i = (int)tier;
        lock (_gate)
        {
            estimated = (uint)i < 3 && _estimates[i] != 0;
            return (uint)i < 3 ? (int)Math.Min(int.MaxValue, (long)_counts[i] + _estimates[i]) : 0;
        }
    }
    internal void Reset()
    {
        lock (_gate)
        {
            _inLevel = false; _client.Clear();
            Array.Clear(_counts, 0, _counts.Length); Array.Clear(_estimates, 0, _estimates.Length);
        }
    }
    internal static string Format(int value, bool estimated = false) => (estimated && value > 0 ? "~" : "") + Math.Max(0, value).ToString(CultureInfo.InvariantCulture);
}

internal readonly struct KillCounterLayout
{
    internal readonly float X, Y, Scale, Width, Height;
    internal KillCounterLayout(float x, float y, float scale, float width, float height)
    { X = x; Y = y; Scale = scale; Width = width; Height = height; }
}

internal static class KillCounterPolicy
{
    internal const float Icon = 20f, Font = 14f, IconGap = 4f, GroupGap = 12f, BelowGap = 6f;
    internal static float GroupWidth(float textWidth) => Icon + IconGap + Math.Max(Font, textWidth);
    internal static bool TryLayout(float right, float bottom, float desiredScale, float contentWidth,
        float screenWidth, float screenHeight, out KillCounterLayout layout)
    {
        layout = default;
        if (!Finite(right) || !Finite(bottom) || !Finite(desiredScale) || !Finite(contentWidth)
            || !Finite(screenWidth) || !Finite(screenHeight) || screenWidth < 32 || screenHeight < 32
            || right < 24 || right > screenWidth - 2 || bottom < 0 || bottom >= screenHeight || contentWidth <= 0) return false;
        float scale = Math.Min(Math.Max(desiredScale, 0.7f), 2f);
        scale = Math.Min(scale, (right - 8f) / contentWidth);
        float y = bottom + BelowGap * scale;
        if (scale < 0.35f || y + Icon * scale > screenHeight - 4f) return false;
        layout = new KillCounterLayout(right - contentWidth * scale, y, scale, contentWidth * scale, Icon * scale);
        return true;
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
