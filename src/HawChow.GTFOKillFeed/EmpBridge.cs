using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace HawChow.GTFOKillFeed;

internal static class EmpBridge
{
	internal const string AssemblyName = "Inas07.EOSExt.EMP";

	internal const string HandlerTypeName = "EOSExt.EMP.Impl.Handlers.EMPPlayerHudHandler";

	private static PropertyInfo _instanceProperty;

	private static MethodInfo _isEmpedMethod;

	private static object _instance;

	private static int _failures;

	private static float _nextProbeAt = -1f / 0f;

	internal static Action<string> Diagnostics;

	private static bool _reportedBind;

	private static int _reportedFailures;

	internal static bool Available => _isEmpedMethod != (MethodInfo)null;

	internal static bool IsEmped
	{
		get;
		private set;
	}

	internal static int Failures => _failures;

	internal static bool Bound
	{
		get
		{
			if (_isEmpedMethod != (MethodInfo)null)
			{
				return _instanceProperty != (PropertyInfo)null;
			}
			return false;
		}
	}

	internal static bool HasInstance => _instance != null;

	private static void Report(string message, bool failure)
	{
		if (failure)
		{
			if (_reportedFailures >= 3)
			{
				return;
			}
			_reportedFailures++;
		}
		try
		{
			Diagnostics?.Invoke(message);
		}
		catch
		{
		}
	}

	internal static void Reset(float now)
	{
		_instanceProperty = null;
		_isEmpedMethod = null;
		_instance = null;
		_failures = 0;
		IsEmped = false;
		_nextProbeAt = (KillPolicy.IsFinite(now) ? now : 0f);
	}

	internal static void Poll(float now, bool hideDuringEmp)
	{
		if (!TeammateMarkPolicy.ShouldProbeEmp(hideDuringEmp))
		{
			IsEmped = false;
			_nextProbeAt = -1f / 0f;
		}
		else
		{
			if (!TeammateMarkPolicy.IsProbeDue(now, _nextProbeAt))
			{
				return;
			}
			if (_isEmpedMethod == (MethodInfo)null || _instanceProperty == (PropertyInfo)null)
			{
				if (!TryBindFromAppDomain(out _instanceProperty, out _isEmpedMethod))
				{
					Defer(now, bound: false, "Inas07.EOSExt.EMP not loaded or handler shape missing");
					return;
				}
				if (!_reportedBind)
				{
					_reportedBind = true;
					Report("emp bridge bound: EMPPlayerHudHandler.Instance/IsEMPed resolved", failure: false);
				}
			}
			if (_instance == null && !TryReadInstance(_instanceProperty, out _instance))
			{
				Defer(now, bound: true, "EMPPlayerHudHandler.Instance is null");
				return;
			}
			if (!TryInvoke(_isEmpedMethod, _instance, out var emped))
			{
				Defer(now, bound: true, "IsEMPed() threw or returned a non-bool");
				return;
			}
			if (_failures > 0)
			{
				Report("emp bridge recovered after " + _failures + " failed probe(s)", failure: false);
				_reportedFailures = 0;
			}
			IsEmped = emped;
			_failures = 0;
			_nextProbeAt = TeammateMarkPolicy.NextProbeAt(now, bound: true);
		}
	}

	private static void Defer(float now, bool bound, string reason)
	{
		IsEmped = false;
		_failures++;
		if (!bound)
		{
			_failures = Math.Max(_failures, 1);
		}
		_nextProbeAt = (KillPolicy.IsFinite(now) ? now : 0f) + TeammateMarkPolicy.RetryDelay(_failures);
		Report("emp probe deferred: " + reason + ";failures=" + _failures, failure: true);
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
		try
		{
			assemblies = AppDomain.CurrentDomain.GetAssemblies();
		}
		catch
		{
			return false;
		}
		foreach (Assembly val in assemblies)
		{
			if (!(val == (Assembly)null))
			{
				string name;
				try
				{
					name = val.GetName().Name;
				}
				catch
				{
					continue;
				}
				if (string.Equals(name, "Inas07.EOSExt.EMP", (StringComparison)4) && TryBind(val, "EOSExt.EMP.Impl.Handlers.EMPPlayerHudHandler", out instanceProperty, out isEmpedMethod))
				{
					return true;
				}
			}
		}
		return false;
	}

	internal static bool TryBind(Assembly assembly, string typeName, out PropertyInfo instanceProperty, out MethodInfo isEmpedMethod)
	{
		instanceProperty = null;
		isEmpedMethod = null;
		if (assembly == (Assembly)null || string.IsNullOrWhiteSpace(typeName))
		{
			return false;
		}
		System.Type type;
		try
		{
			type = assembly.GetType(typeName, false);
		}
		catch
		{
			return false;
		}
		if (type == (System.Type)null)
		{
			return false;
		}
		try
		{
			instanceProperty = type.GetProperty("Instance", (BindingFlags)24);
			isEmpedMethod = type.GetMethod("IsEMPed", (BindingFlags)20, (Binder)null, System.Type.EmptyTypes, (ParameterModifier[])null);
		}
		catch
		{
			return false;
		}
		if (instanceProperty == (PropertyInfo)null || isEmpedMethod == (MethodInfo)null)
		{
			return false;
		}
		if (isEmpedMethod.ReturnType != typeof(bool))
		{
			return false;
		}
		if (!instanceProperty.PropertyType.IsAssignableFrom(type))
		{
			return false;
		}
		return true;
	}

	private static bool TryReadInstance(PropertyInfo instanceProperty, out object instance)
	{
		instance = null;
		if (instanceProperty == (PropertyInfo)null)
		{
			return false;
		}
		try
		{
			instance = instanceProperty.GetValue((object)null);
		}
		catch
		{
			return false;
		}
		return instance != null;
	}

	internal static bool TryInvoke(MethodInfo isEmpedMethod, object instance, out bool emped)
	{
		emped = false;
		if (isEmpedMethod == (MethodInfo)null || instance == null)
		{
			return false;
		}
		object obj;
		try
		{
			obj = ((MethodBase)isEmpedMethod).Invoke(instance, (object[])null);
		}
		catch
		{
			return false;
		}
		if (obj is bool flag)
		{
			emped = flag;
			return true;
		}
		return false;
	}
}
