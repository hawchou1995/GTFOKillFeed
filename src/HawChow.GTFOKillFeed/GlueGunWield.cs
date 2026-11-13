namespace HawChow.GTFOKillFeed;

// Shared production/smoke source. FirstPersonItemHolder.WieldedItem is declared as ItemEquippable and its
// getter builds a fresh ItemEquippable wrapper around the native pointer, so a managed `is GlueGun` test can
// never observe the weapon the player actually holds (live 1.2.7 symptom: C-Foam Launcher aimed at an enemy
// still showed a [头5×] tag). TryCast<T>() asks the native class, which is the only reliable answer.
internal static class GlueGunWield
{
    internal static bool IsHeld(FirstPersonItemHolder holder)
    {
        if (holder == null) return false;
        try
        {
            ItemEquippable wielded = holder.WieldedItem;
            if (wielded == null) return false;
            return wielded.TryCast<global::GlueGun>() != null;
        }
        catch
        {
            // A collected or already-destroyed holder/item must read as "unknown weapon" instead of
            // claiming glue mode, matching the previous read guard in EnemyFocus.Update.
            return false;
        }
    }
}
