// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Creator;
using Polytoria.Datamodel.Services;
using Polytoria.Shared;
using Polytoria.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using Script = Polytoria.Datamodel.Script;

namespace Polytoria.Creator.UI;

public partial class InsertMenuPopup : PopupPanel
{
	private Vector2I _popupSize = new(250, 350);
	private PackedScene _categoryTitlePacked = GD.Load<PackedScene>("res://scenes/creator/popups/insert/components/category_title.tscn");
	private PackedScene _itemPacked = GD.Load<PackedScene>("res://scenes/creator/popups/insert/components/item.tscn");
	public Instance? InsertTo;

	private sealed class ItemKey
	{
		public string Title = "";
		public Type[] RecommendOn = [];
	}

	private sealed class SubItems : List<string>
	{
	}

	private readonly Dictionary<ItemKey, SubItems> insertItems = new()
	{
		[new() { Title = "Building" }] = new()
		{
			"Part",
			"Truss",
			"Mesh",
			"Seat",
		},
		[new() { Title = "Lights" }] = new()
		{
			"PointLight",
			"SpotLight"
		},
		[new() { Title = "Elements" }] = new()
		{
			"Text3D",
			"Image3D",
			"Decal",
			"Sound",
			"Particles",
			"NPC",
			"GUI3D",
			"Folder",
			"Model",
			"Tool",
			"Marker3D",
			"Camera",
		},
		[new() { Title = "Character", RecommendOn = [typeof(CharacterModel)] }] = new()
		{
			"Accessory",
			"Clothing",
		},
		[new() { Title = "Scripting", RecommendOn = [typeof(ScriptService), typeof(Folder)] }] = new()
		{
			"NetworkEvent",
			"BindableEvent",
		},
		[new() { Title = "Values", RecommendOn = [typeof(Folder)] }] = new()
		{
			"BoolValue",
			"IntValue",
			"NumberValue",
			"StringValue",
			"Vector2Value",
			"Vector3Value",
			"ColorValue",
			"InstanceValue"
		},
		[new() { Title = "Skies", RecommendOn = [typeof(Lighting)] }] = new()
		{
			"ImageSky",
			"GradientSky",
			"ProceduralSky"
		},
		[new() { Title = "Physics" }] = new()
		{
			"RigidBody",
			"BodyPosition",
			"Grabbable",
		},
		/*
		["Vehicles"] = new()
		{
			"Vehicle",
			"VehicleWheel",
			"VehicleSeat",
		},
		*/
		[new() { Title = "Lighting Effects", RecommendOn = [typeof(Lighting)] }] = new()
		{
			"ColorAdjustModifier",
		},
		[new() { Title = "UIs", RecommendOn = [typeof(UIField), typeof(GUI), typeof(GUI3D), typeof(PlayerGUI)] }] = new()
		{
			"GUI",
			"UIView",
			"UILabel",
			"UIButton",
			"UIImage",
			"UITextInput",
			"UIHLayout",
			"UIVLayout",
			"UIHFlow",
			"UIVFlow",
			"UIGridLayout",
			"UIScrollView",
			"UIViewport",
		},
		[new() { Title = "Stats", RecommendOn = [typeof(Stats)] }] = new()
		{
			"Stat",
		},
		[new() { Title = "Teams", RecommendOn = [typeof(Teams)] }] = new()
		{
			"Team",
		},
	};

	[Export] public LineEdit SearchBox = null!;

	[Export] public Control ItemContainer = null!;
	private Button? _bottomFix;

	private Control? _dummyFocus;
	public override void _Ready()
	{
		SearchBox.TextChanged += OnSearchTextChanged;
		AboutToPopup += OnAboutToPopup;
		CloseRequested += OnCloseRequested;
		TreeExiting += OnCloseRequested;

		PopulateItems();
		SearchBox.GrabFocus();
		SearchBox.GuiInput += OnSearchboxGUIInput;
		SearchBox.FocusEntered += OnSearchBoxFocusEntered;

		base._Ready();
	}

	private void OnSearchBoxFocusEntered()
	{
		SearchBox.Edit();
	}

	private void OnSearchboxGUIInput(InputEvent @event)
	{
		if (@event is InputEventKey key)
		{
			if (key.Keycode == Key.Down)
			{
				_bottomFix?.GrabFocus();
			}
		}
	}

	private void Clear()
	{
		for (int i = ItemContainer.GetChildCount() - 1; i >= 0; i--)
		{
			Node child = ItemContainer.GetChild(i);
			child.QueueFree();
		}
	}


	private void PopulateItems(string? search = null)
	{
		Clear();

		string? query = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
		InsertPopupItem? previousItem = null;
		InsertPopupItem? firstItem = null;

		_bottomFix?.QueueFree();
		_bottomFix = new()
		{
			Visible = false
		};
		ItemContainer.AddChild(_bottomFix);

		List<(ItemKey cat, List<string> filtered)> toProcess = [];

		// Process recommended
		foreach (KeyValuePair<ItemKey, SubItems> kv in insertItems)
		{
			ItemKey category = kv.Key;
			SubItems subItems = kv.Value;

			// filter subitems based on search
			List<string> filtered = query == null
				? subItems
				: subItems.Where(s => s.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

			// If none matched, skip this category
			if (filtered.Count == 0)
				continue;

			// Check if this category is recommended
			bool isRecommended = false;
			if (InsertTo != null && category.RecommendOn.Length > 0)
			{
				Type insertToType = InsertTo.GetType();

				isRecommended = category.RecommendOn.Any(insertToType.IsAssignableTo);
			}

			if (isRecommended)
			{
				toProcess.Insert(0, (category, filtered));
			}
			else
			{
				toProcess.Add((category, filtered));
			}
		}

		// Process categories
		foreach (var (cat, filtered) in toProcess)
		{
			Control categoryTitle = _categoryTitlePacked.Instantiate<Control>();
			categoryTitle.GetNode<Label>("Label").Text = cat.Title;
			ItemContainer.AddChild(categoryTitle);

			foreach (string className in filtered)
			{
				string myClass = className;

				InsertPopupItem item = _itemPacked.Instantiate<InsertPopupItem>();
				item.Pressed += () => OnInsert(myClass);
				item.Classname = myClass;
				ItemContainer.AddChild(item);
				firstItem ??= item;

				if (previousItem != null)
				{
					previousItem.FocusNeighborBottom = item.GetPath();
					item.FocusNeighborTop = previousItem.GetPath();
				}
				previousItem = item;
			}
		}

		if (firstItem != null)
		{
			SearchBox.FocusNeighborBottom = firstItem.GetPath();
			_bottomFix!.FocusNeighborBottom = firstItem.GetPath();
			firstItem.FocusNeighborTop = SearchBox.GetPath();
		}
	}

	private void OnSearchTextChanged(string newText)
	{
		PopulateItems(newText);
	}

	private void OnInsert(string className)
	{
		if (World.Current == null)
		{
			return;
		}

		Instance instance = Globals.LoadInstance<Instance>(className, World.Current)!;
		Instance parentTo;

		if (InsertTo != null)
		{
			parentTo = InsertTo;
		}
		else
		{
			switch (instance)
			{
				// Default insert path
				case Part:
					parentTo = World.Current.Environment;
					break;
				case Light:
					parentTo = World.Current.Lighting;
					break;
				case UIField when instance is not GUI:
					{
						GUI? existingUI = (GUI?)World.Current.PlayerGUI.FindChild("GUI");
						if (existingUI == null)
						{
							existingUI = World.Current.New<GUI>();
							existingUI.Parent = World.Current.PlayerGUI;
						}

						parentTo = existingUI;
						break;
					}
				case Script:
					parentTo = World.Current.ScriptService;
					break;
				default:
					parentTo = World.Current.Environment;
					break;
			}
		}

		instance.Name = className;
		instance.CreatorInserted();

		World.Current.CreatorContext.History.CreateInstances([instance], parentTo);
		World.Current.CreatorContext.Selections.SelectOnly(instance);

		if (instance is Dynamic dyn)
		{
			Datamodel.Environment.RayResult? hit = World.Current.CreatorContext.Freelook.GetPlacementRay();
			if (hit != null)
			{
				Vector3 surfacePoint = hit.Value.Position;
				Vector3 surfaceNormal = hit.Value.Normal;
				float offsetDistance = dyn.CalculateBounds().Size.Y / 2;

				dyn.Position = surfacePoint + surfaceNormal * offsetDistance;
			}
			else
			{
				dyn.Position = World.Current.CreatorContext.Freelook.GetPlacementPosition();
			}

			if (CreatorService.Interface.MoveSnapEnabled)
			{
				dyn.Position = dyn.Position.Snap(CreatorService.Interface.MoveSnapping);
			}
		}

		QueueFree();
	}

	private void OnCloseRequested()
	{
		_dummyFocus?.QueueFree();
	}

	private void OnAboutToPopup()
	{
		_dummyFocus = new();
		GetParent().AddChild(_dummyFocus);
		_dummyFocus.FocusMode = Control.FocusModeEnum.All;
		_dummyFocus.GrabFocus();
	}

	public void PopupAtCursor()
	{
		Popup(new((Vector2I)GetViewport().GetMousePosition(), _popupSize));
	}
}
