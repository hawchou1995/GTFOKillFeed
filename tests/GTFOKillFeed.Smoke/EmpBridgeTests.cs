using System;
using System.Reflection;
using HawChow.GTFOKillFeed;

// Proves the optional EOSExt_EMP bridge against a real (fake) handler type compiled into this test
// assembly: the same reflection path the plugin uses, with no compile-time dependency on the mod.
// Namespace + type name mirror the real `EOSExt.EMP.Impl.Handlers.EMPPlayerHudHandler`, and
// EmpBridge.TryBind is the exact method the production probe calls.
internal static class EmpBridgeTests
{
    internal static void Run(Action<bool, string> check)
    {
        Assembly self = typeof(EmpBridgeTests).Assembly;

        check(EmpBridge.TryBind(self, "EOSExt.EMP.Impl.Handlers.EMPPlayerHudHandler",
                out PropertyInfo instance, out MethodInfo isEmped),
            "emp bridge: the real handler shape resolves through reflection");
        check(instance != null && instance.PropertyType == typeof(EOSExt.EMP.Impl.Handlers.EMPPlayerHudHandler),
            "emp bridge: Instance resolves to the handler type");
        check(isEmped != null && isEmped.ReturnType == typeof(bool) && isEmped.GetParameters().Length == 0,
            "emp bridge: IsEMPed() resolves as a parameterless bool method");

        check(!EmpBridge.TryBind(self, "EOSExt.EMP.Impl.Handlers.MissingHandler", out _, out _),
            "emp bridge: a renamed type fails closed");
        check(!EmpBridge.TryBind(null, "EOSExt.EMP.Impl.Handlers.EMPPlayerHudHandler", out _, out _),
            "emp bridge: a null assembly fails closed");
        check(!EmpBridge.TryBind(self, "", out _, out _), "emp bridge: an empty type name fails closed");

        var handler = new EOSExt.EMP.Impl.Handlers.EMPPlayerHudHandler { Emped = true };
        check(EmpBridge.TryInvoke(isEmped, handler, out bool active) && active,
            "emp bridge: IsEMPed()==true is reported");

        handler.Emped = false;
        check(EmpBridge.TryInvoke(isEmped, handler, out bool idle) && !idle,
            "emp bridge: IsEMPed()==false is reported");

        handler.ThrowOnIsEmped = true;
        check(!EmpBridge.TryInvoke(isEmped, handler, out bool failed) && !failed,
            "emp bridge: a throwing IsEMPed() degrades to 'no EMP' without escaping");
        handler.ThrowOnIsEmped = false;

        check(!EmpBridge.TryInvoke(isEmped, null, out _),
            "emp bridge: a null handler instance degrades to 'no EMP'");
        check(!EmpBridge.TryInvoke(null, handler, out _),
            "emp bridge: a null method degrades to 'no EMP'");

        // ---- production poll path: the optional mod is NOT installed here --------------------
        EmpBridge.Reset(0f);
        check(!EmpBridge.Available && !EmpBridge.IsEmped, "emp bridge: reset clears the binding and state");
        EmpBridge.Poll(0f, false);
        check(EmpBridge.Failures == 0 && !EmpBridge.Available,
            "emp bridge: HideDuringEMP=false never touches reflection (no probe, no retry)");
        EmpBridge.Poll(0f, true);
        check(!EmpBridge.Available && !EmpBridge.IsEmped && EmpBridge.Failures == 1,
            "emp bridge: a missing Inas07.EOSExt.EMP assembly fails open and counts one attempt");
        EmpBridge.Poll(0.1f, true);
        check(EmpBridge.Failures == 1, "emp bridge: a failed probe is not repeated inside its backoff");
        EmpBridge.Poll(0.55f, true);
        check(EmpBridge.Failures == 2, "emp bridge: the failed probe retries after the 0.25 s backoff");

        for (int i = 0; i < 12; i++) EmpBridge.Poll(10f + i * 10f, true);
        check(EmpBridge.Failures >= 3 && EmpBridge.Failures <= 14 && !EmpBridge.Available,
            "emp bridge: repeated failures stay bounded and never bind a foreign assembly");
        check(!EmpBridge.IsEmped, "emp bridge: a failed chain never reports an active EMP");

        EmpBridge.Reset(5f);
        check(EmpBridge.Failures == 0 && !EmpBridge.Available,
            "emp bridge: a level change re-arms the probe from a clean state");
    }
}

namespace EOSExt.EMP.Impl.Handlers
{
    // Test double that mirrors the real optional handler's public surface.
    internal sealed class EMPPlayerHudHandler
    {
        public static EMPPlayerHudHandler Instance { get; set; }

        internal bool Emped;
        internal bool ThrowOnIsEmped;

        public bool IsEMPed()
        {
            if (ThrowOnIsEmped) throw new InvalidOperationException("native handler disposed");
            return Emped;
        }
    }
}
