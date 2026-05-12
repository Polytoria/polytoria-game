// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel.Resources;
using Polytoria.Shared;
using System;
using System.Reflection;

namespace Polytoria.Creator.Properties;

public sealed partial class BaseAssetProperty : Control, IProperty<BaseAsset?>
{
	private BaseAsset? _value;

	public BaseAsset? Value
	{
		get => _value;
		set
		{
			_value = value;
			Refresh();
		}
	}

	public Type PropertyType { get; set; } = null!;

	public event Action<object?>? ValueChanged;

	public object? GetValue()
	{
		return Value;
	}

	public void SetValue(object? value)
	{
		Value = (BaseAsset?)value;
	}

	private Control _propertyLayout = null!;
	private FoldableContainer _foldable = null!;
	private MenuButton _addMenu = null!;
	private Button _removeButton = null!;
	private PopupMenu _addMenuPopup = null!;

	public void Refresh()
	{
		ClearProperty();
		BaseAsset? baseAsset = Value;

		if (baseAsset != null)
		{
			_addMenu.Visible = false;
			_foldable.Visible = true;

			_foldable.Title = baseAsset.ClassName;

			Type typeToLoad = baseAsset.GetType();

			// TODO: Kinda hardcoded, we should look into this
			if (baseAsset is AudioAsset)
			{
				typeToLoad = typeof(AudioAsset);
			}

			IPropertySubview? subview = Globals.LoadSubviewProperty(typeToLoad);
			if (subview != null)
			{
				subview.TargetObject = baseAsset;
				_propertyLayout.AddChild((Node)subview);
			}

			foreach (PropertyInfo property in baseAsset.GetEditableProperties())
			{
				_propertyLayout.AddChild(UI.Properties.CreatePropertyControl([baseAsset], property));
			}
		}
		else
		{
			_addMenu.Visible = true;
			_foldable.Visible = false;
		}
	}

	private void ClearProperty()
	{
		foreach (Node item in _propertyLayout.GetChildren())
		{
			item.QueueFree();
		}
	}

	private void RemoveBaseAsset()
	{
		ValueChanged?.Invoke(null);
	}

	private void ListAddMenu()
	{
		Type baseType = PropertyType;

		var derivedTypes = BaseAsset.GetAllDerivedTypesOf(baseType);

		int i = 0;

		foreach (Type t in derivedTypes)
		{
			_addMenuPopup.AddIconItem(Globals.LoadIcon(t.Name), t.Name, i);
			_addMenuPopup.SetItemIconMaxWidth(i, 16);
			i++;
		}
	}

	public override void _Ready()
	{
		_propertyLayout = GetNode<Control>("Foldable/Layout/Properties");
		_removeButton = GetNode<Button>("Foldable/Layout/RemoveButton");
		_addMenu = GetNode<MenuButton>("AddMenu");
		_foldable = GetNode<FoldableContainer>("Foldable");
		_foldable.Folded = true;
		_removeButton.Pressed += RemoveBaseAsset;
		_addMenuPopup = _addMenu.GetPopup();
		_addMenuPopup.IndexPressed += AddMenuPressed;

		ListAddMenu();

		Refresh();
	}

	private void AddMenuPressed(long index)
	{
		string typeName = _addMenuPopup.GetItemText((int)index);
		BaseAsset? asset = (BaseAsset?)Globals.LoadNetworkedObject(typeName);
		ValueChanged?.Invoke(asset);
	}
}
