// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Creator.Properties;
using Polytoria.Creator.UI.Misc;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Creator;
using Polytoria.Shared;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Polytoria.Creator.UI;

public sealed partial class Properties : TabContainer
{
	public static Properties Singleton { get; private set; } = null!;
	public Properties()
	{
		Singleton = this;
	}

	private static readonly Type _editable = typeof(EditableAttribute);
	private static readonly Type _obsolete = typeof(Attributes.ObsoleteAttribute);
	private PackedScene _propertiesPacked = GD.Load<PackedScene>("res://scenes/creator/docks/properties/properties_view.tscn");
	private static readonly Dictionary<World, List<PropertyConnection>> _gameToConnections = [];

	private class PropertyConnection
	{
		public NetworkedObject Target { get; set; } = null!;
		public IProperty Input { get; set; } = null!;
		public PropertyInfo Property { get; set; } = null!;
		public Action<string> Handler { get; set; } = null!;
	}

	private static readonly Dictionary<World, PropertiesView> _gameToView = [];

	public void SwitchTo(World? game)
	{
		if (game == null || !_gameToView.TryGetValue(game, out PropertiesView? value))
		{
			CurrentTab = -1;
			return;
		}

		CurrentTab = GetTabIdxFromControl(value);
	}

	public void Insert(World game)
	{
		AddChild(_gameToView[game] = _propertiesPacked.Instantiate<PropertiesView>());
		_gameToConnections[game] = [];
	}

	public static void Remove(World game)
	{
		DisconnectAllConnections(game);
		if (_gameToView.Remove(game, out PropertiesView? container))
		{
			container.QueueFree();
			container.Dispose();
		}
		_gameToConnections.Remove(game);
	}

	private static void DisconnectAllConnections(World root)
	{
		if (!_gameToConnections.TryGetValue(root, out List<PropertyConnection>? connections))
			return;

		foreach (PropertyConnection connection in connections)
		{
			connection.Target.PropertyChanged.Disconnect(connection.Handler);
		}

		connections.Clear();
	}


	public static void Show(Instance instance)
	{
		WalkProperties(Clear(instance), instance, instance.GetType());
		GetView(instance.Root).TagsView.Show([instance]);
	}

	public static void ShowMultiple(List<Instance> instances)
	{
		if (instances.Count == 0) return;
		VBoxContainer list = Clear(instances[0]);
		GetView(instances[0].Root).TagsView.Show(instances);

		foreach (Type commonType in GetCommonTypes(instances))
		{
#pragma warning disable IL2072 // Datamodel types has the reflections needed
			WalkProperties(list, instances[0], commonType, instances);
#pragma warning restore IL2072
		}
	}

	private static HashSet<Type> GetCommonTypes(List<Instance> instances)
	{
		if (instances.Count == 0) return [];

		HashSet<Type> commonTypes = [];
		Type? type = instances[0].GetType();
		while (type != null && type.Namespace == "Polytoria.Datamodel")
		{
			commonTypes.Add(type);
			type = type.BaseType;
		}

		for (int i = 1; i < instances.Count; i++)
		{
			HashSet<Type> instanceTypes = [];
			Type? t = instances[i].GetType();
			while (t != null && t.Namespace == "Polytoria.Datamodel")
			{
				instanceTypes.Add(t);
				t = t.BaseType;
			}

			commonTypes.IntersectWith(instanceTypes);
		}

		return commonTypes;
	}


	public static VBoxContainer Clear(Instance instance)
	{
		return ClearRoot(instance.Root);
	}

	public static VBoxContainer ClearRoot(World root)
	{
		VBoxContainer list = _gameToView[root].PropertiesContainer;
		_gameToView[root].TagsView.Show([]);

		foreach (Node child in list.GetChildren())
		{
			child.QueueFree();
		}

		return list;
	}

	public static PropertiesView GetView(World root)
	{
		return _gameToView[root];
	}

	private static void WalkProperties(VBoxContainer list, Instance instance, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type type, List<Instance>? multiple = null)
	{
		VBoxContainer layout = new() { MouseFilter = MouseFilterEnum.Ignore };

		bool hasProperties = false;

		foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
		{
			if (!property.IsDefined(_editable, false)) continue;
			if (property.IsDefined(_obsolete, false)) continue;

			// Skip if is hidden
			EditableAttribute? editableAttr = property.GetCustomAttribute<EditableAttribute>();
			if (editableAttr != null && editableAttr.IsHidden) continue;

			MethodInfo? getter = property.GetGetMethod(false);
			if (getter == null || getter != getter.GetBaseDefinition()) continue;

			// Use multiple instances if available, otherwise just the single one.
			List<NetworkedObject> targets = multiple != null ? new(multiple) : [instance];
			try
			{
				layout.AddChild(CreatePropertyControl(targets, property));
			}
			catch (Exception ex)
			{
				PT.PrintErr(ex);
			}

			hasProperties = true;
		}

		if (hasProperties)
		{
			TextureRect icon = new()
			{
				Texture = Globals.LoadIcon(type.Name),
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspect,
				CustomMinimumSize = new(18, 18)
			};

			Label title = new()
			{
				Text = type.Name,
				TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
				CustomMinimumSize = new(0, 18),
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
			};
			title.AddThemeColorOverride("font_color", new(0xccccccff));
			title.AddThemeFontSizeOverride("font_size", 14);

			HBoxContainer hbox = new();
			hbox.AddChild(icon);
			hbox.AddChild(title);

			PanelContainer header = new();
			header.AddThemeStyleboxOverride("panel", new StyleBoxFlat()
			{
				BgColor = new(0x161616ff),
				CornerRadiusTopLeft = 6,
				CornerRadiusTopRight = 6,
				CornerRadiusBottomRight = 6,
				CornerRadiusBottomLeft = 6,
				ExpandMarginLeft = 4,
				ContentMarginLeft = 2,
				ContentMarginTop = 4,
				ContentMarginRight = 6,
				ContentMarginBottom = 4
			});
			header.AddChild(hbox);
			list.AddChild(header);
			list.AddChild(layout);
		}

		if (multiple == null && type.BaseType?.Namespace == "Polytoria.Datamodel")
		{
			WalkProperties(list, instance, type.BaseType);
		}
	}

	public static Control CreatePropertyControl(List<NetworkedObject> networkedObjects, PropertyInfo property)
	{
		HBoxContainer container = new();
		PropertyLabel t = new()
		{
			Text = property.Name,
			VerticalAlignment = VerticalAlignment.Center,
			TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
			CustomMinimumSize = new(88, 24),
			TooltipText = property.Name,
			MouseFilter = MouseFilterEnum.Pass,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsStretchRatio = 0.4f,
			Property = property
		};

		IProperty input = Globals.LoadProperty(property.PropertyType);
		Control c = (Control)input;
		c.SizeFlagsStretchRatio = 0.6f;

		t.PropertyPair = input;

		container.AddChild(t);

		input.PropertyType = property.PropertyType;

		if (input is BooleanProperty)
		{
			// Special center for booleans
			c.SizeFlagsHorizontal = SizeFlags.Expand | SizeFlags.ShrinkCenter;
		}
		else
		{
			c.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		}

		container.AddChild((Node)input);

		// Wait one frame for property to be ready
		Callable.From(() =>
		{
			Dictionary<NetworkedObject, object?> previewOldValues = [];

			NetworkedObject first = networkedObjects[0];
			input.SetValue(property.GetValue(first));
			World root = first.Root;

			CreatorHistory history = first.Root.CreatorContext.History;

			if (input is ColorProperty clr)
			{
				foreach (NetworkedObject item in networkedObjects)
					previewOldValues[item] = property.GetValue(item);

				clr.PreviewChanged += val =>
				{
					foreach (NetworkedObject item in networkedObjects)
					{
						property.SetValue(item, val);
					}
				};
			}

			NetworkedObject target = networkedObjects[0];

			Action<string> handler = (propName) =>
			{
				if (propName == property.Name && IsInstanceValid((Node)input))
				{
					object? value = property.GetValue(target);
					input.SetValue(value);
				}
			};

			PropertyConnection connection = new()
			{
				Target = target,
				Input = input,
				Property = property,
				Handler = handler
			};

			target.PropertyChanged.Connect(handler);

			if (_gameToConnections.TryGetValue(root, out List<PropertyConnection>? connections))
			{
				connections.Add(connection);
			}

			void valueChanged(object? val)
			{
				Dictionary<NetworkedObject, object?> oldValues;

				if (input is ColorProperty && previewOldValues.Count > 0)
				{
					oldValues = new(previewOldValues);
					previewOldValues.Clear();
				}
				else
				{
					oldValues = [];
					foreach (NetworkedObject item in networkedObjects)
						oldValues[item] = property.GetValue(item);
				}

				history.NewAction($"Change property {property.Name}");
				history.AddDoCallback(new((_) =>
				{
					foreach (NetworkedObject item in networkedObjects)
					{
						property.SetValue(item, val);
					}
					input.SetValue(val);
				}));

				history.AddUndoCallback(new((_) =>
				{
					foreach (NetworkedObject item in networkedObjects)
					{
						property.SetValue(item, oldValues[item]);
					}
					input.SetValue(oldValues[networkedObjects[0]]);
				}));

				history.CommitAction();
			}

			input.ValueChanged += valueChanged;
			t.Pasted += valueChanged;
		}).CallDeferred();

		return container;
	}
}
