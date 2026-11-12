using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class Program
{
	private static int _failures;

	private static int Main(string[] args)
	{
		if (args.Length != 1)
		{
			Console.Error.WriteLine("usage: KillFeed.IlGuard <built GTFOKillFeed.dll>");
			return 2;
		}
		using ModuleDefinition module = ModuleDefinition.ReadModule(args[0]);
		try
		{
			CheckConfigWiring(module);
			CheckIconWiring(module);
			CheckCardWiring(module);
			CheckPreviewAndToggle(module);
			CheckNoGameWrites(module);
			CheckOccluderAndLifecycleWiring(module);
			CheckHitMarkerSubmission(module);
		}
		catch (System.Exception ex)
		{
			_failures++;
			Console.WriteLine("ILGUARD ABORT: " + ex.GetType().Name + ": " + ex.Message);
		}
		Console.WriteLine("ILGUARD failures=" + _failures);
		return (_failures == 0) ? 0 : 1;
	}

	private static void CheckConfigWiring(ModuleDefinition module)
	{
		TypeDefinition plugin = Type(module, "HawChow.GTFOKillFeed.Plugin");
		Field(plugin, "KillCardEnabled", "BepInEx.Configuration.ConfigEntry`1<System.Boolean>");
		Field(plugin, "KillIconHeadEnabled", "BepInEx.Configuration.ConfigEntry`1<System.Boolean>");
		Field(plugin, "KillIconHeadOpacity", "BepInEx.Configuration.ConfigEntry`1<System.Single>");
		Field(plugin, "KillIconDisplay", "BepInEx.Configuration.ConfigEntry`1<System.String>");
		Field(plugin, "KillIconViewMode", "BepInEx.Configuration.ConfigEntry`1<System.String>");
		MethodDefinition load = Method(plugin, "Load");
		int snapshotIndex = IndexOfCall(load, "HawChow.GTFOKillFeed.KillViewConfigFile", "Read");
		int headKeyIndex = IndexOfString(load, "HeadEnabled", exact: true);
		Require(snapshotIndex >= 0, "Plugin.Load calls KillViewConfigFile.Read");
		Require(headKeyIndex >= 0, "Plugin.Load binds KillIcon/HeadEnabled");
		Require(snapshotIndex < headKeyIndex, "legacy snapshot is read before any new-key Bind can write defaults");
		int displayIndex = BindPairIndex(load, "KillIcon", "Display");
		Require(displayIndex >= 0, "legacy KillIcon/Display is bound so it can be reloaded");
		Require(snapshotIndex < displayIndex, "snapshot is read before the legacy Display entry is created");
		Require(BindStringDefault(load, "KillIcon", "Display") == "", "KillIcon/Display default is empty (never writes a fake old value)");
		Require(BindStringDefault(load, "KillIcon", "ViewMode") == "", "KillIcon/ViewMode default is empty");
		Require(BindBool(load, "KillIcon", "HeadEnabled") == 0, "KillIcon/HeadEnabled default is false");
		Require(BindBool(load, "Card", "Enabled") == 1, "Card/Enabled default is true");
		double opacity;
		Require(TryBindDouble(load, "KillIcon", "Opacity", out opacity) && Math.Abs(opacity - 0.5) < 1e-9, "KillIcon/Opacity default is 0.5");
		int applyIndex = IndexOfCall(load, "HawChow.GTFOKillFeed.Plugin", "ApplyKillViewSwitches");
		Require(applyIndex > displayIndex, "Plugin.Load applies the migration after all binds");
		MethodDefinition helper = Method(plugin, "ApplyKillViewSwitches");
		Require(CountCalls(helper, "HawChow.GTFOKillFeed.KillViewPolicy", "ResolveSwitches") == 1, "ApplyKillViewSwitches resolves once");
		Require(CountCalls(helper, "HawChow.GTFOKillFeed.KillViewConfigApply", "Apply") == 1, "ApplyKillViewSwitches applies through the shared production helper");
		MethodDefinition poll = Method(plugin, "PollConfigHotReload");
		int reloadIndex = IndexOfCall(poll, "BepInEx.Configuration.ConfigFile", "Reload");
		int readIndex = IndexOfCallAfter(poll, "HawChow.GTFOKillFeed.KillViewConfigFile", "Read", reloadIndex);
		int hotApplyIndex = IndexOfCallAfter(poll, "HawChow.GTFOKillFeed.Plugin", "ApplyKillViewSwitches", reloadIndex);
		Require(reloadIndex >= 0, "PollConfigHotReload reloads the ConfigFile");
		Require(readIndex > reloadIndex, "hot reload re-reads the raw legacy Display after ConfigFile.Reload");
		Require(hotApplyIndex > readIndex, "hot reload re-applies the legacy/new priority after Reload");
	}

	private static void CheckIconWiring(ModuleDefinition module)
	{
		TypeDefinition hud = Type(module, "HawChow.GTFOKillFeed.KillIconHud");
		MethodDefinition draw = Method(hud, "Draw");
		MethodDefinition slot = Method(hud, "DrawSlot");
		Require(CountCalls(draw, "HawChow.GTFOKillFeed.KillViewPolicy", "HeadVisible") == 1, "KillIconHud.Draw gates on HeadVisible once");
		Require(CountCalls(draw, "HawChow.GTFOKillFeed.KillIconOccluders", "Collect") == 1, "Draw collects the candidate set once per frame");
		Require(CountCalls(slot, "HawChow.GTFOKillFeed.KillIconOccluders", "Collect") == 0, "DrawSlot does not query per icon");
		Require(CountCalls(slot, "HawChow.GTFOKillFeed.KillViewPolicy", "CompositeAlpha") == 1, "DrawSlot composes opacity exactly once");
		Require(CountCalls(slot, "HawChow.GTFOKillFeed.KillViewPolicy", "Opacity") == 0, "DrawSlot does not apply opacity a second time");
		Require(CountCalls(slot, "HawChow.GTFOKillFeed.KillIconPolicy", "Alpha") == 1, "DrawSlot evaluates lifecycle fade exactly once");
		Require(CountCalls(slot, "HawChow.GTFOKillFeed.KillViewPolicy", "ResolvePlacement") == 1, "DrawSlot resolves placement once");
		Require(CountCalls(slot, "HawChow.GTFOKillFeed.KillIconHud", "Hide") == 2, "DrawSlot hides safely for incomplete candidates and unsafe placement");
	}

	private static void CheckCardWiring(ModuleDefinition module)
	{
		TypeDefinition hud = Type(module, "HawChow.GTFOKillFeed.KillHud");
		MethodDefinition paints = Method(hud, "PaintsCard");
		Require(CountCalls(paints, "HawChow.GTFOKillFeed.KillViewPolicy", "CardVisible") == 1, "PaintsCard gates on the independent card switch");
		Require(CountCalls(paints, "HawChow.GTFOKillFeed.KillIconHud", "IsPresenting") == 0, "card visibility no longer depends on icon presence");
		MethodDefinition drawCards = Method(hud, "DrawCards");
		Require(ReferencesField(drawCards, "HawChow.GTFOKillFeed.Plugin", "KillCardEnabled"), "DrawCards reads Plugin.KillCardEnabled");
	}

	private static void CheckPreviewAndToggle(ModuleDefinition module)
	{
		TypeDefinition tracker = Type(module, "HawChow.GTFOKillFeed.KillTracker");
		MethodDefinition preview = Method(tracker, "AddPreview");
		Require(ReferencesField(preview, "HawChow.GTFOKillFeed.Plugin", "KillCardEnabled"), "F9 card preview follows the card switch");
		Require(ReferencesField(preview, "HawChow.GTFOKillFeed.Plugin", "KillIconHeadEnabled"), "F9 icon preview follows the head switch");
		Require(CountCalls(preview, "HawChow.GTFOKillFeed.KillIconHud", "AddPreview") == 1, "F9 still queues icons when head is enabled");
		TypeDefinition hud = Type(module, "HawChow.GTFOKillFeed.KillHud");
		MethodDefinition update = Method(hud, "Update");
		Require(ReferencesField(update, "HawChow.GTFOKillFeed.Plugin", "ToggleKey"), "F7 still reads Plugin.ToggleKey");
		Require(ReferencesField(update, "HawChow.GTFOKillFeed.Plugin", "PreviewKey"), "F9 still reads Plugin.PreviewKey");
		MethodDefinition onGui = Method(hud, "OnGUI");
		Require(ReferencesField(onGui, "HawChow.GTFOKillFeed.Plugin", "Enabled"), "OnGUI still honors General/Enabled");
	}

	private static void CheckOccluderAndLifecycleWiring(ModuleDefinition module)
	{
		TypeDefinition occluders = Type(module, "HawChow.GTFOKillFeed.KillIconOccluders");
		MethodDefinition seed = Method(occluders, "Seed");
		MethodDefinition collect = Method(occluders, "Collect");
		Require(CountCalls(seed, "UnityEngine.Object", "FindObjectsOfType") == 1, "registry is seeded by one bounded one-time scene scan");
		Require(CountCalls(collect, "UnityEngine.Object", "FindObjectsOfType") == 0, "per-frame collection never scans the scene");
		Require(CountCalls(collect, "UnityEngine.Physics", "OverlapSphereNonAlloc") == 0, "the old depth-limited sphere query is gone");
		Require(CountCalls(collect, "HawChow.GTFOKillFeed.KillViewPolicy", "ShouldConsiderCandidate") == 1, "collector uses the shared enemy/bounds filter");
		Require(CountCalls(collect, "HawChow.GTFOKillFeed.KillViewPolicy", "TryAppendCandidate") == 1, "collector uses the shared bounded append/saturation check");
		TypeDefinition spawn = Type(module, "HawChow.GTFOKillFeed.EnemySpawnPatch");
		Require(CountCallsDeep(spawn, "HawChow.GTFOKillFeed.KillIconOccluders", "Register") == 1, "spawn registers a candidate");
		TypeDefinition despawn = Type(module, "HawChow.GTFOKillFeed.EnemyDespawnPatch");
		Require(CountCallsDeep(despawn, "HawChow.GTFOKillFeed.KillIconOccluders", "Unregister") == 1, "despawn removes a candidate");
		TypeDefinition tracker = Type(module, "HawChow.GTFOKillFeed.KillTracker");
		Require(CountCalls(Method(tracker, "Reset"), "HawChow.GTFOKillFeed.KillIconOccluders", "Reset") == 1, "tracker reset clears the registry");
		Require(CountCalls(Method(tracker, "ObserveState"), "HawChow.GTFOKillFeed.KillIconOccluders", "EnterLevel") == 1, "level enter reseeds the registry");
		Require(CountCalls(Method(occluders, "EnterLevel"), "HawChow.GTFOKillFeed.KillIconOccluders", "Reset") == 1, "level enter clears the recovery state through Reset");
		Field(occluders, "Recovery", "HawChow.GTFOKillFeed.KillOccluderRecovery");
		Require(HasField(occluders, "Registry"), "the enemy registry is the shared bounded implementation");
		Require(!HasField(occluders, "_complete"), "the sticky completeness field is gone");
		MethodDefinition rebuild = Method(occluders, "Rebuild");
		Require(CountCalls(rebuild, "UnityEngine.Object", "FindObjectsOfType") == 1, "the bounded rebuild owns the only rescan");
		Require(CountCallsDeep(occluders, "UnityEngine.Object", "FindObjectsOfType") == 2, "scene scans are the one-time seed plus the rate-limited rebuild");
		int dueIndex = IndexOfCall(collect, "HawChow.GTFOKillFeed.KillOccluderRecovery", "RescanDue");
		int rebuildIndex = IndexOfCall(collect, "HawChow.GTFOKillFeed.KillIconOccluders", "Rebuild");
		Require(dueIndex >= 0, "collect asks the recovery limiter before rebuilding");
		Require(rebuildIndex > dueIndex, "collect rebuilds only when the limiter says it is due");
		Require(CountCalls(collect, "HawChow.GTFOKillFeed.KillOccluderRecovery", "BeginFrame") == 1, "collect resets the frame-local flag once per frame");
		Require(CountCalls(collect, "HawChow.GTFOKillFeed.KillOccluderRecovery", "NoteFrameIncomplete") == 4, "every frame-local fault hides only its own frame");
		Require(CountCalls(rebuild, "HawChow.GTFOKillFeed.KillOccluderRecovery", "NoteRegistryRebuilt") == 1, "only a successful bounded rebuild clears the incomplete registry");
		Require(CountCalls(rebuild, "HawChow.GTFOKillFeed.KillOccluderRecovery", "NoteRescanFailed") == 3, "every failed rebuild stays fail-closed and reschedules");
		Require(CountCalls(rebuild, "HawChow.GTFOKillFeed.KillOccluderRecovery", "NoteRescanAttempt") == 1, "bounded rebuilds are counted");
		Require(CountCalls(seed, "HawChow.GTFOKillFeed.KillOccluderRecovery", "NoteRegistryIncomplete") == 3, "a failed or overflowing seed marks the registry incomplete");
		TypeDefinition recovery = Type(module, "HawChow.GTFOKillFeed.KillOccluderRecovery");
		Require(CountCallsDeep(recovery, "UnityEngine.Object", "FindObjectsOfType") == 0, "the recovery policy never scans the scene");
		Require(CountCallsDeep(recovery, "UnityEngine.Object", "FindObjectsOfTypeAll") == 0, "the recovery policy never scans all objects");
	}

	private static void CheckHitMarkerSubmission(ModuleDefinition module)
	{
		TypeDefinition policy = Type(module, "HawChow.GTFOKillFeed.HitMarkerPolicy");
		MethodDefinition accepts = Method(policy, "AcceptsSubmission");
		Require(accepts != null && accepts.ReturnType.FullName == "System.Boolean", "HitMarkerPolicy.AcceptsSubmission returns bool");
		Require(accepts != null && accepts.Parameters.Count == 2 && accepts.Parameters[0].ParameterType.FullName == "System.Boolean" && accepts.Parameters[1].ParameterType.FullName == "HawChow.GTFOKillFeed.HitMarkerKind", "AcceptsSubmission takes (bool channelEnabled, HitMarkerKind kind)");
		bool channelGate = false;
		bool noneComparison = false;
		int trueReturns = 0;
		int falseReturns = 0;
		if (accepts != null && accepts.HasBody)
		{
			IList<Instruction> body = accepts.Body.Instructions;
			for (int i = 0; i < body.Count; i++)
			{
				Instruction instruction = body[i];
				if (instruction.OpCode == OpCodes.Ldc_I4_1 && i + 1 < body.Count && body[i + 1].OpCode == OpCodes.Ret)
				{
					trueReturns++;
				}
				if (i + 1 < body.Count && body[i + 1].OpCode == OpCodes.Ret && (instruction.OpCode == OpCodes.Cgt_Un || instruction.OpCode == OpCodes.Ceq || instruction.OpCode == OpCodes.Bne_Un))
				{
					trueReturns++;
				}
				if (instruction.OpCode == OpCodes.Ldc_I4_0 && i + 1 < body.Count && body[i + 1].OpCode == OpCodes.Ret)
				{
					falseReturns++;
				}
				if ((instruction.OpCode == OpCodes.Brfalse || instruction.OpCode == OpCodes.Brfalse_S) && instruction.Operand is Instruction target && target.OpCode == OpCodes.Ldc_I4_0 && i > 0 && body[i - 1].OpCode == OpCodes.Ldarg_0)
				{
					channelGate = true;
				}
				if (instruction.OpCode == OpCodes.Ldc_I4_0 && i > 0 && i + 1 < body.Count && body[i - 1].OpCode == OpCodes.Ldarg_1 && (body[i + 1].OpCode == OpCodes.Cgt_Un || body[i + 1].OpCode == OpCodes.Ceq || body[i + 1].OpCode == OpCodes.Bne_Un))
				{
					noneComparison = true;
				}
			}
		}
		Require(channelGate, "AcceptsSubmission short-circuits a disabled channel to the false return");
		Require(noneComparison, "AcceptsSubmission compares the kind parameter against HitMarkerKind.None before accepting");
		Require(trueReturns == 1 && falseReturns == 1, "AcceptsSubmission has exactly one accepting and one rejecting return");
		TypeDefinition hud = Type(module, "HawChow.GTFOKillFeed.HitMarkerHud");
		MethodDefinition submit = Method(hud, "Submit");
		int gate = IndexOfCall(submit, "HawChow.GTFOKillFeed.HitMarkerPolicy", "AcceptsSubmission");
		Require(gate >= 0, "HitMarkerHud.Submit calls HitMarkerPolicy.AcceptsSubmission");
		Require(CountCalls(submit, "HawChow.GTFOKillFeed.HitMarkerPolicy", "AcceptsSubmission") == 1, "Submit calls the submission gate exactly once");
		int project = IndexOfCall(submit, "HawChow.GTFOKillFeed.HitMarkerHud", "TryProject");
		int resolve = IndexOfCall(submit, "HawChow.GTFOKillFeed.HitMarkerHud", "Resolve");
		Require(project > gate, "Submit projects only after the None/channel gate");
		Require(resolve > gate, "Submit resolves the queue only after the None/channel gate");
		if (submit != null && gate >= 0 && gate + 3 < submit.Body.Instructions.Count)
		{
			Instruction branch = submit.Body.Instructions[gate + 1];
			Instruction rejected = submit.Body.Instructions[gate + 2];
			Instruction ret = submit.Body.Instructions[gate + 3];
			bool conditional = branch.OpCode == OpCodes.Brtrue || branch.OpCode == OpCodes.Brtrue_S;
			Require(conditional, "Submit branches on the accepted flag");
			Require(rejected.OpCode == OpCodes.Ldc_I4_0 && ret.OpCode == OpCodes.Ret, "Submit's rejected path returns false immediately (ldc.i4.0; ret) before any work");
			int targetIndex = (conditional && branch.Operand is Instruction destination) ? submit.Body.Instructions.IndexOf(destination) : -1;
			Require(targetIndex > gate, "Submit continues past the gate only when it accepts the submission");
		}
		else
		{
			Require(false, "Submit has instructions after the submission gate");
		}
	}

	private static void CheckNoGameWrites(ModuleDefinition module)
	{
		string[] types = new string[3] { "HawChow.GTFOKillFeed.KillViewPolicy", "HawChow.GTFOKillFeed.KillIconOccluders", "HawChow.GTFOKillFeed.KillOccluderRecovery" };
		for (int i = 0; i < types.Length; i++)
		{
			TypeDefinition type = Type(module, types[i]);
			Require(type.CustomAttributes.Count == 0, types[i] + " has no Harmony/attributes");
			foreach (MethodDefinition method in type.Methods)
			{
				foreach (Instruction instruction in method.Body.Instructions)
				{
					if (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt)
					{
						MethodReference target = instruction.Operand as MethodReference;
						if (target == null)
						{
							continue;
						}
						Require(!target.Name.StartsWith("set_", StringComparison.Ordinal), types[i] + "." + method.Name + " does not call " + target.FullName);
						Require(!ContainsAny(target.Name, "Send", "Rpc", "Write", "Network"), types[i] + "." + method.Name + " does not call network writer " + target.Name);
					}
					if (instruction.OpCode == OpCodes.Stfld || instruction.OpCode == OpCodes.Stsfld)
					{
						FieldReference field = instruction.Operand as FieldReference;
						if (field != null)
						{
							Require(field.DeclaringType.FullName.StartsWith("HawChow.GTFOKillFeed.", StringComparison.Ordinal), types[i] + "." + method.Name + " does not write " + field.FullName);
						}
					}
				}
			}
		}
		TypeDefinition occluders = Type(module, "HawChow.GTFOKillFeed.KillIconOccluders");
		MethodDefinition collect = Method(occluders, "Collect");
		Require(CountCalls(collect, "UnityEngine.Physics", "OverlapSphereNonAlloc") == 0, "occluders never use the depth-limited sphere query");
		Require(CountCalls(collect, "UnityEngine.Physics", "OverlapSphere") == 0, "occluders never allocate a full OverlapSphere result");
		Require(CountCalls(collect, "UnityEngine.Object", "FindObjectsOfType") == 0, "occluders never scan the whole scene");
		Require(CountCalls(collect, "UnityEngine.Object", "FindObjectsOfTypeAll") == 0, "occluders never scan all objects");
	}

	private static bool HasField(TypeDefinition type, string name)
	{
		return type != null && type.Fields.Any((FieldDefinition f) => f.Name == name);
	}

	private static TypeDefinition Type(ModuleDefinition module, string fullName)
	{
		TypeDefinition type = module.GetType(fullName);
		Require(type != null, "type exists: " + fullName);
		return type;
	}

	private static FieldDefinition Field(TypeDefinition type, string name, string fieldType)
	{
		FieldDefinition field = type.Fields.FirstOrDefault((FieldDefinition f) => f.Name == name);
		Require(field != null, type.FullName + "." + name + " exists");
		if (field != null)
		{
			Require(field.FieldType.FullName == fieldType, type.FullName + "." + name + " type=" + field.FieldType.FullName);
		}
		return field;
	}

	private static MethodDefinition Method(TypeDefinition type, string name)
	{
		MethodDefinition method = type.Methods.FirstOrDefault((MethodDefinition m) => m.Name == name);
		Require(method != null, type.FullName + "." + name + " exists");
		return method;
	}

	private static int IndexOfCall(MethodDefinition method, string type, string name)
	{
		if (method == null)
		{
			return -1;
		}
		for (int i = 0; i < method.Body.Instructions.Count; i++)
		{
			Instruction instruction = method.Body.Instructions[i];
			if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
			{
				continue;
			}
			MethodReference target = instruction.Operand as MethodReference;
			if (target != null && target.Name == name && target.DeclaringType.FullName == type)
			{
				return i;
			}
		}
		return -1;
	}

	private static int IndexOfConfigEntrySetter(MethodDefinition method, string name, int after)
	{
		if (method == null)
		{
			return -1;
		}
		for (int i = after + 1; i < method.Body.Instructions.Count; i++)
		{
			Instruction instruction = method.Body.Instructions[i];
			if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
			{
				continue;
			}
			MethodReference target = instruction.Operand as MethodReference;
			if (target != null && target.Name == name && target.DeclaringType.FullName.StartsWith("BepInEx.Configuration.ConfigEntry", StringComparison.Ordinal))
			{
				return i;
			}
		}
		return -1;
	}

	private static int IndexOfCallAfter(MethodDefinition method, string type, string name, int after)
	{
		if (method == null)
		{
			return -1;
		}
		for (int i = after + 1; i < method.Body.Instructions.Count; i++)
		{
			Instruction instruction = method.Body.Instructions[i];
			if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
			{
				continue;
			}
			MethodReference target = instruction.Operand as MethodReference;
			if (target != null && target.Name == name && target.DeclaringType.FullName == type)
			{
				return i;
			}
		}
		return -1;
	}

	private static int CountCallsDeep(TypeDefinition type, string targetType, string targetName)
	{
		int count = 0;
		foreach (MethodDefinition method in type.Methods)
		{
			count += CountCalls(method, targetType, targetName);
		}
		foreach (TypeDefinition nested in type.NestedTypes)
		{
			count += CountCallsDeep(nested, targetType, targetName);
		}
		return count;
	}

	private static int CountCalls(MethodDefinition method, string type, string name)
	{
		if (method == null)
		{
			return -1;
		}
		int count = 0;
		foreach (Instruction instruction in method.Body.Instructions)
		{
			if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
			{
				continue;
			}
			MethodReference target = instruction.Operand as MethodReference;
			if (target != null && target.Name == name && target.DeclaringType.FullName == type)
			{
				count++;
			}
		}
		return count;
	}

	private static int IndexOfString(MethodDefinition method, string text, bool exact)
	{
		if (method == null)
		{
			return -1;
		}
		for (int i = 0; i < method.Body.Instructions.Count; i++)
		{
			Instruction instruction = method.Body.Instructions[i];
			if (instruction.OpCode == OpCodes.Ldstr && instruction.Operand is string value && (exact ? value == text : value.Contains(text)))
			{
				return i;
			}
		}
		return -1;
	}

	private static bool ReferencesField(MethodDefinition method, string type, string name)
	{
		if (method == null)
		{
			return false;
		}
		foreach (Instruction instruction in method.Body.Instructions)
		{
			FieldReference field = instruction.Operand as FieldReference;
			if (field != null && field.Name == name && field.DeclaringType.FullName == type)
			{
				return true;
			}
		}
		return false;
	}

	private static int BindPairIndex(MethodDefinition method, string section, string key)
	{
		if (method == null)
		{
			return -1;
		}
		IList<Instruction> instructions = method.Body.Instructions;
		for (int i = 0; i + 1 < instructions.Count; i++)
		{
			if (instructions[i].OpCode == OpCodes.Ldstr && (string)instructions[i].Operand == section && instructions[i + 1].OpCode == OpCodes.Ldstr && (string)instructions[i + 1].Operand == key)
			{
				for (int j = i + 2; j < instructions.Count && j <= i + 8; j++)
				{
					if (instructions[j].OpCode != OpCodes.Call && instructions[j].OpCode != OpCodes.Callvirt)
					{
						continue;
					}
					MethodReference target = instructions[j].Operand as MethodReference;
					if (target != null && target.Name == "Bind" && target.DeclaringType.FullName == "BepInEx.Configuration.ConfigFile")
					{
						return i;
					}
				}
			}
		}
		return -1;
	}

	private static string BindStringDefault(MethodDefinition method, string section, string key)
	{
		if (method == null)
		{
			return null;
		}
		IList<Instruction> instructions = method.Body.Instructions;
		for (int i = 0; i + 4 < instructions.Count; i++)
		{
			if (instructions[i].OpCode == OpCodes.Ldstr && (string)instructions[i].Operand == section && instructions[i + 1].OpCode == OpCodes.Ldstr && (string)instructions[i + 1].Operand == key && instructions[i + 2].OpCode == OpCodes.Ldstr)
			{
				for (int j = i + 3; j < instructions.Count && j <= i + 8; j++)
				{
					if (instructions[j].OpCode != OpCodes.Call && instructions[j].OpCode != OpCodes.Callvirt)
					{
						continue;
					}
					MethodReference target = instructions[j].Operand as MethodReference;
					if (target != null && target.Name == "Bind" && target.DeclaringType.FullName == "BepInEx.Configuration.ConfigFile")
					{
						return (string)instructions[i + 2].Operand;
					}
				}
			}
		}
		return null;
	}

	private static int BindBool(MethodDefinition method, string section, string key)
	{
		if (method == null)
		{
			return -1;
		}
		IList<Instruction> instructions = method.Body.Instructions;
		for (int i = 0; i + 3 < instructions.Count; i++)
		{
			if (instructions[i].OpCode == OpCodes.Ldstr && (string)instructions[i].Operand == section && instructions[i + 1].OpCode == OpCodes.Ldstr && (string)instructions[i + 1].Operand == key)
			{
				Instruction value = instructions[i + 2];
				if (value.OpCode == OpCodes.Ldc_I4_0)
				{
					return 0;
				}
				if (value.OpCode == OpCodes.Ldc_I4_1)
				{
					return 1;
				}
			}
		}
		return -1;
	}

	private static bool TryBindDouble(MethodDefinition method, string section, string key, out double value)
	{
		value = double.NaN;
		if (method == null)
		{
			return false;
		}
		IList<Instruction> instructions = method.Body.Instructions;
		for (int i = 0; i + 3 < instructions.Count; i++)
		{
			if (instructions[i].OpCode == OpCodes.Ldstr && (string)instructions[i].Operand == section && instructions[i + 1].OpCode == OpCodes.Ldstr && (string)instructions[i + 1].Operand == key)
			{
				Instruction instruction = instructions[i + 2];
				if (instruction.OpCode == OpCodes.Ldc_R8)
				{
					value = (double)instruction.Operand;
					return true;
				}
				if (instruction.OpCode == OpCodes.Ldc_R4)
				{
					value = (float)instruction.Operand;
					return true;
				}
			}
		}
		return false;
	}

	private static bool ContainsAny(string text, params string[] needles)
	{
		for (int i = 0; i < needles.Length; i++)
		{
			if (text.Contains(needles[i]))
			{
				return true;
			}
		}
		return false;
	}

	private static void Require(bool condition, string message)
	{
		if (condition)
		{
			return;
		}
		_failures++;
		Console.WriteLine("ILGUARD FAIL: " + message);
	}
}
