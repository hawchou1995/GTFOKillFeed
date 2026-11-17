using System;
using HawChow.GTFOKillFeed;

// Regression for the live 1.2.7 defect: with a C-Foam Launcher held and the crosshair on an enemy, the bar
// showed "[头5×]". FirstPersonItemHolder.WieldedItem returns a fresh ItemEquippable wrapper, so the old
// `holder.WieldedItem is global::GlueGun` test could never be true. These assertions compile the real
// production probe (..\..\GlueGunWield.cs) and pin the live shape: the managed wrapper type stays
// ItemEquippable and only the simulated native class decides whether the cast succeeds.
internal static class GlueGunWieldTests
{
    internal static void Run(Action<bool, string> check)
    {
        var holder = new FirstPersonItemHolder();
        ItemEquippable glueGun = ItemEquippable.Native(typeof(GlueGun));
        ItemEquippable shotgun = ItemEquippable.Native(typeof(Shotgun));

        // 1. Live shape: the wrapper is an ItemEquippable, exactly like the interop getter produces.
        holder.WieldedItem = glueGun;
        check(holder.WieldedItem is ItemEquippable && !(holder.WieldedItem is GlueGun),
            "live shape: a wielded C-Foam Launcher is exposed as an ItemEquippable wrapper, so the legacy managed `is GlueGun` test is false");

        // 2. The production probe must still recognise it through the native class.
        check(GlueGunWield.IsHeld(holder), "native-class probe recognises the wielded C-Foam Launcher");

        // 3. Non-glue weapons never enter glue mode.
        holder.WieldedItem = shotgun;
        check(!GlueGunWield.IsHeld(holder) && holder.WieldedItem is ItemEquippable,
            "a held non-glue weapon stays out of glue mode");
        holder.WieldedItem = ItemEquippable.Native(typeof(Weapon));
        check(!GlueGunWield.IsHeld(holder), "a plain wielded weapon without a glue native class stays out of glue mode");
        holder.WieldedItem = ItemEquippable.Native(typeof(ItemEquippable));
        check(!GlueGunWield.IsHeld(holder), "a wielded ItemEquippable whose native class is the base type stays out of glue mode");

        // 4. Missing holder and missing item fall back safely.
        check(!GlueGunWield.IsHeld(null), "an empty holder never reports glue mode");
        holder.WieldedItem = null;
        check(!GlueGunWield.IsHeld(holder), "an empty wielded item never reports glue mode");

        // 5. Interop read/cast failures degrade to "unknown weapon" instead of claiming glue mode.
        // The thrown cast also stands in for Il2CppInterop's ObjectCollectedException: the native getter never
        // yields a zero-pointer wrapper (it returns null), and Il2CppObjectBase.Pointer throws once collected.
        holder.WieldedItem = glueGun;
        holder.ThrowOnRead = true;
        check(!GlueGunWield.IsHeld(holder), "a throwing WieldedItem read falls back to no glue mode");
        holder.ThrowOnRead = false;
        holder.WieldedItem = ItemEquippable.Native(typeof(GlueGun), throwOnCast: true);
        check(!GlueGunWield.IsHeld(holder), "a throwing native cast falls back to no glue mode");

        // 6. Weapon switches glue -> normal -> glue -> empty track the live holder every frame.
        holder.WieldedItem = ItemEquippable.Native(typeof(GlueGun));
        bool[] glueSequence = new bool[4];
        for (int i = 0; i < glueSequence.Length; i++)
        {
            glueSequence[i] = GlueGunWield.IsHeld(holder);
            holder.WieldedItem = i switch
            {
                0 => ItemEquippable.Native(typeof(Shotgun)),
                1 => ItemEquippable.Native(typeof(GlueGun)),
                _ => null
            };
        }
        check(glueSequence[0] && !glueSequence[1] && glueSequence[2] && !glueSequence[3],
            "glue -> normal -> glue -> empty is re-read from the live holder on every frame");

        // 7. Joint behaviour with the untouched C-Foam/blob and tag-selection policy.
        holder.WieldedItem = glueGun;
        check(EnemyTag(holder, true, 4f) == "[结沫*2]" && EnemyTag(holder, false, 4f) == "[结沫*2]",
            "a held glue gun shows the C-Foam blob count regardless of the weakspot switch");
        holder.WieldedItem = shotgun;
        check(EnemyTag(holder, true, 4f) == "[头5×]" && EnemyTag(holder, false, 4f) == string.Empty,
            "a normal weapon keeps the weakspot tag and still obeys ShowWeakspotPart");

        // 8. Target switches re-read the new enemy's glue tolerance while the weapon is unchanged.
        check(EnemyTag(holder, true, 4f) == "[头5×]", "first target keeps the weakspot tag");
        holder.WieldedItem = glueGun;
        check(EnemyTag(holder, true, float.NaN) == string.Empty && EnemyTag(holder, true, -1f) == string.Empty,
            "an unreadable or negative tolerance never fabricates a blob count");
        check(EnemyTag(holder, true, 1000000f) == "[结沫*免疫]", "the immune tolerance sentinel stays explicit");
        check(EnemyTag(holder, true, 3f) == "[结沫*1]" && EnemyTag(holder, true, 4f) == "[结沫*2]",
            "switching targets recomputes ceil(tolerance / 3) for the newly focused enemy");
        holder.WieldedItem = null;
        check(EnemyTag(holder, true, 4f) == "[头5×]" && EnemyTag(holder, false, 4f) == string.Empty,
            "losing the wielded item restores the weakspot-tag path and its visibility switch");
    }

    // Mirrors EnemyBar.Tick: glue mode comes from the production probe, the tag from the existing pure policy.
    private static string EnemyTag(FirstPersonItemHolder holder, bool showWeakspotPart, float glueTolerance)
        => BarPolicy.SelectEnemyTag(GlueGunWield.IsHeld(holder), showWeakspotPart, "[头5×]", BarPolicy.CfoamTag(glueTolerance));
}

// Test-only host boundary. The plugin compiles GlueGunWield.cs against the real GTFO interop assemblies; the
// smoke project compiles the same source against these minimal equivalents of the interop surface.
public class FirstPersonItemHolder
{
    internal bool ThrowOnRead;
    private ItemEquippable _wieldedItem;

    public ItemEquippable WieldedItem
    {
        get
        {
            if (ThrowOnRead) throw new InvalidOperationException("simulated native WieldedItem read failure");
            return _wieldedItem;
        }
        set => _wieldedItem = value;
    }
}

public class ItemEquippable
{
    // Stands in for the il2cpp class the native object really is; the managed wrapper type never changes.
    internal Type SimulatedNativeClass = typeof(ItemEquippable);
    internal bool ThrowOnCast;

    internal static ItemEquippable Native(Type nativeClass, bool throwOnCast = false)
        => new ItemEquippable { SimulatedNativeClass = nativeClass, ThrowOnCast = throwOnCast };

    // Mirrors Il2CppInterop's Il2CppObjectBase.TryCast<T>: the native class decides, a mismatch returns
    // null, a failure throws, and a match returns a fresh wrapper of T.
    public T TryCast<T>() where T : ItemEquippable
    {
        if (ThrowOnCast) throw new InvalidCastException("simulated native cast failure");
        if (SimulatedNativeClass != typeof(T)) return null;
        return (T)Activator.CreateInstance(typeof(T));
    }
}

public class Weapon : ItemEquippable { }
public class GlueGun : Weapon { }
public class Shotgun : ItemEquippable { }
