// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Polytoria.Creator.Input;

public sealed record class CreatorKeybindSectionDef
{
	public required string Key { get; init; }
	public required string Label { get; init; }
	public required int SortOrder { get; init; }
}

public sealed record class CreatorKeybindDefinition
{
	public required string Id { get; init; }
	public required string SectionKey { get; init; }
	public required string Label { get; init; }
	public string Description { get; init; } = string.Empty;
	public bool RequireGameOpen { get; init; }
	public IReadOnlyList<CreatorKeybindBinding> DefaultBindings { get; init; } = [];
}

public sealed record class CreatorKeybindBinding
{
	public Key Keycode { get; set; } = Key.None;
	public bool CtrlPressed { get; set; }
	public bool ShiftPressed { get; set; }
	public bool AltPressed { get; set; }
	public bool MetaPressed { get; set; }

	public InputEventKey ToInputEvent()
	{
		return new()
		{
			Keycode = Keycode,
			CtrlPressed = CtrlPressed,
			ShiftPressed = ShiftPressed,
			AltPressed = AltPressed,
			MetaPressed = MetaPressed,
			CommandOrControlAutoremap = CtrlPressed
		};
	}

	public string ToDisplayString()
	{
		List<string> parts = [];

		if (CtrlPressed) parts.Add("Ctrl");
		if (ShiftPressed) parts.Add("Shift");
		if (AltPressed) parts.Add("Alt");
		if (MetaPressed) parts.Add("Meta");

		string keyName = Keycode == Key.None ? "Unbound" : Keycode.ToString();
		parts.Add(keyName);
		return string.Join("+", parts);
	}

	public CreatorKeybindBinding Copy()
	{
		return new()
		{
			Keycode = Keycode,
			CtrlPressed = CtrlPressed,
			ShiftPressed = ShiftPressed,
			AltPressed = AltPressed,
			MetaPressed = MetaPressed
		};
	}
}

public static class CreatorKeybinds
{
	private const string KeybindsPath = "user://creator/keybinds.json";

	public static readonly IReadOnlyList<CreatorKeybindSectionDef> Sections =
	[
		new() { Key = "creator", Label = "Creator", SortOrder = 0 },
		new() { Key = "file", Label = "File", SortOrder = 1 },
		new() { Key = "edit", Label = "Edit", SortOrder = 2 },
		new() { Key = "insert", Label = "Insert", SortOrder = 3 },
		new() { Key = "model", Label = "Model", SortOrder = 4 },
		new() { Key = "tools", Label = "Tools", SortOrder = 5 },
		new() { Key = "view", Label = "View", SortOrder = 6 },
		new() { Key = "help", Label = "Help", SortOrder = 7 },
		new() { Key = "dev", Label = "Dev", SortOrder = 8 },
	];

	public static readonly IReadOnlyList<CreatorKeybindDefinition> Definitions = BuildDefinitions();
	public static readonly IReadOnlyDictionary<string, CreatorKeybindDefinition> DefinitionsById = Definitions.ToDictionary(x => x.Id);

	private static readonly Dictionary<string, List<CreatorKeybindBinding>> _bindings = [];
	private static bool _initialized;

	public static event Action? Changed;

	public static void Init()
	{
		if (_initialized)
			return;

		_initialized = true;
		Load();
	}

	public static IReadOnlyList<CreatorKeybindDefinition> GetDefinitionsForSection(string sectionKey)
	{
		return Definitions.Where(def => def.SectionKey == sectionKey).ToList();
	}

	public static IReadOnlyList<CreatorKeybindBinding> GetBindings(string id)
	{
		Init();

		if (_bindings.TryGetValue(id, out List<CreatorKeybindBinding>? bindings))
		{
			return bindings.Select(binding => binding.Copy()).ToList();
		}

		if (DefinitionsById.TryGetValue(id, out CreatorKeybindDefinition? def))
		{
			return def.DefaultBindings.Select(binding => binding.Copy()).ToList();
		}

		return [];
	}

	public static string GetDisplayText(string id)
	{
		IReadOnlyList<CreatorKeybindBinding> bindings = GetBindings(id);
		if (bindings.Count == 0)
		{
			return "Unbound";
		}

		return string.Join(", ", bindings.Select(binding => binding.ToDisplayString()));
	}

	public static Shortcut? GetShortcut(string id)
	{
		IReadOnlyList<CreatorKeybindBinding> bindings = GetBindings(id);
		if (bindings.Count == 0)
		{
			return null;
		}

		Shortcut shortcut = new();
		shortcut.Events = [.. bindings.Select(binding => binding.ToInputEvent())];
		return shortcut;
	}

	public static void SetBindings(string id, IEnumerable<CreatorKeybindBinding> bindings)
	{
		Init();

		List<CreatorKeybindBinding> normalized = bindings.Select(binding => binding.Copy()).ToList();

		if (DefinitionsById.TryGetValue(id, out CreatorKeybindDefinition? def)
			&& normalized.SequenceEqual(def.DefaultBindings))
		{
			if (_bindings.Remove(id))
			{
				Save();
				Changed?.Invoke();
			}
			return;
		}

		_bindings[id] = normalized;
		Save();
		Changed?.Invoke();
	}

	public static void ReplaceBinding(string id, int index, CreatorKeybindBinding binding)
	{
		List<CreatorKeybindBinding> bindings = GetBindings(id).Select(b => b.Copy()).ToList();

		if (index < 0 || index >= bindings.Count)
		{
			bindings.Add(binding.Copy());
		}
		else
		{
			bindings[index] = binding.Copy();
		}

		SetBindings(id, bindings);
	}

	public static void AddBinding(string id, CreatorKeybindBinding binding)
	{
		List<CreatorKeybindBinding> bindings = GetBindings(id).Select(b => b.Copy()).ToList();
		bindings.Add(binding.Copy());
		SetBindings(id, bindings);
	}

	public static void RemoveBinding(string id, int index)
	{
		List<CreatorKeybindBinding> bindings = GetBindings(id).Select(b => b.Copy()).ToList();
		if (index < 0 || index >= bindings.Count)
			return;

		bindings.RemoveAt(index);
		SetBindings(id, bindings);
	}

	public static void ResetBinding(string id)
	{
		Init();

		if (_bindings.Remove(id))
		{
			Save();
			Changed?.Invoke();
		}
	}

	public static void ResetAll()
	{
		Init();

		if (_bindings.Count == 0)
			return;

		_bindings.Clear();
		Save();
		Changed?.Invoke();
	}

	public static bool IsPressed(string id)
	{
		foreach (CreatorKeybindBinding binding in GetBindings(id))
		{
			if (binding.Keycode == Key.None)
				continue;

			if (!Godot.Input.IsKeyPressed(binding.Keycode))
				continue;

			if (binding.CtrlPressed && !Godot.Input.IsKeyPressed(Key.Ctrl))
				continue;
			if (binding.ShiftPressed && !Godot.Input.IsKeyPressed(Key.Shift))
				continue;
			if (binding.AltPressed && !Godot.Input.IsKeyPressed(Key.Alt))
				continue;
			if (binding.MetaPressed && !Godot.Input.IsKeyPressed(Key.Meta))
				continue;

			return true;
		}

		return false;
	}

	private static void Load()
	{
		if (!Godot.FileAccess.FileExists(KeybindsPath))
			return;

		try
		{
			string json = Godot.FileAccess.GetFileAsString(KeybindsPath);
			Dictionary<string, List<CreatorKeybindBinding>>? data =
				JsonSerializer.Deserialize(json, KeybindsGenerationContext.Default.DictionaryStringListCreatorKeybindBinding);

			if (data == null)
				return;

			_bindings.Clear();
			foreach ((string key, List<CreatorKeybindBinding> value) in data)
			{
				if (!DefinitionsById.ContainsKey(key))
					continue;

				_bindings[key] = value.Select(binding => binding.Copy()).ToList();
			}
		}
		catch (Exception ex)
		{
			PT.PrintErr("Failed to load creator keybinds: ", ex);
		}
	}

	private static void Save()
	{
		try
		{
			string json = JsonSerializer.Serialize(_bindings, KeybindsGenerationContext.Default.DictionaryStringListCreatorKeybindBinding);
			using var file = Godot.FileAccess.Open(KeybindsPath, Godot.FileAccess.ModeFlags.Write);
			if (file == null)
			{
				PT.PrintErr($"Failed to open keybind file for writing: {KeybindsPath}");
				return;
			}

			file.StoreString(json);
		}
		catch (Exception ex)
		{
			PT.PrintErr("Failed to save creator keybinds: ", ex);
		}
	}

	private static List<CreatorKeybindDefinition> BuildDefinitions()
	{
		List<CreatorKeybindDefinition> defs = [];

		defs.Add(new()
		{
			Id = "creator.modifier_ctrl",
			SectionKey = "creator",
			Label = "Control Modifier",
			Description = "Default modifier used for add/select actions.",
			DefaultBindings = [new() { Keycode = Key.Ctrl }]
		});
		defs.Add(new()
		{
			Id = "creator.modifier_shift",
			SectionKey = "creator",
			Label = "Shift Modifier",
			Description = "Default modifier used for alternate action variants.",
			DefaultBindings = [new() { Keycode = Key.Shift }]
		});
		defs.Add(new()
		{
			Id = "creator.modifier_alt",
			SectionKey = "creator",
			Label = "Alt Modifier",
			Description = "Default modifier used for temporary overrides.",
			DefaultBindings = [new() { Keycode = Key.Alt }]
		});

		defs.Add(new()
		{
			Id = "file.new",
			SectionKey = "file",
			Label = "New",
			Description = "Create a new world.",
			DefaultBindings = [new() { CtrlPressed = true, Keycode = Key.N }]
		});
		defs.Add(new()
		{
			Id = "file.open",
			SectionKey = "file",
			Label = "Open",
			Description = "Open a world from disk.",
			DefaultBindings = [new() { CtrlPressed = true, Keycode = Key.O }]
		});
		defs.Add(new()
		{
			Id = "file.keybinds",
			SectionKey = "file",
			Label = "Keybinds",
			Description = "Open the keybind editor."
		});
		defs.Add(new()
		{
			Id = "file.save",
			SectionKey = "file",
			Label = "Save",
			Description = "Save the current world.",
			RequireGameOpen = true,
			DefaultBindings = [new() { CtrlPressed = true, Keycode = Key.S }]
		});
		defs.Add(new()
		{
			Id = "file.save_as",
			SectionKey = "file",
			Label = "Save As...",
			Description = "Save the current world to a new file.",
			RequireGameOpen = true,
			DefaultBindings = [new() { CtrlPressed = true, ShiftPressed = true, Keycode = Key.S }]
		});
		defs.Add(new()
		{
			Id = "file.publish",
			SectionKey = "file",
			Label = "Publish",
			Description = "Publish the current world.",
			RequireGameOpen = true
		});
		defs.Add(new()
		{
			Id = "file.exit",
			SectionKey = "file",
			Label = "Exit",
			Description = "Quit the application."
		});

		defs.Add(new()
		{
			Id = "edit.undo",
			SectionKey = "edit",
			Label = "Undo",
			Description = "Undo the last edit.",
			RequireGameOpen = true,
			DefaultBindings = [new() { CtrlPressed = true, Keycode = Key.Z }]
		});
		defs.Add(new()
		{
			Id = "edit.redo",
			SectionKey = "edit",
			Label = "Redo",
			Description = "Redo the last undone edit.",
			RequireGameOpen = true,
			DefaultBindings = [new() { CtrlPressed = true, ShiftPressed = true, Keycode = Key.Z }]
		});
		defs.Add(new()
		{
			Id = "edit.delete",
			SectionKey = "edit",
			Label = "Delete",
			Description = "Delete the current selection.",
			RequireGameOpen = true,
			DefaultBindings = [new() { Keycode = Key.Delete }, new() { Keycode = Key.Backspace }]
		});
		defs.Add(new()
		{
			Id = "edit.duplicate",
			SectionKey = "edit",
			Label = "Duplicate",
			Description = "Duplicate the current selection.",
			RequireGameOpen = true,
			DefaultBindings = [new() { CtrlPressed = true, Keycode = Key.D }]
		});
		defs.Add(new()
		{
			Id = "edit.toggle_locked",
			SectionKey = "edit",
			Label = "Toggle Locked",
			Description = "Toggle lock state on the selection.",
			RequireGameOpen = true,
			DefaultBindings = [new() { CtrlPressed = true, Keycode = Key.L }]
		});
		defs.Add(new()
		{
			Id = "edit.select_all",
			SectionKey = "edit",
			Label = "Select All",
			Description = "Select all descendants of the current world.",
			RequireGameOpen = true,
			DefaultBindings = [new() { CtrlPressed = true, Keycode = Key.A }]
		});
		defs.Add(new()
		{
			Id = "edit.input_manager",
			SectionKey = "edit",
			Label = "Input Manager",
			Description = "Open the input manager for the project."
		});

		defs.Add(new()
		{
			Id = "insert.new_instance",
			SectionKey = "insert",
			Label = "New Instance",
			Description = "Open the insert menu.",
			RequireGameOpen = true,
			DefaultBindings = [new() { ShiftPressed = true, Keycode = Key.Space }, new() { CtrlPressed = true, Keycode = Key.I }]
		});

		defs.Add(new()
		{
			Id = "model.group",
			SectionKey = "model",
			Label = "Group",
			Description = "Group the current selection.",
			RequireGameOpen = true,
			DefaultBindings = [new() { CtrlPressed = true, Keycode = Key.G }]
		});
		defs.Add(new()
		{
			Id = "model.ungroup",
			SectionKey = "model",
			Label = "Ungroup",
			Description = "Ungroup the current selection.",
			RequireGameOpen = true,
			DefaultBindings = [new() { CtrlPressed = true, Keycode = Key.U }]
		});
		defs.Add(new()
		{
			Id = "model.group_folder",
			SectionKey = "model",
			Label = "Group Folder",
			Description = "Group the selection into a folder.",
			RequireGameOpen = true,
			DefaultBindings = [new() { CtrlPressed = true, AltPressed = true, Keycode = Key.G }]
		});
		defs.Add(new()
		{
			Id = "model.group_rigidbody",
			SectionKey = "model",
			Label = "Group RigidBody",
			Description = "Group the selection into a rigid body.",
			RequireGameOpen = true,
			DefaultBindings = [new() { CtrlPressed = true, ShiftPressed = true, Keycode = Key.G }]
		});

		defs.Add(new()
		{
			Id = "tools.play_test",
			SectionKey = "tools",
			Label = "Play Test",
			Description = "Start a local playtest.",
			RequireGameOpen = true,
			DefaultBindings = [new() { Keycode = Key.F5 }]
		});
		defs.Add(new()
		{
			Id = "tools.play_test_here",
			SectionKey = "tools",
			Label = "Play Test Here",
			Description = "Start a local playtest from the current camera position.",
			RequireGameOpen = true,
			DefaultBindings = [new() { CtrlPressed = true, Keycode = Key.F5 }]
		});
		defs.Add(new()
		{
			Id = "tools.manage_addons",
			SectionKey = "tools",
			Label = "Manage Addons",
			Description = "Open the addon folder."
		});
		defs.Add(new()
		{
			Id = "tools.migrate_coordinates",
			SectionKey = "tools",
			Label = "Migrate Coordinates",
			Description = "Convert the current world to the new coordinate format.",
			RequireGameOpen = true
		});
		defs.Add(new()
		{
			Id = "tools.settings",
			SectionKey = "tools",
			Label = "Settings",
			Description = "Open the creator settings."
		});

		defs.Add(new()
		{
			Id = "view.toggle_fullscreen",
			SectionKey = "view",
			Label = "Toggle Fullscreen",
			Description = "Switch the application between fullscreen and windowed mode.",
			DefaultBindings = [new() { Keycode = Key.F11 }]
		});

		defs.Add(new()
		{
			Id = "help.copy_system_info",
			SectionKey = "help",
			Label = "Copy System Info",
			Description = "Copy diagnostic system information to the clipboard."
		});
		defs.Add(new()
		{
			Id = "help.open_documentation",
			SectionKey = "help",
			Label = "Open Documentation",
			Description = "Open the Polytoria documentation."
		});
		defs.Add(new()
		{
			Id = "help.report_bug",
			SectionKey = "help",
			Label = "Report a Bug",
			Description = "Open the bug report page."
		});

		defs.Add(new()
		{
			Id = "dev.pack_current_project",
			SectionKey = "dev",
			Label = "Pack Current Project",
			Description = "Pack the current project into a single file.",
			RequireGameOpen = true
		});
		defs.Add(new()
		{
			Id = "dev.link_device",
			SectionKey = "dev",
			Label = "Link Device",
			Description = "Open the device linking prompt."
		});

		return defs;
	}
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(CreatorKeybindBinding))]
[JsonSerializable(typeof(Dictionary<string, List<CreatorKeybindBinding>>))]
public partial class KeybindsGenerationContext : JsonSerializerContext { }
