using System;
using System.Reflection;

namespace HawChow.GTFOKillFeed;

// Optional, reflection-only bridge to Inas07's EOSExt_EMP. There is no compile-time reference and
// no Harmony patch on the third-party mod: the marker only needs the same public
// EMPPlayerHudHandler.Instance.IsEMPed() pair that AdvancedBIOCOM already relies on (verified against
// the published 1.1.8 assembly metadata). Every failure mode degrades to "no EMP" so a missing,
// renamed or not-yet-set-up handler can never hide the mark by mistake.
internal static class EmpBridge
{
    internal const string AssemblyName = "Inas07.EOSExt.EMP";
    internal const string HandlerTypeName = "EOSExt.EMP.Impl.Handlers.EMPPlayerHudHandler";

    private static PropertyInfo _instanceProperty;
    private static MethodInfo _isEmpedMethod;
    private static object _instance;
    private static int _failures;
    private static float _nextProbeAt = float.NegativeInfinity;

    internal static bool Available => _isEmpedMethod != null;
    internal static bool IsEmped { get; private set; }
    internal static int Failures => _failures;
    internal static bool Bound => _isEmpedMethod != null && _instanceProperty != null;
    internal static bool HasInstance => _instance != null;

    // Bounded diagnostics hook (production routes it into the budgeted [MARK] log): it reports the
    // first successful bind and at most three failure reasons, so a missing or rebuilt mod never
    // spams the log on every retry.
    internal static Action<string> Diagnostics;
    private static bool _reportedBind;
    private static int _reportedFailures;

    private static void Report(string message, bool failure)
    {
        if (failure)
        {
            if (_reportedFailures >= 3) return;
            _reportedFailures++;
        }
        try { Diagnostics?.Invoke(message); } catch { }
    }

    internal static void Reset(float now)
    {
        _instanceProperty = null;
        _isEmpedMethod = null;
        _instance = null;
        _failures = 0;
        IsEmped = false;
        _nextProbeAt = KillPolicy.IsFinite(now) ? now : 0f;
    }

    // Called once per HUD tick. hideDuringEmp is the configured opt-in: while it is false this
    // method does not touch reflection at all.
    internal static void Poll(float now, bool hideDuringEmp)
    {
        if (!TeammateMarkPolicy.ShouldProbeEmp(hideDuringEmp))
        {
            IsEmped = false;
            _nextProbeAt = float.NegativeInfinity;
            return;
        }
        if (!TeammateMarkPolicy.IsProbeDue(now, _nextProbeAt)) return;

        if (_isEmpedMethod == null || _instanceProperty == null)
        {
            if (!TryBindFromAppDomain(out _instanceProperty, out _isEmpedMethod))
            {
                Defer(now, false, "Inas07.EOSExt.EMP not loaded or handler shape missing");
                return;
            }
            if (!_reportedBind)
            {
                _reportedBind = true;
                Report("emp bridge bound: EMPPlayerHudHandler.Instance/IsEMPed resolved", false);
            }
        }
        if (_instance == null && !TryReadInstance(_instanceProperty, out _instance))
        {
            Defer(now, true, "EMPPlayerHudHandler.Instance is null");
            return;
        }
        if (!TryInvoke(_isEmpedMethod, _instance, out bool emped))
        {
            Defer(now, true, "IsEMPed() threw or returned a non-bool");
            return;
        }
        if (_failures > 0)
        {
            Report("emp bridge recovered after " + _failures + " failed probe(s)", false);
            _reportedFailures = 0;
        }
        IsEmped = emped;
        _failures = 0;
        _nextProbeAt = TeammateMarkPolicy.NextProbeAt(now, true);
    }

    private static void Defer(float now, bool bound, string reason)
    {
        IsEmped = false;
        _failures++;
        if (!bound) _failures = Math.Max(_failures, 1);
        float clock = KillPolicy.IsFinite(now) ? now : 0f;
        _nextProbeAt = clock + TeammateMarkPolicy.RetryDelay(_failures);
        Report("emp probe deferred: " + reason + ";failures=" + _failures, true);
        // A null instance is normal between levels (the handler clears it on despawn), so only the
        // binding itself is dropped, and only after repeated failures.
        if (_failures >= 3)
        {
            _instance = null;
            _instanceProperty = null;
            _isEmpedMethod = null;
        }
    }

    private static bool TryBindFromAppDomain(out PropertyInfo instanceProperty, out MethodInfo isEmpedMethod)
    {
        instanceProperty = null;
        isEmpedMethod = null;
        Assembly[] assemblies;
        try { assemblies = AppDomain.CurrentDomain.GetAssemblies(); }
        catch { return false; }
        for (int i = 0; i < assemblies.Length; i++)
        {
            Assembly assembly = assemblies[i];
            if (assembly == null) continue;
            string name;
            try { name = assembly.GetName().Name; }
            catch { continue; }
            if (!string.Equals(name, AssemblyName, StringComparison.Ordinal)) continue;
            if (TryBind(assembly, HandlerTypeName, out instanceProperty, out isEmpedMethod)) return true;
        }
        return false;
    }

    internal static bool TryBind(Assembly assembly, string typeName, out PropertyInfo instanceProperty, out MethodInfo isEmpedMethod)
    {
        instanceProperty = null;
        isEmpedMethod = null;
        if (assembly == null || string.IsNullOrWhiteSpace(typeName)) return false;
        Type type;
        try { type = assembly.GetType(typeName, false); }
        catch { return false; }
        if (type == null) return false;
        try
        {
            instanceProperty = type.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public);
            isEmpedMethod = type.GetMethod("IsEMPed", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
        }
        catch { return false; }
        if (instanceProperty == null || isEmpedMethod == null) return false;
        if (isEmpedMethod.ReturnType != typeof(bool)) return false;
        if (!instanceProperty.PropertyType.IsAssignableFrom(type)) return false;
        return true;
    }

    private static bool TryReadInstance(PropertyInfo instanceProperty, out object instance)
    {
        instance = null;
        if (instanceProperty == null) return false;
        try { instance = instanceProperty.GetValue(null); }
        catch { return false; }
        return instance != null;
    }

    internal static bool TryInvoke(MethodInfo isEmpedMethod, object instance, out bool emped)
    {
        emped = false;
        if (isEmpedMethod == null || instance == null) return false;
        object result;
        try { result = isEmpedMethod.Invoke(instance, null); }
        catch { return false; }
        if (result is bool value) { emped = value; return true; }
        return false;
    }
}
