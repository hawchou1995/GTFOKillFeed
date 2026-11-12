using BepInEx.Configuration;

namespace HawChow.GTFOKillFeed;

internal sealed class SoundConfigBinding
{
	internal ConfigEntry<bool> Master;

	internal ConfigEntry<float> LegacyVolume;

	internal ConfigEntry<bool> HitEnabled;

	internal ConfigEntry<bool> KillEnabled;

	internal ConfigEntry<float> HitVolume;

	internal ConfigEntry<float> KillVolume;

	internal SoundConfigPlan Plan;

	internal static SoundConfigBinding Bind(ConfigFile config, SoundConfigPlan plan)
	{
		SoundConfigBinding soundConfigBinding = new SoundConfigBinding();
		soundConfigBinding.Plan = plan;
		soundConfigBinding.Master = config.Bind("HitSound", SoundConfigPlan.KeyMasterEnabled, plan.BindMasterEnabled, "命中/击杀音效的总开关（父开关），默认 true：默认值只在该键缺失时生效。1.2.16 的旧值原样保留并继续生效——关闭即两路都不出声，改回 true 两路立即恢复（对应通道被单独关闭时除外）。与 MuteVanillaIndicator 互不联动。保存即热重载。");
		soundConfigBinding.LegacyVolume = config.Bind("HitSound", SoundConfigPlan.KeyLegacyVolume, plan.BindLegacyVolume, "音效主音量（1.2.16 旧键，默认 0.8 = 100%）。本版起它是命中/击杀两路的公用主音量：通道实际输出 = 通道音量 × 本值 ÷ 0.8，再夹取 0–1。旧配置升级后本键继续有效，热改它两路一起变；0 等于静音但不改变开关状态；NaN/Infinity 回落 0.8。原值不会被改写或删除。保存即热重载。");
		soundConfigBinding.HitEnabled = config.Bind("HitSound", SoundConfigPlan.KeyHitEnabled, plan.BindHitEnabled, "命中音（普通/弱点/护甲命中）独立开关。新安装默认 false；已有 1.2.16 旧键时默认 true 并跟随父开关 Enabled，因此旧用户把旧总开关改回 true 就能恢复声音。父开关关闭时本项无效。保存即热重载。");
		soundConfigBinding.KillEnabled = config.Bind("HitSound", SoundConfigPlan.KeyKillEnabled, plan.BindKillEnabled, "击杀音（按 MINOR/ELITE/BOSS 档位与飞行/装甲飞行）独立开关。新安装默认 false；已有 1.2.16 旧键时默认 true 并跟随父开关 Enabled，因此旧用户升级后声音与音量保持不变。父开关关闭时本项无效。保存即热重载。");
		soundConfigBinding.HitVolume = config.Bind("HitSound", SoundConfigPlan.KeyHitVolume, plan.BindHitVolume, "命中音通道音量，范围 0–1。实际输出 = 本值 × 主音量（HitSound/Volume）÷ 0.8。新安装默认 0.4（主音量 0.8 时实际就是 0.4）；旧配置升级时默认 0.8，因此升级前后实际音量逐值相同。越界夹取，NaN/Infinity 回落该默认值；0 等于不播放，但不改变开关状态。保存即热重载。");
		soundConfigBinding.KillVolume = config.Bind("HitSound", SoundConfigPlan.KeyKillVolume, plan.BindKillVolume, "击杀音通道音量，范围 0–1。实际输出 = 本值 × 主音量（HitSound/Volume）÷ 0.8。新安装默认 0.4（主音量 0.8 时实际就是 0.4）；旧配置升级时默认 0.8，因此升级前后实际音量逐值相同。越界夹取，NaN/Infinity 回落该默认值；0 等于不播放，但不改变开关状态。保存即热重载。");
		return soundConfigBinding;
	}

	// The effective volumes here are what CueWavePlayer receives; LegacyVolume is read on
	// every call, so editing the old single volume and hot-reloading moves both channels.
	internal SoundSettings Current()
	{
		float channelFallback = ((Plan != null) ? Plan.ChannelFallback : SoundSettings.ReferenceMasterVolume);
		return SoundSettings.Resolve(Master == null || Master.Value, HitEnabled == null || HitEnabled.Value, KillEnabled == null || KillEnabled.Value, (HitVolume != null) ? HitVolume.Value : channelFallback, (KillVolume != null) ? KillVolume.Value : channelFallback, (LegacyVolume != null) ? LegacyVolume.Value : SoundSettings.ReferenceMasterVolume, channelFallback);
	}
}
