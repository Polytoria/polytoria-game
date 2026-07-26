// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Creator.Settings;
using Polytoria.Datamodel;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DataModelScript = Polytoria.Datamodel.Script;

namespace Polytoria.Creator.LSP;

/// <summary>
/// Keeps the generated world-to-file module map current while VS Code is the
/// selected editor. This is independent from built-in completion requests, so
/// moving a ModuleScript in Creator is reflected in an already-open VS Code
/// workspace without opening or saving either script in Creator.
/// </summary>
public sealed partial class VSCodeModuleMapSyncService : Node
{
	private static readonly ConditionalWeakTable<CreatorSession, VSCodeModuleMapSyncService> Services = new();

	private CreatorSession _session = null!;
	private readonly HashSet<World> _trackedWorlds = [];
	private readonly List<World> _worldOrder = [];
	private readonly Dictionary<World, int> _worldInstanceCounts = [];
	private readonly HashSet<Instance> _trackedInstances = [];
	private readonly HashSet<DataModelScript> _trackedScripts = [];
	private readonly Dictionary<DataModelScript, ScriptMapState> _scriptMapStates = [];
	private bool _wasActive;
	private bool _mapDirty = true;
	private bool _refreshQueued;

	private readonly record struct ScriptMapState(string WorldPath, string? LinkedPath);

	public VSCodeModuleMapSyncService()
	{
		Name = "VSCodeModuleMapSyncService";
	}

	public static void EnsureAttached(CreatorSession session)
	{
		if (Services.TryGetValue(session, out _))
		{
			return;
		}

		VSCodeModuleMapSyncService service = new()
		{
			_session = session
		};
		Services.Add(session, service);
		session.AddChild(service);
		service.SetProcess(true);
		service.UpdateActivation();
	}

	public override void _Process(double delta)
	{
		UpdateActivation();
		if (!_wasActive)
		{
			return;
		}

		SynchronizeTrackedWorlds();
		if (_mapDirty)
		{
			QueueRefresh();
		}
	}

	public override void _ExitTree()
	{
		UntrackAllWorlds();
		base._ExitTree();
	}

	private bool IsActive()
	{
		return CreatorSettingsService.Instance.Get<PreferredEditorEnum>(CreatorSettingKeys.CodeEditor.PreferredEditor) == PreferredEditorEnum.VSCode;
	}

	private void UpdateActivation()
	{
		bool active = IsActive();
		if (active == _wasActive)
		{
			return;
		}

		_wasActive = active;
		if (!active)
		{
			return;
		}

		VSCodeConfigService.Ensure(_session.ProjectFolderPath);
		SynchronizeTrackedWorlds();
		MarkMapDirty();
	}

	private void SynchronizeTrackedWorlds()
	{
		bool worldOrderChanged = _worldOrder.Count != _session.OpenedWorlds.Count;
		if (!worldOrderChanged)
		{
			for (int index = 0; index < _worldOrder.Count; index++)
			{
				if (!ReferenceEquals(_worldOrder[index], _session.OpenedWorlds[index]))
				{
					worldOrderChanged = true;
					break;
				}
			}
		}

		List<World> removedWorlds = [];
		foreach (World trackedWorld in _trackedWorlds)
		{
			if (!_session.OpenedWorlds.Contains(trackedWorld))
			{
				removedWorlds.Add(trackedWorld);
			}
		}

		foreach (World removedWorld in removedWorlds)
		{
			bool containedScripts = SubtreeContainsScript(removedWorld);
			UntrackSubtree(removedWorld);
			_trackedWorlds.Remove(removedWorld);
			_worldInstanceCounts.Remove(removedWorld);
			if (containedScripts)
			{
				MarkMapDirty();
			}
		}

		foreach (World world in _session.OpenedWorlds)
		{
			if (_trackedWorlds.Add(world))
			{
				_worldInstanceCounts[world] = world.InstanceCount;
				if (TrackSubtree(world))
				{
					MarkMapDirty();
				}
			}
			else if (!_worldInstanceCounts.TryGetValue(world, out int previousCount) || previousCount != world.InstanceCount)
			{
				// Some import/deserialization paths add a complete subtree without emitting
				// the ordinary ChildAdded signal. Reconcile whenever the authoritative world
				// instance count changes so VS Code does not depend on the built-in editor's
				// separate script index to discover toolbox modules.
				_worldInstanceCounts[world] = world.InstanceCount;
				if (TrackSubtree(world))
				{
					MarkMapDirty();
				}
			}
		}

		// LinkedScript may be assigned after an imported Instance enters the tree.
		// Comparing the tracked state catches missed property notifications and path
		// changes that occur while the external editor is already open.
		if (RefreshScriptMapStates())
		{
			MarkMapDirty();
		}

		if (worldOrderChanged)
		{
			_worldOrder.Clear();
			_worldOrder.AddRange(_session.OpenedWorlds);
			MarkMapDirty();
		}
	}

	private bool TrackSubtree(Instance instance)
	{
		bool addedScript = false;
		if (_trackedInstances.Add(instance))
		{
			instance.ChildAdded.Connect(OnTrackedChildAdded);
			instance.ChildRemoved.Connect(OnTrackedChildRemoved);
			instance.Renamed.Connect(OnTrackedInstanceRenamed);

			if (instance is DataModelScript script && _trackedScripts.Add(script))
			{
				script.PropertyChanged.Connect(OnTrackedScriptPropertyChanged);
				_scriptMapStates[script] = GetScriptMapState(script);
				addedScript = true;
			}
		}

		// Always recurse. A parent can already be tracked while an imported child was
		// inserted through a path that did not emit ChildAdded.
		foreach (Instance child in instance.GetChildren())
		{
			addedScript |= TrackSubtree(child);
		}

		return addedScript;
	}

	private void UntrackSubtree(Instance instance)
	{
		if (!_trackedInstances.Remove(instance))
		{
			return;
		}

		foreach (Instance child in instance.GetChildren())
		{
			UntrackSubtree(child);
		}

		instance.ChildAdded.Disconnect(OnTrackedChildAdded);
		instance.ChildRemoved.Disconnect(OnTrackedChildRemoved);
		instance.Renamed.Disconnect(OnTrackedInstanceRenamed);

		if (instance is DataModelScript script)
		{
			script.PropertyChanged.Disconnect(OnTrackedScriptPropertyChanged);
			_trackedScripts.Remove(script);
			_scriptMapStates.Remove(script);
		}
	}

	private void UntrackAllWorlds()
	{
		List<World> worlds = [.. _trackedWorlds];
		foreach (World world in worlds)
		{
			UntrackSubtree(world);
		}

		_trackedWorlds.Clear();
		_worldOrder.Clear();
		_worldInstanceCounts.Clear();
		_scriptMapStates.Clear();
	}

	private void OnTrackedChildAdded(Instance child)
	{
		if (TrackSubtree(child))
		{
			MarkMapDirty();
		}
	}

	private void OnTrackedChildRemoved(Instance child)
	{
		bool containedScripts = SubtreeContainsScript(child);
		UntrackSubtree(child);
		if (containedScripts)
		{
			MarkMapDirty();
		}
	}

	private void OnTrackedInstanceRenamed()
	{
		MarkMapDirty();
	}

	private void OnTrackedScriptPropertyChanged(string propertyName)
	{
		if (propertyName == nameof(DataModelScript.LinkedScript))
		{
			MarkMapDirty();
		}
	}

	private bool RefreshScriptMapStates()
	{
		bool changed = false;
		foreach (DataModelScript script in _trackedScripts)
		{
			ScriptMapState current = GetScriptMapState(script);
			if (!_scriptMapStates.TryGetValue(script, out ScriptMapState previous) || previous != current)
			{
				_scriptMapStates[script] = current;
				changed = true;
			}
		}
		return changed;
	}

	private static ScriptMapState GetScriptMapState(DataModelScript script)
	{
		return new(script.LuaPath, script.LinkedScript?.LinkedPath);
	}

	private void MarkMapDirty()
	{
		_mapDirty = true;
		if (_wasActive)
		{
			QueueRefresh();
		}
	}

	private void QueueRefresh()
	{
		if (_refreshQueued)
		{
			return;
		}

		_refreshQueued = true;
		Callable.From(FlushMap).CallDeferred();
	}

	private void FlushMap()
	{
		if (!_wasActive || !_mapDirty)
		{
			_refreshQueued = false;
			return;
		}

		SynchronizeTrackedWorlds();
		_mapDirty = false;
		VSCodeConfigService.Ensure(_session.ProjectFolderPath);
		LuauModuleMapService.Generate(_session, _trackedScripts);
		_refreshQueued = false;
	}

	private static bool SubtreeContainsScript(Instance instance)
	{
		if (instance is DataModelScript)
		{
			return true;
		}

		foreach (Instance child in instance.GetChildren())
		{
			if (SubtreeContainsScript(child))
			{
				return true;
			}
		}

		return false;
	}
}
