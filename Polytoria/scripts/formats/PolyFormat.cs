// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Data;
using Polytoria.Datamodel.Resources;
using Polytoria.Shared;
using Polytoria.Utils;
using Polytoria.Utils.Compression;
using Polytoria.Utils.DTOs;
using Semver;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Polytoria.Formats;

public static partial class PolyFormat
{
	private static readonly ConditionalWeakTable<Type, Dictionary<string, PropertyInfo>> _propertyCache = new();

	public static object? SerializePropValue(object? propValue)
	{
		if (propValue == null) return null;

		Type propType = propValue.GetType();

		if (propType.IsEnum)
		{
			// Enums serialized as int
			int intValue = Convert.ToInt32(propValue);
			return intValue;
		}

		return propValue;
	}

	public static object? DeserializePropValue(object? data, Type targetType)
	{
		if (data == null) return null;

		if (data is JsonElement jsonElement)
		{
			return DeserializeJsonElement(jsonElement, targetType);
		}

		if (targetType.IsEnum)
		{
			int enumValue = (int)data;
			if (Enum.IsDefined(targetType, enumValue))
			{
				return Enum.ToObject(targetType, enumValue);
			}
		}

		return data;
	}

	private static object? DeserializeJsonElement(JsonElement element, Type targetType)
	{
		if (targetType.IsEnum)
		{
			switch (element.ValueKind)
			{
				case JsonValueKind.String:
					{
						string? enumValue = element.GetString();
						if (enumValue != null && Enum.TryParse(targetType, enumValue, ignoreCase: true, out object? parsed))
						{
							return parsed;
						}
						break;
					}
				case JsonValueKind.Number:
					{
						int enumValue = element.GetInt32();
						if (Enum.IsDefined(targetType, enumValue))
						{
							return Enum.ToObject(targetType, enumValue);
						}
						break;
					}
			}

			PT.PrintErr($"Failed to deserialize enum {targetType.Name} from {element.ValueKind}");
			return null;
		}

		switch (element.ValueKind)
		{
			case JsonValueKind.String:
				if (targetType == typeof(Color))
					return JsonSerializer.Deserialize(element.GetRawText(), PolyJSONGenerationContext.Default.Color);
				break;
			case JsonValueKind.Array:
				// Handle Vector arrays
				if (targetType == typeof(Vector2))
					return JsonSerializer.Deserialize(element.GetRawText(), PolyJSONGenerationContext.Default.Vector2);
				if (targetType == typeof(Vector3))
					return JsonSerializer.Deserialize(element.GetRawText(), PolyJSONGenerationContext.Default.Vector3);
				break;
		}

		try
		{
			JsonTypeInfo? typeInfo = PolyJSONGenerationContext.Default.GetTypeInfo(targetType);
			if (typeInfo != null)
			{
				return JsonSerializer.Deserialize(element.GetRawText(), typeInfo);
			}
			else
			{
				PT.PrintErr($"INTERNAL BUG: PolyFormat No typeinfo for {targetType}");
			}
		}
		catch (Exception ex)
		{
			PT.PrintErr($"INTERNAL BUG: {ex}");
		}

		return null;
	}

	public static PolyRootData ReadRootDataBytes(byte[] content)
	{
		if (content.Length == 0) return new();

		// Deserialize content in another thread
		if (content[0] == 0x7B)
		{
			// Uncompressed
			return JsonSerializer.Deserialize(content, PolyJSONGenerationContext.Default.PolyRootData);
		}
		else
		{
			// Compressed
			return JsonSerializer.Deserialize(ZstdCompressionUtils.Decompress(content), PolyJSONGenerationContext.Default.PolyRootData);
		}
	}

	public static Instance? LoadModel(World root, byte[] content, NetworkedObject? parent = null, PolyObject? overrides = null, PolyLoadContext? globalLoadContext = null, PolyLoadContext? subLoadContext = null)
	{
		PolyRootData data = ReadRootDataBytes(content);
		return LoadModelFromRootData(root, data, parent, overrides, globalLoadContext, subLoadContext);
	}

	public static Instance? LoadModelFromRootData(World root, PolyRootData data, NetworkedObject? parent = null, PolyObject? overrides = null, PolyLoadContext? globalLoadContext = null, PolyLoadContext? subLoadContext = null)
	{
		parent ??= root.TemporaryContainer;
		PolyLoadContext context = new() { RootData = data, Root = root, LoadType = data.FileType, IndexToFile = subLoadContext?.IndexToFile ?? [], AssignModelRoot = globalLoadContext?.AssignModelRoot ?? true };

		if (globalLoadContext != null)
		{
			context.LoadingModelChain = globalLoadContext.LoadingModelChain;
		}

		if (data.FileType == PolyFileType.Model)
		{
			foreach (PolyObject item in data.NonInstanceObjects)
			{
				FromPolyObject(item, context);
			}

			context.IsRootHint = true;

			// No need same name checking on models
			context.DoParentCheck = false;
			context.InsertChild = false;

			PolyObject rootObj = data.Objects[0];

			NetworkedObject? netObj = FromPolyObject(rootObj, context, parent);

			if (netObj is Instance rootInstance)
			{
				if (context.AssignModelRoot)
				{
					context.ModelRoot = rootInstance;
				}
				context.InsertChild = true;

				// Add childs
				foreach (PolyObject child in rootObj.Children)
				{
					FromPolyObject(child, context, netObj);
				}
			}

			if (netObj is Instance i)
			{
				if (overrides != null && globalLoadContext != null)
				{
					LoadEditableOverrides(overrides, i, context, globalLoadContext);
				}
				if (rootObj.LinkedModel != null)
				{
					i.LinkedModel = root.Assets.GetFileLinkByID(rootObj.LinkedModel);
				}
				i.InvokePropReady();
				return i;
			}
			return null;
		}
		else
		{
			throw new NotImplementedException("Unsupported file type");
		}
	}

	public static void InjectModelData(PolyRootData data, Instance to)
	{
		PolyLoadContext context = new() { RootData = data, Root = to.Root, LoadType = data.FileType, ModelRoot = to };

		if (data.FileType == PolyFileType.Model)
		{
			foreach (PolyObject item in data.NonInstanceObjects)
			{
				FromPolyObject(item, context);
			}

			foreach (Instance item in to.GetChildren())
			{
				// Check if is extra
				if (item.ModelRoot == to)
				{
					item.DeleteNow();
				}
			}

			if (data.Objects.Length < 0) return;

			PolyObject targetObj = data.Objects[0];

			// No need same name checking on models
			context.DoParentCheck = false;

			foreach (PolyObject item in targetObj.Children)
			{
				FromPolyObject(item, context, to);
			}
		}
		else
		{
			throw new NotImplementedException("Unsupported file type");
		}
	}

	public static void LoadWorld(World root, byte[] rawdata, bool forceMigrateCords = false)
	{
		// Empty world file
		if (rawdata.Length == 0) return;

		PolyRootData data = ReadRootDataBytes(rawdata);
		InternalLoadWorld(root, data, forceMigrateCords);
	}

	private static void InternalLoadWorld(World root, PolyRootData data, bool forceMigrateCords = false)
	{
		Stopwatch sw = new();
		sw.Start();
		PolyLoadContext context = new() { RootData = data, Root = root, ForceCordMigration = forceMigrateCords };

		// Empty world
		if (data.Objects == null || data.Objects.Length == 0) return;
		if (data.FileType != PolyFileType.World) return;

		PolyObject rootObj = data.Objects[0];
		LoadProperties(rootObj, root, context);

		foreach (PolyObject item in data.NonInstanceObjects)
		{
			FromPolyObject(item, context);
		}

		foreach (PolyObject item in rootObj.Children)
		{
			FromPolyObject(item, context, root);
		}
	}

	public static NetworkedObject? FromPolyObject(PolyObject obj, PolyLoadContext loadContext, NetworkedObject? parent = null)
	{
		string className = ConvertClassName(obj.ClassName);

		// Prevent spawning player
		if (className == "Player") return null;

		bool hasParent;
		bool isModelRoot = loadContext.IsRootHint;
		bool skipChildren = false;

		loadContext.IsRootHint = false;

		NetworkedObject? netObj;
		Instance? existing = null;

		if (parent is Instance preI)
		{
			existing = preI.FindChild(obj.Name);
		}

		if (obj.LinkedModel != null && !isModelRoot)
		{
			if (loadContext.LoadingModelChain.Contains(obj.LinkedModel))
			{
				PT.PrintWarn($"Circular reference detected at {obj.LinkedModel}, model not loaded.");
				return null;
			}

			byte[]? linkedModelData = loadContext.Root.IO.ReadBytesFromID(obj.LinkedModel);
			if (linkedModelData == null)
			{
				PT.PrintErr("Failed to load linked model: ", obj.LinkedModel);
				return null;
			}

			if (!loadContext.LoadedModel.TryGetValue(obj.LinkedModel, out PolyRootData data))
			{
				data = ReadRootDataBytes(linkedModelData);
				loadContext.LoadedModel[obj.LinkedModel] = data;
			}

			loadContext.LoadingModelChain.Add(obj.LinkedModel);

			try
			{
				netObj = LoadModelFromRootData(loadContext.Root, data, parent, obj, loadContext);
			}
			finally
			{
				// Remove from loading chain after loading
				loadContext.LoadingModelChain.Remove(obj.LinkedModel);
			}
			skipChildren = true;
		}
		else
		{
			if (loadContext.DoParentCheck && existing is NetworkedObject existingObj)
			{
				netObj = existingObj;
			}
			else
			{
				netObj = Globals.LoadNetworkedObject(className);
			}
		}

		if (netObj == null)
		{
			PT.PrintWarn("[PF] [WARN] Unknown class: ", className);
			netObj = Globals.LoadNetworkedObject("MissingInstance");
		}

		if (netObj == null)
		{
			PT.PrintWarn("[PF] [WARN] netObj is null");
			return null;
		}

		if (isModelRoot && netObj is Instance objRoot)
		{
			loadContext.ModelRoot = objRoot;
		}

		netObj.Root = loadContext.Root;
		netObj.ObjectID = obj.ID;
		netObj.AutoInvokeReady = false;
		// Set to false as properties set will override it.
		netObj.CallInitOverrides = false;
		netObj.TrySetName(obj.Name);

		if (!isModelRoot && netObj is Instance i)
		{
			i.ModelRoot = loadContext.ModelRoot;
		}

		hasParent = netObj.GetParent() != null;

		if (parent != null && !hasParent)
		{
			netObj.SetNetworkParent(parent, force: true);
			hasParent = true;
		}

		if (parent == null)
		{
			// For non instance children to initialize
			netObj.InitEntry();
		}

		loadContext.SpawnedObjects.TryAdd(obj.ID, netObj);

		// Load properties
		LoadProperties(obj, netObj, loadContext);
		ResolvePendingRef(obj.ID, netObj, loadContext);

		if (loadContext.InsertChild && !skipChildren)
		{
			// Load child
			foreach (PolyObject child in obj.Children)
			{
				FromPolyObject(child, loadContext, netObj);
			}
		}

		if (isModelRoot)
		{
			loadContext.ModelRoot = null;
		}

		if (hasParent && !isModelRoot)
		{
			netObj.InvokePropReady();
		}

		return netObj;
	}

	private static void LoadEditableOverrides(PolyObject obj, Instance rootInstance, PolyLoadContext loadContext, PolyLoadContext globalLoadContext)
	{
		// NOTE: Retrieve via ID doesn't work for some reason
		foreach (PolyObject child in GetDescendants(obj))
		{
			if (rootInstance.FindChild(child.Name) is Instance targetByName)
			{
				// Apply overrides
				LoadProperties(child, targetByName, loadContext);
			}
			else
			{
				PT.PrintWarn($"[PF] [Warn] {child.Name} override doesn't exist");
			}
		}

		loadContext.ModelRoot = null;
		loadContext.InsertChild = true;

		foreach (PolyObject child in obj.Children)
		{
			if (rootInstance.FindChild(child.Name) == null)
			{
				// Add extra children
				FromPolyObject(child, globalLoadContext, rootInstance);
			}
		}
	}

	private static PolyObject[] GetDescendants(PolyObject obj)
	{
		List<PolyObject> i = [];
		i.AddRange(obj.Children);
		foreach (PolyObject item in obj.Children)
		{
			i.AddRange(GetDescendants(item));
		}
		return [.. i];
	}

	private static void QueuePendingRef(string objID, NetworkedObject requester, PropertyInfo prop, PolyLoadContext ctx)
	{
		if (!ctx.PendingReferences.TryGetValue(objID, out var list))
		{
			list = [];
			ctx.PendingReferences[objID] = list;
		}

		requester.PendingProps.Add(prop.Name);
		list.Add((requester, prop));
	}

	private static void ResolvePendingRef(string objID, NetworkedObject resolvedObj, PolyLoadContext ctx)
	{
		if (!ctx.PendingReferences.TryGetValue(objID, out var list))
		{
			return;
		}

		foreach (var (requester, property) in list)
		{
			if (requester.IsDeleted) continue;

			try
			{
				property.SetValue(requester, resolvedObj);
			}
			catch (Exception ex)
			{
				GD.PushError(ex);
			}

			requester.PendingProps.Remove(property.Name);
		}

		ctx.PendingReferences.Remove(objID);
	}

	private static void LoadProperties(PolyObject obj, NetworkedObject netObj, PolyLoadContext loadContext)
	{
		Type dataModelType = netObj.GetType();

		Dictionary<string, PropertyInfo> propertyCache = GetOrCreatePropertyCache(dataModelType);

		foreach (KeyValuePair<string, object?> prop in obj.Properties)
		{
			// Could be deleted mid-way
			if (netObj.IsDeleted) continue;

			string propName = prop.Key;
			object? propVal = prop.Value;

			if (!propertyCache.TryGetValue(propName, out PropertyInfo? property))
			{
				PT.Print("Unknown property: ", dataModelType.Name, ".", propName);
				continue;
			}

			object? val = null;
			Type propType = property.PropertyType;

			if (propType.IsAssignableTo(typeof(FileLinkAsset)))
			{
				string? linkedID = (string?)DeserializePropValue(propVal, typeof(string));
				if (linkedID != null)
				{
					if (loadContext.IndexToFile.TryGetValue(linkedID, out string? pathTo))
					{
						// Create new file link
						val = loadContext.Root.Assets.GetFileLinkByPath(pathTo);
					}
					else
					{
						// Get from ID
						val = loadContext.Root.Assets.GetFileLinkByID(linkedID);
					}
				}
			}
			else if (propType.IsAssignableTo(typeof(NetworkedObject)))
			{
				string? objID = (string?)DeserializePropValue(propVal, typeof(string));

				if (objID == null)
				{
					PT.PrintWarn(propName, " doesn't have ID");
					continue;
				}

				if (loadContext.SpawnedObjects.TryGetValue(objID, out NetworkedObject? refObj))
				{
					// Apply property
					val = refObj;
				}
				else
				{
					// Add prop to pending
					QueuePendingRef(objID, netObj, property, loadContext);
					continue;
				}
			}
			else
			{
				val = DeserializePropValue(propVal, propType);
			}

			string curVer = loadContext.RootData.Version;

			if ((SemVersion.Parse(loadContext.RootData.Version, SemVersionStyles.Any)
				.ComparePrecedenceTo(SemVersion.Parse("2.0.0")) < 0) || loadContext.ForceCordMigration)
			{
				GD.Print("Semver detected, migrating");
				MigrateAxis(propName, ref val);
			}

			try
			{
				property.SetValue(netObj, val);
			}
			catch (Exception ex)
			{
				GD.PushError(ex);
			}
		}
	}

	private static Dictionary<string, PropertyInfo> GetOrCreatePropertyCache(
		[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type type)
	{
		return _propertyCache.GetValue(type, t =>
		{
			Dictionary<string, PropertyInfo> cache = [];
			PropertyInfo[] properties = t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy);
			foreach (PropertyInfo prop in properties)
			{
				if (prop.IsDefined(typeof(EditableAttribute)) || prop.IsDefined(typeof(SaveIncludeAttribute)))
				{
					cache[prop.Name] = prop;
				}
			}
			return cache;
		});
	}

	public static PolyObject? ToPolyObject(NetworkedObject obj, PolyRoot root, bool isLinkedChild = false)
	{
		Dictionary<string, object?> objProps = [];
		Type objType = obj.GetType();

		if (objType.IsDefined(typeof(SaveIgnoreAttribute))) return null;
		if (obj is Instance preI && !preI.Archivable) return null;

		IEnumerable<PropertyInfo> creatorProperties = obj.GetEditableProperties();

		IEnumerable<PropertyInfo> saveIncludes = objType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
			.Where(p => p.GetCustomAttribute<SaveIncludeAttribute>() != null);

		HashSet<string> existingNames = [.. creatorProperties.Select(p => p.Name)];
		creatorProperties = creatorProperties.Concat(
			saveIncludes.Where(p => !existingNames.Contains(p.Name))
		);

		foreach (PropertyInfo prop in creatorProperties)
		{
			if (prop.IsDefined(typeof(Attributes.ObsoleteAttribute))) continue;
			if (prop.IsDefined(typeof(SaveIgnoreAttribute))) continue;
			if (prop.CanRead)
			{
				object? val = prop.GetValue(obj);
				if (val == null) continue;

				DefaultValueAttribute? df = prop.GetCustomAttribute<DefaultValueAttribute>();
				if (df != null)
				{
					try
					{
						object? convertedDefault = Convert.ChangeType(df.DefaultValue, prop.PropertyType);

						if (Equals(convertedDefault, val))
						{
							// Ignore default values
							continue;
						}
					}
					catch (Exception ex)
					{
						// Failed to change type, continue anyway
						PT.PrintErr(ex);
						continue;
					}
				}

				if (val is FileLinkAsset fl)
				{
					// Special handling of filelinks
					val = fl.LinkedID;
				}
				else if (val is NetworkedObject netObj)
				{
					if (netObj is not Instance)
					{
						// Add non instance
						if (!root.NonInstanceAdded.TryGetValue(netObj.ObjectID, out PolyObject? refObj))
						{
							refObj = ToPolyObject(netObj, root);
							if (refObj != null)
							{
								root.NonInstanceObjects.Add(refObj);
								root.NonInstanceAdded.Add(netObj.ObjectID, refObj);
							}
						}
					}
					val = netObj.ObjectID;
				}

				try
				{
					objProps.Add(prop.Name, SerializePropValue(val));
				}
				catch (Exception ex)
				{
					PT.PrintWarn("error when serialize: ", ex);
				}
			}
		}

		List<PolyObject> childs = [];

		PolyObject pobj = new()
		{
			Name = obj.Name,
			ClassName = obj.ClassName,
			ID = obj.ObjectID,
			Properties = objProps,
			IsLinkedChild = isLinkedChild
		};

		if (obj is Instance instance)
		{
			bool includeChildren = true;
			if (instance.LinkedModel != null)
			{
				// Save linked model
				pobj.LinkedModel = instance.LinkedModel.LinkedID;
				if (isLinkedChild)
				{
					includeChildren = false;
				}
			}
			if (includeChildren)
			{
				foreach (Instance child in instance.GetChildren())
				{
					Type ct = child.GetType();
					if (ct.GetCustomAttribute<SaveIgnoreAttribute>() != null) continue;
					if (child.ModelRoot != null && instance.EditableChildren)
					{
						// Save editable children
						PolyObject? cobj = ToPolyObject(child, root, false);
						if (cobj == null) continue;
						cobj.IsLinkedChild = false;
						childs.Add(cobj);
					}
					else if (child.ModelRoot != null && root.FileType == PolyFileType.Model)
					{
						// Save linked children
						PolyObject? cobj = ToPolyObject(child, root, true);
						if (cobj == null) continue;
						cobj.IsLinkedChild = true;
						childs.Add(cobj);
					}
					else if (child.ModelRoot == null)
					{
						// Save children with no model root
						PolyObject? cobj = ToPolyObject(child, root, false);
						if (cobj == null) continue;
						cobj.IsLinkedChild = false;
						childs.Add(cobj);
					}
				}
			}
		}

		pobj.Children = [.. childs];

		return pobj;
	}

	public static PolyRootData SavePlace(World game)
	{
		PolyRoot root = new() { FileType = PolyFileType.World };
		root.Objects.Add(ToPolyObject(game, root)!);

		return root.ToData();
	}

	public static PolyRootData SaveModel(Instance instance)
	{
		if (instance.GetType().IsDefined(typeof(StaticAttribute))) throw new InvalidOperationException("Static class cannot be made as a model");
		PolyRoot root = new() { FileType = PolyFileType.Model };
		root.Objects.Add(ToPolyObject(instance, root)!);

		return root.ToData();
	}

	public static string SavePlaceAsJSON(World game)
	{
		PolyRootData data = SavePlace(game);
		return JsonSerializer.Serialize(data, PolyJSONGenerationContext.Default.PolyRootData);
	}

	public static byte[] SaveCompressedPlaceAsByte(World game)
	{
		return CompressPolyContent(SavePlaceAsJSON(game).ToUtf8Buffer());
	}

	public static byte[] SaveCompressedModelAsByte(Instance instance)
	{
		return CompressPolyContent(SaveModelAsJSON(instance).ToUtf8Buffer());
	}

	public static byte[] CompressPolyContent(byte[] data)
	{
		return ZstdCompressionUtils.Compress(data);
	}

	public static byte[] DecompressPolyContent(byte[] data)
	{
		return ZstdCompressionUtils.Decompress(data);
	}

	public static string SaveModelAsJSON(Instance instance)
	{
		PolyRootData data = SaveModel(instance);
		return JsonSerializer.Serialize(data, PolyJSONGenerationContext.Default.PolyRootData);
	}

	public static void SaveWorldToFile(World game, string path)
	{
		if (File.Exists(path))
		{
			if (!IsPolyFileCompressed(path))
			{
				string data = SavePlaceAsJSON(game);
				File.WriteAllText(path, data);
				return;
			}
		}
		byte[] compressed = SaveCompressedPlaceAsByte(game);
		File.WriteAllBytes(path, compressed);
	}

	public static void SaveModelToFile(Instance instance, string path)
	{
		if (File.Exists(path))
		{
			if (IsPolyFileCompressed(path))
			{
				byte[] compressed = SaveCompressedModelAsByte(instance);
				File.WriteAllBytes(path, compressed);
				return;
			}
		}
		string data = SaveModelAsJSON(instance);
		File.WriteAllText(path, data);
	}

	public static Instance? LoadModelFromFile(World root, string path, NetworkedObject? parent = null)
	{
		byte[] bytes = File.ReadAllBytes(path);
		return LoadModel(root, bytes, parent);
	}

	public static bool IsPolyFileCompressed(string path)
	{
		using FileStream fs = new(path, FileMode.Open, System.IO.FileAccess.Read);
		int f = fs.ReadByte();

		// Empty file
		if (f == -1)
		{
			// Return compressed by default
			return true;
		}

		byte firstByte = (byte)f;

		// Detect "{"
		if (firstByte == 0x7B)
		{
			return false;
		}
		else
		{
			return true;
		}
	}

	public partial struct PolyObjReference
	{
		public string ID { get; set; }
	}

	public partial class PolyObject
	{
		public string Name { get; set; } = "";
		public string ClassName { get; set; } = "";
		public string ID { get; set; } = "";
		public Dictionary<string, object?> Properties { get; set; } = [];
		public PolyObject[] Children { get; set; } = [];
		public string? LinkedModel { get; set; }
		public bool IsLinkedChild { get; set; }
	}

	public partial class PolyLoadContext
	{
		public PolyRootData RootData;
		public World Root = null!;
		public Dictionary<string, NetworkedObject> SpawnedObjects = [];
		public Dictionary<string, List<(NetworkedObject obj, PropertyInfo prop)>> PendingReferences = [];
		public bool IsRootHint = false;
		public Instance? ModelRoot = null;
		public bool AssignModelRoot = true;
		public bool DoParentCheck = true;
		public PolyFileType LoadType = PolyFileType.World;
		public bool InsertChild = true;
		public HashSet<string> LoadingModelChain = [];
		public Dictionary<string, string> IndexToFile = [];
		public Dictionary<string, PolyRootData> LoadedModel = [];
		public bool ForceCordMigration = false;
	}

	public partial class PolyRoot
	{
		public List<PolyObject> Objects = [];
		public List<PolyObject> NonInstanceObjects = [];
		public Dictionary<string, PolyObject> NonInstanceAdded = [];
		public List<KeyValuePair<string, (string objID, string propName)>> PendingReferences = [];
		public PolyFileType FileType;

		public PolyRootData ToData()
		{
			return new()
			{
				Version = Globals.AppVersion,
				FileType = FileType,
				Objects = [.. Objects],
				NonInstanceObjects = [.. NonInstanceObjects]
			};
		}
	}

	public partial struct PolyRootData
	{
		[JsonInclude] public string Version;
		[JsonInclude] public PolyFileType FileType;
		[JsonInclude] public PolyObject[] Objects;
		[JsonInclude] public PolyObject[] NonInstanceObjects;
		[JsonIgnore] public string FolderPath;
	}

	public enum PolyFileType
	{
		World,
		Model
	}

	public static string ConvertClassName(string className)
	{
		if (className == "UIHorizontalLayout")
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
		else if (className == "PhysicalModel")
		{
			className = "RigidBody";
		}

		return className;
	}

	public static void MigrateAxis(string propName, ref object? val)
	{
		if ((propName == nameof(Dynamic.Position) || propName == nameof(Dynamic.LocalPosition) || propName == nameof(Physical.Velocity)) && val is Vector3 v3)
		{
			val = v3.Flip();
		}
		else if ((propName == nameof(Dynamic.Rotation) || propName == nameof(Dynamic.LocalRotation) || propName == nameof(Physical.AngularVelocity)) && val is Vector3 vrot3)
		{
			val = vrot3.FlipEuler();
		}
		else if ((propName == nameof(UIField.Rotation)) && val is float f)
		{
			val = -f;
		}
		else if ((propName == nameof(UIField.PositionRelative) || propName == nameof(UIField.PivotPoint)) && val is Vector2 v2)
		{
			val = new Vector2(v2.X, 1 - v2.Y);
		}
		else if ((propName == nameof(UIField.PositionOffset)) && val is Vector2 vo2)
		{
			val = new Vector2(vo2.X, -vo2.Y);
		}
	}

	[JsonSourceGenerationOptions(WriteIndented = true, Converters = [
		typeof(Vector2JsonConverter),
		typeof(Vector3JsonConverter),
		typeof(ColorJsonConverter),
		typeof(ColorSeriesJsonConverter),
		typeof(NumberRangeJsonConverter)
		], NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals)]
	[JsonSerializable(typeof(string))]
	[JsonSerializable(typeof(bool))]
	[JsonSerializable(typeof(byte))]
	[JsonSerializable(typeof(sbyte))]
	[JsonSerializable(typeof(short))]
	[JsonSerializable(typeof(ushort))]
	[JsonSerializable(typeof(int))]
	[JsonSerializable(typeof(uint))]
	[JsonSerializable(typeof(long))]
	[JsonSerializable(typeof(ulong))]
	[JsonSerializable(typeof(float))]
	[JsonSerializable(typeof(double))]
	[JsonSerializable(typeof(decimal))]

	[JsonSerializable(typeof(Vector2))]
	[JsonSerializable(typeof(Vector3))]
	[JsonSerializable(typeof(Color))]

	[JsonSerializable(typeof(ColorSeries))]
	[JsonSerializable(typeof(NumberRange))]

	[JsonSerializable(typeof(string[]))]
	[JsonSerializable(typeof(byte[]))]
	[JsonSerializable(typeof(PolyRootData))]
	[JsonSerializable(typeof(PolyObject))]
	[JsonSerializable(typeof(PolyObject[]))]
	[JsonSerializable(typeof(List<PolyObject>))]
	[JsonSerializable(typeof(Dictionary<string, object>))]
	internal partial class PolyJSONGenerationContext : JsonSerializerContext
	{
	}
}
