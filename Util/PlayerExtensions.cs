using AzuCraftyBoxes.Patches;
using static AzuCraftyBoxes.AzuCraftyBoxesPlugin;

namespace AzuCraftyBoxes.Util;

public enum PullMode
{
    Off = 0,
    All = 1,
    MineOnly = 2,
}

public static class PlayerPullingExtensions
{
    public static PullMode GetPullMode(this Player player)
    {
        if (!player.m_customData.TryGetValue(PreventPullingLogicKey, out string? rawValue) || !int.TryParse(rawValue, out int state) || state < 0 || state > 2)
        {
            state = (int)PullMode.All;
            player.m_customData[PreventPullingLogicKey] = state.ToString();
        }

        return (PullMode)state;
    }

    public static void SetPullMode(this Player player, PullMode mode)
    {
        player.m_customData[PreventPullingLogicKey] = ((int)mode).ToString();
    }

    public static bool IsPullingAllowed(this Player player)
    {
        return player.GetPullMode() != PullMode.Off;
    }

    public static void ApplyPullingStatusEffect(this Player player)
    {
        PullMode mode = player.GetPullMode();
        bool show = preventPullingStatusEffectDisplay.Value.isOn();
        SetStatusEffect(player, SE_ContainerPull.SE_ContainerPulling, show && mode == PullMode.Off);
        SetStatusEffect(player, SE_ContainerPull.SE_MineOnly, show && mode == PullMode.MineOnly);
    }

    public static PullMode CyclePullMode(this Player player)
    {
        PullMode next = player.GetPullMode() switch
        {
            PullMode.All => PullMode.MineOnly,
            PullMode.MineOnly => PullMode.Off,
            _ => PullMode.All,
        };
        player.SetPullMode(next);
        player.ApplyPullingStatusEffect();
        return next;
    }

    private static void SetStatusEffect(Player player, StatusEffect effect, bool on)
    {
        if (on)
            player.m_seman.AddStatusEffect(effect);
        else
            player.m_seman.RemoveStatusEffect(effect);
    }
}