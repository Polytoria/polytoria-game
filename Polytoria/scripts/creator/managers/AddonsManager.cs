// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using Polytoria.Formats;
using Polytoria.Scripting;
using Polytoria.Shared;
using Polytoria.Datamodel.Creator;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using static Polytoria.Creator.Managers.AddonsManager;
using static Polytoria.Datamodel.Creator.CreatorAddons;
using Script = Polytoria.Datamodel.Script;

namespace Polytoria.Creator.Managers;

public sealed partial class AddonsManager : Node
{
	private const ScriptPermissionFlags AddonDefaultPermissionFlags =
		ScriptPermissionFlags.CreatorAccess | ScriptPermissionFlags.ContextAccess;
	public static readonly string UserAddonFolder = Path.Combine(CreatorService.DocumentsRootPath, "Addons");
	public static readonly string AddonPermissionsFile = Path.Combine(CreatorService.DocumentsRootPath, "addon_perms");

	private static readonly Dictionary<string, List<AddonSession>> _pathToAddons = [];
	private static readonly Dictionary<Script, string> _scriptToPath = [];
	private static readonly HashSet<World> _registeredRoots = [];

	private static AddonPermissionPair _permissions = [];

	static AddonsManager()
	{
		if (!Directory.Exists(UserAddonFolder))
		{
			Directory.CreateDirectory(UserAddonFolder);
		}
		LoadPermissionsData();
	}

	public static void RegisterRoot(World root)
	{
		_registeredRoots.Add(root);
	}

	public static void UnregisterRoot(World root)
	{
		_registeredRoots.Remove(root);

		// Clean up addon sessions for this root
		foreach (var addonList in _pathToAddons.Values.ToList())
		{
			var sessionsToRemove = addonList.Where(s => s.Root == root).ToList();
			foreach (var session in sessionsToRemove)
			{
				CleanupAddonSession(session);
				addonList.Remove(session);
			}
		}
	}

	public static async void RunAddons(World root)
	{
		RegisterRoot(root);

		foreach (string f in Directory.GetFiles(UserAddonFolder))
		{
			try
			{
				await RunAddon(f, root, Path.GetFileName(f));
			}
			catch (Exception ex)
			{
				PT.PrintErr(ex);
			}
		}
	}

	public static async Task InstallAddonFromScript(Script s)
	{
		string addonName = s.Name;
		string addonFileName = addonName + ".ptaddon";
		string addonPath = Path.GetFullPath(Path.Join(UserAddonFolder, addonFileName));
		PT.Print("Installing addon ", addonName, " to ", addonPath);
		await PackedFormat.PackAddonToFile(s, addonPath, new() { Name = s.Name });
		PT.Print("Addon Installed!");

		// Run addon for all registered roots
		foreach (var root in _registeredRoots.ToList())
		{
			await RunAddon(addonPath, root, addonFileName);
		}
	}

	public static async Task RunAddon(string addonPath, World root, string shortPath)
	{
		PackedFormat.AddonData data = PackedFormat.LoadAddonFile(root, addonPath);

		AddonSession session = new()
		{
			Data = data,
			Path = shortPath,
			Root = root
		};

		if (!_pathToAddons.TryGetValue(shortPath, out List<AddonSession>? value))
		{
			value = [];
			_pathToAddons[shortPath] = value;
		}

		value.Add(session);

		ScriptPermissionFlags permFlags = AddonDefaultPermissionFlags;
		if (_permissions.TryGetValue(shortPath, out ScriptPermissionFlags existing))
		{
			permFlags = existing;
		}

		List<Instance> all = [.. data.EntryScript.GetDescendants()];

		// Run first
		all.Insert(0, data.EntryScript);

		foreach (Instance item in all)
		{
			if (item is Script s && (item is ServerScript || item is ClientScript))
			{
				session.Scripts.Add(s);
				s.PermissionFlags = permFlags;
				RunAddonScript(s, shortPath);
			}
		}
	}

	public static void RunAddonScript(Script s, string shortPath)
	{
		_scriptToPath[s] = shortPath;

		void deleted()
		{
			s.Deleted -= deleted;
			_scriptToPath.Remove(s);
		}

		s.Deleted += deleted;

		s.PermissionFlags = AddonDefaultPermissionFlags;
		s.Run();
	}

	public static void GiveScriptPermission(Script s, AddonPermissionEnum perm)
	{
		switch (perm)
		{
			case AddonPermissionEnum.IORead:
				{
					s.PermissionFlags |= ScriptPermissionFlags.IORead;
					break;
				}
			case AddonPermissionEnum.IOWrite:
				{
					s.PermissionFlags |= ScriptPermissionFlags.IOWrite;
					break;
				}
		}
	}

	public static void RevokeScriptPermission(Script s, AddonPermissionEnum perm)
	{
		switch (perm)
		{
			case AddonPermissionEnum.IORead:
				{
					s.PermissionFlags &= ~ScriptPermissionFlags.IORead;
					break;
				}
			case AddonPermissionEnum.IOWrite:
				{
					s.PermissionFlags &= ~ScriptPermissionFlags.IOWrite;
					break;
				}
		}
	}

	public static void SetAddonPermission(Script s, AddonPermissionEnum perm, bool enabled)
	{
		SetAddonPermissions(s, [perm], enabled);
	}

	public static void SetAddonPermissions(Script s, AddonPermissionEnum[] perms, bool enabled)
	{
		if (_scriptToPath.TryGetValue(s, out string? path))
		{
			if (_pathToAddons.TryGetValue(path, out List<AddonSession>? sessions))
			{
				// Give permission across sessions
				foreach (var session in sessions)
				{
					foreach (var script in session.Scripts)
					{
						foreach (var perm in perms)
						{
							if (enabled)
							{
								GiveScriptPermission(script, perm);
							}
							else
							{
								RevokeScriptPermission(script, perm);
							}
						}
					}
				}

				LoadPermissionsData();
				_permissions[path] = sessions[0].Data.EntryScript.PermissionFlags;
				SavePermissionsData();
			}
		}
	}

	public static AddonSession? GetAddonSession(Script s)
	{
		if (_scriptToPath.TryGetValue(s, out string? path))
		{
			if (_pathToAddons.TryGetValue(path, out List<AddonSession>? sessions))
			{
				// Find the session that contains this script
				return sessions.FirstOrDefault(session => session.Scripts.Contains(s));
			}
		}
		return null;
	}

	public static List<AddonSession> GetAddonSessions(string path)
	{
		if (_pathToAddons.TryGetValue(path, out List<AddonSession>? sessions))
		{
			return sessions;
		}
		return [];
	}

	public static ScriptPermissionFlags GetExistingPermissionFlags(string path)
	{
		if (_permissions.TryGetValue(path, out var permissions))
		{
			return permissions;
		}
		return default;
	}

	public static bool GetHasAskedForPerms(string path)
	{
		return _permissions.ContainsKey(path);
	}

	private static void CleanupAddonSession(AddonSession session)
	{
		foreach (var script in session.Scripts)
		{
			_scriptToPath.Remove(script);
		}
		session.Scripts.Clear();
	}

	private static void LoadPermissionsData()
	{
		if (!File.Exists(AddonPermissionsFile)) return;
		string f = File.ReadAllText(AddonPermissionsFile);
		_permissions = JsonSerializer.Deserialize(f, AddonJSONGenerationContext.Default.AddonPermissionPair) ?? [];
	}

	private static void SavePermissionsData()
	{
		File.WriteAllText(AddonPermissionsFile, JsonSerializer.Serialize(_permissions, AddonJSONGenerationContext.Default.AddonPermissionPair));
	}

	public struct AddonMetadata
	{
		public string Name { get; set; }
		public string Description { get; set; }
		public string Author { get; set; }
		public string Version { get; set; }
	}

	public class AddonSession
	{
		public string Path { get; set; } = "";
		public PackedFormat.AddonData Data { get; set; }
		public List<Script> Scripts { get; set; } = [];
		public World? Root { get; set; }
	}
}

public class AddonPermissionPair : Dictionary<string, ScriptPermissionFlags>;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AddonMetadata))]
[JsonSerializable(typeof(AddonPermissionPair))]
[JsonSerializable(typeof(string))]
internal partial class AddonJSONGenerationContext : JsonSerializerContext { }
