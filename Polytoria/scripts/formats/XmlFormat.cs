// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel;
using Polytoria.Enums;
using Polytoria.Shared;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using TurboXml;
using static Polytoria.Datamodel.Part;
using XmlParser = TurboXml.XmlParser;

namespace Polytoria.Formats;

public static class XmlFormat
{
	private static readonly ShapeEnum[] _partShapes = [
		ShapeEnum.Brick,
		ShapeEnum.Sphere,
		ShapeEnum.Cylinder,
		ShapeEnum.Wedge,
		ShapeEnum.Truss,
		ShapeEnum.Frame,
		ShapeEnum.Bevel,
		ShapeEnum.Concave,
		ShapeEnum.Cone,
		ShapeEnum.Corner
	];

	private static readonly PartMaterialEnum[] _partMaterials = [
		PartMaterialEnum.SmoothPlastic,
		PartMaterialEnum.Wood,
		PartMaterialEnum.Concrete,
		PartMaterialEnum.Neon,
		PartMaterialEnum.Metal,
		PartMaterialEnum.Brick,
		PartMaterialEnum.Grass,
		PartMaterialEnum.Dirt,
		PartMaterialEnum.Stone,
		PartMaterialEnum.Snow,
		PartMaterialEnum.Ice,
		PartMaterialEnum.RustyIron,
		PartMaterialEnum.Sand,
		PartMaterialEnum.Sandstone,
		PartMaterialEnum.Plastic,
		PartMaterialEnum.Plywood,
		PartMaterialEnum.Planks,
		PartMaterialEnum.MetalGrid,
		PartMaterialEnum.MetalPlate,
		PartMaterialEnum.Fabric,
		PartMaterialEnum.Marble
	];

	private static readonly Dictionary<Type, string> _fixedServiceNames = new()
	{
		{typeof(Datamodel.Environment), nameof(Datamodel.Environment) },
		{typeof(Datamodel.Lighting), nameof(Datamodel.Lighting) },
		{typeof(Datamodel.Players), nameof(Datamodel.Players) },
		{typeof(Datamodel.Services.ScriptService), nameof(Datamodel.Services.ScriptService) },
		{typeof(Datamodel.Hidden), nameof(Datamodel.Hidden) },
		{typeof(Datamodel.ServerHidden), nameof(Datamodel.ServerHidden) },
		{typeof(Datamodel.PlayerDefaults), nameof(Datamodel.PlayerDefaults) },
		{typeof(Datamodel.PlayerGUI), nameof(Datamodel.PlayerGUI) },
	};

	private static readonly ConditionalWeakTable<Type, Dictionary<string, PropertyInfo>> _editablePropertyCache = [];
	private static readonly Assembly _datamodelAssembly = typeof(World).Assembly;
	private static readonly ConcurrentDictionary<string, Type?> _datamodelTypeCache = new(StringComparer.Ordinal);

	public class GameItem
	{
		[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
		public Type? Class;
		public string? Name;
		public List<(PropertyInfo Property, object Value)> Properties = new(12);
		public List<GameItem> Children = new(4);
	}

	public struct LegacyParser : IXmlReadHandler
	{
		public LegacyParser() { }

		private readonly Stack<GameItem> _items = new();
		private bool _inProps;
		private string? _propName;
		private string? _text;
		private float _x, _y, _z, _w;

		public readonly GameItem Root => _items.Peek();

		private static string? ParseString(ReadOnlySpan<char> value)
		{
			value = value.Trim();
			if (value.IsEmpty)
				return string.Empty;

			return value.ToString();
		}

		private static int ParseInt(string? value)
		{
			return int.TryParse(
			value,
			NumberStyles.AllowLeadingSign,
			CultureInfo.InvariantCulture,
			out int result
		) ? result : 0;
		}

		private static float ParseFloat(string? value)
		{
			return float.TryParse(
				value,
				NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
				CultureInfo.InvariantCulture,
				out float result
			) ? result : 0;
		}

		public void OnBeginTag(ReadOnlySpan<char> name, int line, int column)
		{
			if (name.SequenceEqual("game"))
			{
				_items.Push(new() { Class = typeof(World), Name = "World" });
			}
			else if (name.SequenceEqual("model"))
			{
				_items.Push(new() { Class = typeof(Model), Name = "Model" });
			}
			else if (name.SequenceEqual("Item"))
			{
				_items.Push(new());
			}
			else if (name.SequenceEqual("Properties"))
			{
				_inProps = true;
			}
		}

		public void OnAttribute(ReadOnlySpan<char> name, ReadOnlySpan<char> value, int nameLine, int nameColumn, int valueLine, int valueColumn)
		{
			if (name.SequenceEqual("class") && _items.TryPeek(out GameItem? item))
			{
				string? className = ParseString(value);

				if (className == null)
				{
					PT.Print("Empty class name: (line ", nameLine, ", column ", nameColumn, ")");
					return;
				}

				className = ConvertClassName(className);

				Type? datamodel = GetDatamodelType(className);
				if (datamodel == null)
				{
					PT.Print("Unknown class: ", className);
					return;
				}

				item.Class = datamodel;
			}
			else if (name.SequenceEqual("name") && _inProps)
			{
				_propName = ParseString(value);
			}
		}

		public void OnText(ReadOnlySpan<char> text, int line, int column)
		{
			_text = ParseString(text);
		}

		public void OnEndTag(ReadOnlySpan<char> name, int line, int column)
		{
			if (name.SequenceEqual("Item"))
			{
				if (_items.TryPop(out GameItem? item) && _items.TryPeek(out GameItem? parent) && item.Class != null)
				{
					parent.Children.Add(item);
				}
			}
			else if (name.SequenceEqual("Properties"))
			{
				_inProps = false;
			}
			else if (_inProps && _propName != null && _items.TryPeek(out GameItem? item) && item.Class != null)
			{
				if (name.SequenceEqual("X") || name.SequenceEqual("R"))
				{
					_x = ParseFloat(_text);
				}
				else if (name.SequenceEqual("Y") || name.SequenceEqual("G"))
				{
					_y = ParseFloat(_text);
				}
				else if (name.SequenceEqual("Z") || name.SequenceEqual("B"))
				{
					_z = ParseFloat(_text);
				}
				else if (name.SequenceEqual("A"))
				{
					_w = ParseFloat(_text);
				}
				else
				{
					object? value = null;

					if (name.SequenceEqual("string"))
					{
						value = _text;
					}
					else if (name.SequenceEqual("boolean"))
					{
						value = _text == "true";
					}
					else if (name.SequenceEqual("int"))
					{
						value = ParseInt(_text);
					}
					else if (name.SequenceEqual("float"))
					{
						value = ParseFloat(_text);
					}
					else if (name.SequenceEqual("vector2"))
					{
						value = new Vector2(_x, _y);
						_x = 0;
						_y = 0;
					}
					else if (name.SequenceEqual("vector3"))
					{
						value = new Vector3(_x, _y, _z);
						_x = 0;
						_y = 0;
						_z = 0;
					}
					else if (name.SequenceEqual("color"))
					{
						value = new Color(_x, _y, _z, _w);
						_x = 0;
						_y = 0;
						_z = 0;
						_w = 0;
					}

					if (value != null)
					{
						// Capitalize first character, for Backward compatibility.
						_propName = char.ToUpper(_propName[0]) + _propName.Substring(1);

						if (_propName == "Name")
						{
							item.Name = (string)value;
							return;
						}
						else if (_propName == "HideStuds")
						{
							return;
						}

						// Flip axis on dynamics
						PolyFormat.MigrateAxis(_propName, ref value);

						if (item.Class == typeof(Part))
						{
							if (value is int idx)
							{
								if (_propName == "Shape")
								{
									value = _partShapes[idx];
								}
								else if (_propName == "Material")
								{
									value = _partMaterials[idx];
								}
							}

							// Backward compatibility
							if (_propName == "IsKinematic")
							{
								_propName = "Anchored";
							}
							else if (_propName == "Scale")
							{
								_propName = "Size";
							}
							else if (_propName == "Shape" && value is string)
							{
								value = ShapeEnum.Brick;
							}
							else if (_propName == "Color" && value is string hex)
							{
								value = Color.FromString(hex, new());
							}
						}

						// Force shape to truss
						if (item.Class == typeof(Truss))
						{
							if (_propName == "Shape" && value is int idx)
							{
								value = ShapeEnum.Truss;
							}
						}

						if (item.Class == typeof(Text3D) && value is int alignmentInt)
						{
							if (_propName == "HorizontalAlignment")
							{
								if (alignmentInt == 1)
								{
									value = TextHorizontalAlignmentEnum.Left;
								}
								else if (alignmentInt == 2)
								{
									value = TextHorizontalAlignmentEnum.Center;
								}
								else if (alignmentInt == 4)
								{
									value = TextHorizontalAlignmentEnum.Right;
								}
							}
							else if (_propName == "VerticalAlignment")
							{
								if (alignmentInt == 256)
								{
									value = TextVerticalAlignmentEnum.Top;
								}
								else if (alignmentInt == 512)
								{
									value = TextVerticalAlignmentEnum.Middle;
								}
								else if (alignmentInt == 1024)
								{
									value = TextVerticalAlignmentEnum.Bottom;
								}
							}
						}

						if (item.Class == typeof(UILabel) || item.Class == typeof(UIButton))
						{
							if (_propName == "JustifyText")
							{
								_propName = "HorizontalAlignment";
							}
							else if (_propName == "VerticalAlign")
							{
								_propName = "VerticalAlignment";
							}
						}

						PropertyInfo? property = GetEditableProperty(item.Class, _propName);

						if (property == null)
						{
							return;
						}

						if (property.PropertyType == typeof(int) && value is string value2)
						{
							value = ParseInt(value2);
						}

						SetProperty(item, property, value);
					}

					_propName = null;
				}

				_text = null;
			}
		}
	}

	private static void SetProperty(GameItem item, PropertyInfo property, object value)
	{
		for (int i = 0; i < item.Properties.Count; i++)
		{
			if (item.Properties[i].Property == property)
			{
				item.Properties[i] = (property, value);
				return;
			}
		}

		item.Properties.Add((property, value));
	}

	private static PropertyInfo? GetEditableProperty([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type type, string propertyName)
	{
		Dictionary<string, PropertyInfo> properties = _editablePropertyCache.GetValue(type, static targetType =>
		{
			Dictionary<string, PropertyInfo> result = new(StringComparer.Ordinal);
			PropertyInfo[] allProps = targetType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);

			foreach (PropertyInfo property in allProps)
			{
				if (!property.IsDefined(typeof(EditableAttribute)))
				{
					continue;
				}

				result[property.Name] = property;
			}

			return result;
		});

		return properties.GetValueOrDefault(propertyName);
	}

	private static Type? GetDatamodelType(string className)
	{
		return _datamodelTypeCache.GetOrAdd(className, static name =>
		{
			Type? datamodelType = _datamodelAssembly.GetType($"Polytoria.Datamodel.{name}", throwOnError: false, ignoreCase: false);
			if (datamodelType != null)
			{
				return datamodelType;
			}

			return _datamodelAssembly.GetType($"Polytoria.Datamodel.Services.{name}", throwOnError: false, ignoreCase: false);
		});
	}

	public static async Task LoadFile(World game, string path)
	{
		string file = await File.ReadAllTextAsync(path);
		GameItem root = ParseContent(file);

		InitLegacyWorld(game);

		foreach (GameItem child in root.Children)
		{
			SpawnItem(child, game, game);
		}
	}

	public static async Task LoadString(World game, string content)
	{
		GameItem root = ParseContent(content);

		InitLegacyWorld(game);

		foreach (GameItem child in root.Children)
		{
			SpawnItem(child, game, game);
		}
	}

	private static void InitLegacyWorld(World world)
	{
		// Override server authroity on legacy worlds
		world.Players.UseServerAuthority = false;

		// Mark this world as legacy
		world.IsLegacyWorld = true;
	}

	public static async Task<Instance?> LoadModelString(World game, string content, Instance parent)
	{
		return await LoadModelItem(game, ParseContent(content), parent);
	}

	public static async Task<Instance?> LoadModelItem(World game, GameItem item, Instance parent)
	{
		return SpawnItem(item, parent, game);
	}

	public static GameItem ParseContent(string content)
	{
		LegacyParser parser = new();
		XmlParser.Parse(content, ref parser);

		GameItem root = parser.Root;

		// Skip root and spawn model
		if (root.Class == typeof(Model) &&
			root.Children.Count == 1 &&
			root.Children[0].Class == typeof(Model))
		{
			return root.Children[0];
		}

		return root;
	}

	public static Instance? SpawnItem(GameItem item, Instance parent, World root)
	{
		if (item.Class == null || item.Name == null)
		{
			PT.Print("bruh");
			return null;
		}

		if (item.Class == typeof(Player)) return null;

		// Fixed name for services / static classes
		if (_fixedServiceNames.TryGetValue(item.Class, out string? fixedName))
		{
			item.Name = fixedName;
		}

		string className = item.Class.Name;
		Instance? instance = parent is World game ? game.FindChild<Instance>(className) : null;

		// fix duplicates
		if (item.Class == typeof(Camera))
		{
			instance = root.Environment.CurrentCamera;
		}
		else if (item.Class == typeof(SunLight))
		{
			instance = root.Lighting.Sun;
		}
		else if (item.Class == typeof(Inventory))
		{
			instance = root.PlayerDefaults.Inventory;
		}

		if (instance == null)
		{
			instance = Globals.LoadInstance<Instance>(className, root);

			if (instance == null)
			{
				PT.Print("Unknown class: " + className);
				return null;
			}

			instance.Name = item.Name;
			instance.AutoInvokeReady = false;
			instance.CallInitOverrides = false;
			instance.SetNetworkParent(parent, force: true);
		}
		else
		{
			// Set to false as properties set will override it.
			instance.CallInitOverrides = false;
		}

		instance.LegacyName = item.Name;
		instance.Root = root;

		// Force Compatibility on old scripts
		if (instance is Datamodel.Script script)
		{
			script.Compatibility = true;
		}

		// Apply properties
		foreach ((PropertyInfo Property, object Value) property in item.Properties)
		{
			try
			{
				object val = property.Value;

				// Handle conversions
				val = (val, property.Property.PropertyType) switch
				{
					// int -> string
					(int intVal, Type t) when t == typeof(string) => intVal.ToString() ?? "",
					// float -> int
					(float floatVal, Type t) when t == typeof(int) => (int)floatVal,
					_ => val
				};
				property.Property.SetValue(instance, val);
			}
			catch (Exception ex)
			{
				PT.PrintErr(property.Property.Name, " to ", property.Value, " set error ", ex);
			}
		}

		// Set use rich text
		if (instance is Text3D text3D)
		{
			if (text3D.Text.Contains('['))
			{
				text3D.UseRichText = true;
			}
		}

		if (instance is UILabel uiLabel)
		{
			if (uiLabel.Text.Contains('['))
			{
				uiLabel.UseRichText = true;
			}
		}

		// Enable use part color on meshes, if Color is not set to default
		if (instance is Datamodel.Mesh m)
		{
			if (!m.Color.IsEqualApprox(new(1, 1, 1)))
			{
				m.UsePartColor = true;
			}
		}

		instance.InvokePropReady();

		foreach (GameItem child in item.Children)
		{
			SpawnItem(child, instance, root);
		}

		return instance;
	}

	public static string ConvertClassName(string className)
	{
		if (className == "ScriptInstance")
		{
			className = "ServerScript";
		}
		else if (className == "LocalScript")
		{
			className = "ClientScript";
		}
		else if (className == "MeshPart")
		{
			className = "Mesh";
		}
		else if (className == "Backpack")
		{
			className = "Inventory";
		}
		else if (className == "Spotlight")
		{
			className = "SpotLight";
		}
		else if (className == "Signal")
		{
			className = "BindableEvent";
		}
		else if (className == "Decal")
		{
			className = "Image3D";
		}
		else if (className == "UIHorizontalLayout")
		{
			className = "UIHLayout";
		}
		else if (className == "UIVerticalLayout")
		{
			className = "UIVLayout";
		}
		else if (className == "Game")
		{
			className = "World";
		}

		return className;
	}
}
