// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Humanizer;
using Polytoria.Attributes;
using Polytoria.Datamodel.Data;
using Polytoria.Datamodel.Interfaces;
using Polytoria.Datamodel.Resources;
using Polytoria.Formats;
using Polytoria.Networking;
using Polytoria.Networking.Synchronizers;
using Polytoria.Scripting;
using Polytoria.Shared;
using Polytoria.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using static Polytoria.Datamodel.Services.NetworkService;

namespace Polytoria.Datamodel;

[Abstract]
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicProperties | DynamicallyAccessedMemberTypes.NonPublicFields | DynamicallyAccessedMemberTypes.NonPublicMethods)]
public partial class NetworkedObject : IScriptObject
{
	private const float DefaultUnreliableSyncInterval = 1;
	private string _name = "Instance";
	private int _sequence = 0;
	private World _game = null!;
	private bool _isReplicating = true;
	private bool _isDeleted = false;

	private bool _processRegistered = false;
	private bool _physicsProcessRegistered = false;

	private static readonly ConditionalWeakTable<Type, PropertyInfo[]> _editablePropertiesCache = [];
	private static readonly ConditionalWeakTable<Type, PropertyInfo[]> _scriptPropertiesCache = [];
	private static readonly ConditionalWeakTable<Type, PropertyInfo[]> _syncPropertiesCache = [];
	private static readonly ConcurrentDictionary<NetworkedObject, Node> _netObjToProxy = new();
	private static readonly ConcurrentDictionary<Node, NetworkedObject> _proxyToNetObj = new();
	private static readonly ConcurrentDictionary<Type, Dictionary<string, PropertyInfo?>> _syncPropertyByNameCache = new();

	private static readonly Dictionary<Type, Dictionary<string, int>> _typeRpcIdMap = [];
	private static readonly Dictionary<Type, Dictionary<int, MethodInfo>> _typeRpcMethodMap = [];

	private readonly Dictionary<string, double> _lastUnreliableSyncTime = [];
	private NetworkedObject? _networkParent;

	internal bool InvokedEntry = false;
	internal HashSet<NetworkedObject> NonInstanceChildren = [];
	internal readonly Dictionary<string, long> PropertySequence = [];

	public NetworkedObject? NetworkParent
	{
		get => _networkParent;
		set
		{
			if (value == _networkParent) return;
			if (_networkParent != null)
			{
				InvokeExitTree();
				TreeExited.Invoke();
			}
			if (_networkParent != null && this is not Instance)
			{
				_networkParent.NonInstanceChildren.Remove(this);
			}
			if (_networkParent is Instance preI && this is Instance selfpreI)
			{
				selfpreI.RemoveNameFromParent();
				selfpreI.RemoveLegacyNameFromParent();
				preI.Children.Remove(selfpreI);
				preI.ChildRemoved.Invoke(selfpreI);
			}

			UnregisterName();
			_networkParent = value;
			ReenforceName();

			if (_networkParent != null && this is not Instance)
			{
				_networkParent.NonInstanceChildren.Add(this);
			}
			if (_networkParent is Instance postI && this is Instance selfpostI)
			{
				selfpostI.AddNameToParent();
				selfpostI.AddLegacyNameToParent();
				postI.Children.Add(selfpostI);
				selfpostI.Index = postI.Children.Count - 1;
				postI.ChildAdded.Invoke(selfpostI);
			}

			if (_networkParent != null)
			{
				if (!InvokedEntry)
				{
					InitEntry();
				}
				else
				{
					InvokeEnterTree();
				}

				try
				{
					PostReparent();
				}
				catch (Exception ex)
				{
					PT.PrintErr(ex);
				}

				TreeEntered.Invoke();
			}
		}
	}

	public string NetworkPath
	{
		get
		{
			NetworkedObject? instance = this;
			List<string> ancestors = [];
			while (instance != null)
			{
				string name = instance.Name;
				if (instance is not Instance)
				{
					name = "+" + name;
				}
				ancestors.Add(name);
				instance = instance.NetworkParent;
			}
			ancestors.Reverse();
			return string.Join('.', ancestors);
		}
	}

	/// <summary>
	/// Godot node linked with this object
	/// </summary>
	internal Node GDNode = null!;

	/// <summary>
	/// Slot node. This is where Godot child will be added to
	/// </summary>
	internal Node SlotNode = null!;

	[Editable, ScriptProperty, SaveIgnore]
	public string Name
	{
		get => _name;
		set
		{
			if (_name == value)
			{
				return;
			}

			string setto = value;

			if (GetType().IsDefined(typeof(StaticAttribute), false))
				throw new InvalidOperationException($"Cannot set Name on static type '{GetType().Name}'.");
			if (string.IsNullOrWhiteSpace(setto))
				setto = ClassName;
			if (this is Instance preI)
			{
				preI.RemoveNameFromParent();
			}
			UnregisterName();
			_name = EnforceName(setto);
			RegisterName();
			if (this is Instance postI)
			{
				postI.AddNameToParent();
			}
			Renamed.Invoke();
			OnPropertyChanged();
		}
	}

	/// <summary>
	/// NameOverride, allows for setting name directly without restrictions, name will still be enforced
	/// </summary>
	internal string NameOverride
	{
		set
		{
			if (this is Instance preI)
			{
				preI.RemoveNameFromParent();
			}
			_name = value;
			ReenforceName();
			if (this is Instance postI)
			{
				postI.AddNameToParent();
			}
			Renamed.Invoke();
			OnPropertyChanged(nameof(Name));
		}
	}

	[ScriptProperty]
	public string ClassName => GetType().Name;

	[ScriptLegacyProperty("ClassName")]
	public string LegacyClassName => XmlFormat.ConvertClassName(ClassName);

	[ScriptProperty]
	public ScriptSharedTable Shared { get; private set; } = new();

	[IgnoreCleanup]
	public World Root
	{
		get => _game;
		set
		{
			_game = value;
			if (_game != null && _game.Network != null && _game.Network.IsServer)
			{
				_game.RegisterNewNetworkedObject(this);
			}
		}
	}

	public event Action? NetPropertiesReady;
	public event Action? Deleted;

	[ScriptProperty] public PTSignal<string> PropertyChanged { get; private set; } = new();
	[ScriptProperty] public PTSignal Renamed { get; private set; } = new();

	private string _networkedObjectID = "";
	private string _objectID = "";

	[ScriptProperty, CloneIgnore, SaveIgnore]
	public string NetworkedObjectID
	{
		get => _networkedObjectID;
		internal set
		{
			_networkedObjectID = value;
			Root?.RegisterNetworkedObject(this);
		}
	}

	[ScriptProperty, CloneIgnore, SaveIgnore]
	public string ObjectID
	{
		get => _objectID;
		internal set
		{
			_objectID = value;
			Root?.RegisterObject(this);
		}
	}

	private readonly Dictionary<string, object?> _lastSyncedValues = [];
	protected bool isInitialized = false;

	public bool IsDeleted
	{
		get => _isDeleted;
		private set
		{
			_isDeleted = value;
		}
	}

	public bool IsProcessRegistered => _processRegistered;
	public bool IsPhysicsProcessRegistered => _physicsProcessRegistered;

	/// <summary>
	/// Set to false if InvokePropReady is called manually (eg. after properties set)
	/// </summary>
	internal bool AutoInvokeReady { get; set; } = true;

	/// <summary>
	/// Set to false if prop ready should not be called on parent set (eg. when cloning)
	/// </summary>
	internal bool AutoInvokeReadyOnParent { get; set; } = true;

	/// <summary>
	/// Set to false if properties will be overrided, (eg. when loading from saves or cloning)
	/// </summary>
	internal bool CallInitOverrides { get; set; } = true;

	internal bool AutoReplicate { get; set; } = true;

	internal bool ChunkBroadcastOverride { get; set; } = false;

	internal bool DeletedAsChild { get; set; } = false;

	internal int AppliedSequence = 0;

	internal readonly HashSet<string> PendingProps = [];

	internal bool IsPropReady { get; set; } = false;

	internal bool ShouldReplicate = true;
	internal bool ShouldReplicateChild => this is not ServerHidden;

	// If local peer id matches, is server (owns everything), or doesn't exist in server (client owns it's local thing)
	protected bool HasAuthority => Root != null && (Root.Network.LocalPeerID == NetworkAuthority || Root.Network.IsServer || !ExistInNetwork);

	/// <summary>
	/// Check if this object is ready in the network
	/// </summary>
	public bool IsNetworkReady
	{
		get
		{
			if (this is Instance i && i.IsInTemporary) return false;
			return IsPropReady;
		}
	}

	[Export, ScriptProperty]
	public bool ExistInNetwork { get; internal set; } = false;

	[ScriptLegacyProperty("ClientSpawned")]
	public bool ClientSpawned => !ExistInNetwork;

	internal int RemoteSenderId = 0;

	[SyncVar(ServerOnly = true)]
	internal int NetworkAuthority { get; set; } = 1; // Generic network authority

	[SyncVar(ServerOnly = true)]
	internal int NetPropAuthority { get; set; } = 1; // Network authority for changing properties


	internal Dictionary<string, NetworkedObject> UniqueNames = [];

	[ScriptProperty] public PTSignal TreeEntered { get; private set; } = new();
	[ScriptProperty] public PTSignal TreeExited { get; private set; } = new();

	[ScriptProperty] public PTSignal Destroying { get; private set; } = new();

	public NetworkedObject()
	{
		InitializeRpcMethods();

		// Ignore if use node is false
		if (!Globals.UseNodes) return;
		Node n = CreateGDNode();
		OverrideGDNode(n);
		InitGDNode();
	}

	internal void ReenforceName()
	{
		UnregisterName();
		_name = EnforceName(_name);
		RegisterName();
	}

	public bool TrySetName(string value)
	{
		if (GetType().IsDefined(typeof(StaticAttribute), false)
			|| string.IsNullOrWhiteSpace(value))
			return false;

		Name = value;
		return true;
	}

	private string EnforceName(string original)
	{
		original = original.EnforceName();

		if (NetworkParent == null || (this is Instance { IsInTemporary: true }))
			return original;

		Dictionary<string, NetworkedObject> uniqueNames = NetworkParent.UniqueNames;

		// If name is not taken
		if (!uniqueNames.TryGetValue(original, out NetworkedObject? owner) || ReferenceEquals(owner, this))
		{
			return original;
		}

		(string baseName, int startNumber) = GetBaseNameAndNumber(original);

		int lowestAvailable = startNumber > 0 ? startNumber + 1 : 2;

		while (uniqueNames.ContainsKey($"{baseName}{lowestAvailable}"))
		{
			lowestAvailable++;
		}

		return $"{baseName}{lowestAvailable}";
	}

	private static (string baseName, int number) GetBaseNameAndNumber(string name)
	{
		if (string.IsNullOrEmpty(name)) return (name, 0);

		int i = name.Length - 1;
		while (i >= 0 && char.IsDigit(name[i]))
		{
			i--;
		}

		if (i == name.Length - 1) return (name, 0);

		string baseName = name[..(i + 1)];
		string numberPart = name[(i + 1)..];
		int number = int.TryParse(numberPart, out int result) ? result : 0;

		if (number == int.MaxValue)
		{
			number = 1;
		}

		return (baseName, number);
	}

	protected void InitializeRpcMethods()
	{
		Type type = GetType();

		if (_typeRpcIdMap.ContainsKey(type))
			return;

		Dictionary<string, int> nameToId = [];
		Dictionary<int, MethodInfo> idToMethod = [];
		int id = 0;

		Type? currentType = type;
		while (currentType != null)
		{
#pragma warning disable IL2075 // Reflection access is already defined
			foreach (var method in currentType.GetMethods(
				BindingFlags.Public | BindingFlags.NonPublic |
				BindingFlags.Instance | BindingFlags.DeclaredOnly))
			{
				if (method.GetCustomAttribute<NetRpcAttribute>() != null &&
					!nameToId.ContainsKey(method.Name))
				{
					nameToId[method.Name] = id;
					idToMethod[id] = method;
					id++;
				}
			}
#pragma warning restore IL2075 // 'this' argument does not satisfy 'DynamicallyAccessedMembersAttribute' in call to target method. The return value of the source method does not have matching annotations.
			currentType = currentType.BaseType;
		}

		_typeRpcIdMap[type] = nameToId;
		_typeRpcMethodMap[type] = idToMethod;
	}

	private void RegisterName()
	{
		if (NetworkParent == null || string.IsNullOrEmpty(_name)) return;
		NetworkParent.UniqueNames[_name] = this;
	}

	private void UnregisterName()
	{
		if (NetworkParent == null || string.IsNullOrEmpty(_name)) return;

		if (NetworkParent.UniqueNames.TryGetValue(_name, out NetworkedObject? currentOwner)
			&& ReferenceEquals(currentOwner, this))
		{
			NetworkParent.UniqueNames.Remove(_name);
		}
	}

	internal NetworkedObject? GetNetObj(string networkPath)
	{
		if (string.IsNullOrEmpty(networkPath))
		{
			return null;
		}
		string[] pathParts = networkPath.Split('.');
		NetworkedObject current = this;
		int startIndex = 0;

		if (pathParts.Length > 0 && pathParts[0] == current.Name)
		{
			startIndex = 1;
		}

		for (int i = startIndex; i < pathParts.Length; i++)
		{
			if (current is not Instance instance)
			{
				return null;
			}

			string part = pathParts[i];
			NetworkedObject? found;

			if (part.StartsWith('+'))
			{
				// Non-Instance child
				found = instance.FindNonInstanceChild(part[1..]);
			}
			else
			{
				// Regular Instance child
				found = instance.FindChild(part);
			}

			if (found == null)
			{
				return null;
			}
			current = found;
		}
		return current;
	}

	internal T? GetNetObj<T>(string networkPath) where T : NetworkedObject
	{
		return (T?)GetNetObj(networkPath);
	}

	internal string GetPathTo(NetworkedObject target)
	{
		ArgumentNullException.ThrowIfNull(target);

		List<string> pathParts = [];
		NetworkedObject? current = target;

		while (current != null && current != this)
		{
			string name = current.Name;
			if (current is not Instance)
			{
				name = "+" + name;
			}
			pathParts.Add(name);
			current = current.NetworkParent;
		}

		if (current != this)
		{
			throw new InvalidOperationException($"{target.Name} is not a descendant of {Name}");
		}

		pathParts.Reverse();
		return string.Join('.', pathParts);
	}

	public NetworkedObject? FindNonInstanceChild(string name)
	{
		foreach (NetworkedObject child in NonInstanceChildren)
		{
			if (child.Name == name)
			{
				return child;
			}
		}

		return null;
	}

	public static NetworkedObject? GetNetObjFromProxy(Node n)
	{
		if (_proxyToNetObj.TryGetValue(n, out NetworkedObject? nobj)) return nobj;
		return null;
	}

	/// <summary>
	/// Initialize the node, required to be called manually when using Game
	/// </summary>
	public void InitEntry()
	{
		if (InvokedEntry) return;
		InvokedEntry = true;

		// Assign network ID
		if (Root != null && Root.Network != null)
		{
			if (ObjectID == "")
			{
				ObjectID = Guid.NewGuid().ToString();
			}
		}

		if (this is World)
		{
			GDNode?.Name = "GameProxy";
		}
		EnterTreeRecheck();

		if (Globals.UseNodes)
		{
			EnterTree();
			Init();
			InitDefaultValues();

			if (CallInitOverrides)
				InitOverrides();

#if DEBUG
			ValidateProcessRegistration();
#endif

			if (AutoInvokeReady)
			{
				InvokePropReady();
			}
		}

		isInitialized = true;
	}

	private void InvokeDeleted()
	{
		SetProcess(false);
		SetPhysicsProcess(false);

		NonInstanceChildren.Clear();
		PendingProps.Clear();

		try
		{
			CleanupProperties();
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
		}

		try
		{
			PreDelete();
		}
		catch (Exception ex)
		{
			PT.PrintErr("Exception when exiting: ", ex);
		}

		IsDeleted = true;
		Deleted?.Invoke();
		Root?.ReportNetworkObjectExitTree(this);
		Root?.UnregisterNetworkedObject(this);
		Root?.UnregisterObject(this);

		if (_netObjToProxy.Remove(this, out var gdn))
		{
			// Delete Valid Root node only
			if (Node.IsInstanceValid(gdn) && !DeletedAsChild)
			{
				gdn.QueueFree();
				gdn.Dispose();
			}
			_proxyToNetObj.Remove(gdn, out _);
		}
	}

	private sealed record DefaultInit(PropertyInfo Property, object? Value);
	private static readonly ConcurrentDictionary<Type, DefaultInit[]> _defaultInitsCache = new();


	private DefaultInit[] GetDefaultInits()
	{
		Type type = GetType();

		return _defaultInitsCache.GetOrAdd(type, _ =>
		{
			return [.. GetEditableProperties().Select(prop =>
			{
				DefaultValueAttribute? attr = prop.GetCustomAttribute<DefaultValueAttribute>();
				if (attr == null)
					return null;

				object? val = ValidateValue(attr.DefaultValue, prop.PropertyType);
				return new DefaultInit(prop, val);
			}).Where(x => x != null).Cast<DefaultInit>()];
		});
	}

	private static object? ValidateValue(object? raw, Type targetType)
	{
		if (raw == null)
			return null;

		Type type = Nullable.GetUnderlyingType(targetType) ?? targetType;

		if (type.IsInstanceOfType(raw))
			return raw;

		if (type.IsEnum)
		{
			if (raw is string s)
				return Enum.Parse(type, s);

			return Enum.ToObject(type, raw);
		}

		return Convert.ChangeType(raw, type);
	}

	private static object? GetCopy(object? value)
	{
		return value switch
		{
			Array a => a.Clone(),
			_ => value
		};
	}

	/// <summary>
	/// Init default values (editable properties with DefaultValue attribute)
	/// </summary>
	private void InitDefaultValues()
	{
		foreach (var defaults in GetDefaultInits())
		{
			try
			{
				defaults.Property.SetValue(this, GetCopy(defaults.Value));
			}
			catch (Exception ex)
			{
				PT.PrintErr(ex);
			}
		}
	}

	internal static void DebugPrintLeftovers(bool p = false)
	{
		PT.Print("_netObjToProxy: ", _netObjToProxy.Count);
		PT.Print("_proxyToNetObj: ", _proxyToNetObj.Count);
		if (p)
		{
			foreach (var item in _netObjToProxy)
			{
				try
				{
					PT.Print(item.Key, ": ", item.Value);
				}
				catch { }
			}
		}
	}

	private void CleanupProperties()
	{
		foreach (PropertyInfo propInfo in GetScriptProperties())
		{
			if (propInfo.PropertyType == typeof(PTSignal))
			{
				// Disconnect all signals
				object? val = propInfo.GetValue(this);
				if (val is PTSignal signal)
				{
					signal.DisconnectAll();
				}
			}
			else if (propInfo.PropertyType.IsAssignableTo(typeof(NetworkedObject)) || propInfo.PropertyType.IsAssignableTo(typeof(RefCounted)))
			{
				if (propInfo.IsDefined(typeof(IgnoreCleanupAttribute))) continue;
				// Null references to other NetworkedObjects
				try
				{
					propInfo.SetValue(this, null);
				}
				catch { }
			}
		}
	}

	private void InvokeEnterTree()
	{
		if (this is Instance i)
		{
			foreach (Instance item in i.GetChildren())
			{
				item.InvokeEnterTree();
			}
		}

		foreach (NetworkedObject item in NonInstanceChildren)
		{
			item.InvokeEnterTree();
		}

		try
		{
			EnterTreeRecheck();
			EnterTree();
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
		}
	}

	private void InvokeExitTree()
	{
		try
		{
			if (this is Instance i)
			{
				foreach (Instance item in i.GetChildren())
				{
					item.InvokeExitTree();
				}
			}

			foreach (NetworkedObject item in NonInstanceChildren)
			{
				item.InvokeExitTree();
			}

			ExitTree();
		}
		catch (Exception ex)
		{
			GD.PushError(ex);
		}
	}

	/// <summary>
	/// Override this function to initialize the godot node, keep in mind this can be called later if node override is called.
	/// </summary>
	public virtual void InitGDNode() { }

	/// <summary>
	/// Override this function to initialize anything else
	/// </summary>
	public virtual void Init() { }

	/// <summary>
	/// Override this function for overriding properties after default value pass
	/// </summary>
	public virtual void InitOverrides() { }

	/// <summary>
	/// Override this function to deinitialize the object
	/// </summary>
	public virtual void PreDelete() { }

	/// <summary>
	/// Calls when object enters the tree, usually by newly inserted into the tree or is reparented
	/// </summary>
	public virtual void EnterTree() { }

	/// <summary>
	/// Calls when object exit the tree, usually by deletion or is reparented
	/// </summary>
	public virtual void ExitTree() { }

	/// <summary>
	/// Process function
	/// </summary>
	/// <param name="delta"></param>
	public virtual void Process(double delta) { }

	/// <summary>
	/// Physics process function
	/// </summary>
	/// <param name="delta"></param>
	public virtual void PhysicsProcess(double delta) { }

	/// <summary>
	/// Calls when this object has been reparented
	/// </summary>
	public virtual void PostReparent() { }

#if CREATOR
	/// <summary>
	/// Calls when this object has been inserted via creator's insert menu
	/// </summary>
	public virtual void CreatorInserted() { }
#endif

	private void EnterTreeRecheck()
	{
		if (IsDeleted) return;

		// Determine should replicatte
		if (this is Instance i && i.IsDescendantOfClass<ServerHidden>())
		{
			ShouldReplicate = false;
		}
		else
		{
			ShouldReplicate = true;
		}

		if (Root != null && Root.Network != null && IsNetworkReady)
		{
			if (Root.Network.IsServer)
			{
				if (!ChunkBroadcastOverride)
				{
					BroadcastReplicate();
				}
			}
			else
			{
				FlushPendings();
			}
		}

		if (ChunkBroadcastOverride)
		{
			ChunkBroadcastOverride = false;
		}

		if (GDNode != null && NetworkParent != null)
		{
			if (!Node.IsInstanceValid(NetworkParent.SlotNode)) return;
			Node? parent = GDNode.GetParentOrNull<Node>();
			if (parent == null)
			{
				NetworkParent.SlotNode.AddChild(GDNode);
			}
			else if (parent != NetworkParent.SlotNode)
			{
				GDNode.Reparent(NetworkParent.SlotNode);
			}
		}
	}

	/// <summary>
	/// Create GD Node
	/// </summary>
	/// <returns></returns>
	public virtual Node CreateGDNode()
	{
		if (GDNode != null) return GDNode;
		Node? node = Globals.LoadNetworkedObjectScene(ClassName);
		if (node != null)
		{
			return node;
		}
		return new();
	}

	/// <summary>
	/// Emits when properties are ready in network
	/// </summary>
	public virtual void Ready() { }

	internal void InvokePropReady()
	{
		// Flush pending outright (for objects that may exists before, such as SunLight/Camera)
		if (Root != null && Root.Network != null)
		{
			FlushPendings();
		}

		Root?.Network?.ReplicateSync.CountInstanceLoaded(this);

		if (IsPropReady) return;
		IsPropReady = true;

		// Send replication when ready
		if (Root != null)
		{
			if (Root.Network != null)
			{
				if (Root.Network.IsServer && AutoReplicate)
				{
					BroadcastReplicate();
				}
				Root.ReportNetworkObjectEnterTree(this);
			}
		}

		if (this is Instance ie)
		{
			// Invoke for children
			foreach (Instance item in ie.GetChildren())
			{
				item.InvokePropReady();
			}
		}

		NetPropertiesReady?.Invoke();
		Ready();
		Root?.ReportNetworkedObjectReady(this);
	}

	private void FlushPendings()
	{
		Root.Network.ReplicateSync.FlushPendingReplicates(this);
		Root.Network.PropSync.FlushPendingProps(this);
		Root.Network.PropSync.LookForResolvePending(this);

		if (this is Dynamic dyn)
		{
			Root.Network.TransformSync.ApplyPendingTransforms(dyn);
		}
	}

	internal static Node[] GetDescendantsInternal(Node? innerNode = null)
	{
		if (innerNode == null) return [];
		Node scanNode = innerNode;
		List<Node> nodes = [];

		foreach (Node node in scanNode.GetChildren(true))
		{
			nodes.Add(node);

			// Recursively add child instances
			if (node.GetChildCount(true) > 0)
			{
				nodes.AddRange(GetDescendantsInternal(node));
			}
		}

		return [.. nodes];
	}

	internal static Node[] GetNonInstanceDescendants(Node? innerNode = null)
	{
		if (innerNode == null) return [];
		Node scanNode = innerNode;
		List<Node> nodes = [];

		foreach (Node node in scanNode.GetChildren(true))
		{
			if (!node.HasMeta("_n"))
			{
				nodes.Add(node);

				// Recursively add child instances
				if (node.GetChildCount(true) > 0)
				{
					nodes.AddRange(GetDescendantsInternal(node));
				}
			}
		}

		return [.. nodes];
	}

	[ScriptMethod]
	public bool IsA(string className)
	{
		Type? objType = GetType();

		// Walk up the inheritance chain
		while (objType != null)
		{
			if (string.Equals(objType.Name, className, StringComparison.OrdinalIgnoreCase) ||
				string.Equals(objType.FullName, className, StringComparison.OrdinalIgnoreCase))
				return true;

			objType = objType.BaseType;

			if (objType == null || objType == typeof(NetworkedObject))
			{
				break;
			}
		}

		return false;
	}

	[ScriptLegacyMethod("IsA")]
	public bool LegacyIsA(string className)
	{
		return IsA(XmlFormat.ConvertClassName(className));
	}

	protected void OnPropertyChanged([CallerMemberName] string propertyName = "", bool syncToNet = true)
	{
		PropertyChanged.Invoke(propertyName);
		if (syncToNet)
			SyncPropToClients(propertyName);
	}

	protected void SyncPropToClients(string propertyName)
	{
		// If network has not started yet, return
		if (Root == null || Root.Network == null) return;

		// If root is not ready, return
		if (!Root.IsLoaded) return;

		// If during replication process, return
		if (!_isReplicating) return;

		PropertyInfo? prop = GetSyncProperty(propertyName);

		if (prop != null)
		{
			SyncVarAttribute? syncvar = prop.GetCustomAttribute<SyncVarAttribute>();

			bool shouldBroadcast =
				// If no SyncVar at all -> always broadcast
				syncvar == null
				// If SyncVar exists without extra rules -> broadcast
				|| (!syncvar.AllowAuthorWrite && !syncvar.ServerOnly)
				// AllowAuthorWrite rule
				|| (syncvar.AllowAuthorWrite && NetworkAuthority == Root.Network.LocalPeerID)
				// ServerOnly rule
				|| (syncvar.ServerOnly && Root.Network.IsServer)
				// Server rule, server has authority over everything
				|| Root.Network.IsServer;

			bool broadcastUnreliable = false;

			if (syncvar != null)
			{
				// Ignore unreliable rule when syncing from server
				if (syncvar.Unreliable && !Root.Network.IsServer)
				{
					broadcastUnreliable = true;
				}
			}

			object? current = prop.GetValue(this);

			// Check if last sync value is the same, or else skip if unreliable is on
			if (!broadcastUnreliable && _lastSyncedValues.TryGetValue(propertyName, out object? lastValue))
			{
				// Check if last value is null, or is the same as one before
				if ((lastValue == null && current == null) || (lastValue != null && lastValue.Equals(current))) { return; }
			}

			// Update cache
			_lastSyncedValues[propertyName] = current;

			if (shouldBroadcast)
			{
				// Throttle unreliable syncs
				if (broadcastUnreliable && !Root.Network.IsServer)
				{
					double currentTime = Time.GetTicksMsec() / 1000.0;

					if (_lastUnreliableSyncTime.TryGetValue(propertyName, out double lastSyncTime))
					{
						double elapsed = currentTime - lastSyncTime;

						if (elapsed < DefaultUnreliableSyncInterval)
						{
							// Drop the message if throttled
							return;
						}
					}

					_lastUnreliableSyncTime[propertyName] = currentTime;
				}

				if (Root.Network.IsServer)
				{
					// Broadcast to all on server
					BroadcastPropUpdate(prop.Name, current, broadcastUnreliable);
				}
				else
				{
					// Broadcast to server if client
					BroadcastPropUpdateToServer(prop.Name, current, broadcastUnreliable);
				}
			}
		}
	}

	/// <summary>
	/// Get sequence of property name
	/// </summary>
	/// <param name="propertyName"></param>
	/// <returns></returns>
	internal long GetSequenceForProp(string propertyName)
	{
		PropertySequence.TryGetValue(propertyName, out long val);
		val += 1;
		PropertySequence[propertyName] = val;

		return val;
	}

	/// <summary>
	/// Compare sequence with the property
	/// </summary>
	/// <param name="propertyName"></param>
	/// <param name="sequence"></param>
	/// <returns></returns>
	internal bool CompareSequenceForProp(string propertyName, long sequence)
	{
		PropertySequence.TryGetValue(propertyName, out long val);

		if (sequence > val)
		{
			PropertySequence[propertyName] = sequence;
			return true;
		}

		return false;
	}

	private void BroadcastReplicate()
	{
		// wait one frame so it's ready
		Callable.From(() =>
		{
			SendNetReplicate(true);
		}).CallDeferred();
	}

	private void SendNetReplicate(bool isSyncOnce = false)
	{
		if (Root.Network == null) { return; }
		Root.Network.ReplicateSync.SendNetReplicate(this, isSyncOnce);
		ExistInNetwork = true;
	}

	public NetReplicateData GetNetReplicateData()
	{
		_sequence++;
		return new NetReplicateData
		{
			Name = Name,
			ClassName = ClassName,
			Authority = NetworkAuthority,
			Props = GetNetPropReplicateData(),
			NodePath = NetworkPath,
			ParentNodePath = NetworkParent?.NetworkPath ?? "",
			ParentNodeID = NetworkParent?.NetworkedObjectID ?? "",
			NetworkID = NetworkedObjectID,
			Index = this is Instance i ? i.Index : 0,
			Sequence = _sequence
		};
	}

	internal void RecvReplicate(NetReplicateData data)
	{
		string objName = data.Name;
		string className = data.ClassName;
		int authority = data.Authority;
		NetPropReplicateData[] props = data.Props;
		//PT.Print(Root.Network.LocalPeerID, " ", data.nodePath, " on the way");

		NetworkedObject? existingObj = null;

		if (existingObj == null)
		{
			if (this is Instance i)
			{
				existingObj = i.FindChild(objName);
			}
			else
			{
				existingObj = FindNonInstanceChild(objName);
			}
		}

		if (existingObj != null)
		{
			NetworkedObject netObj = existingObj;

			if (data.Sequence < netObj.AppliedSequence) return; // Decline sequence

			netObj.Root = Root;
			netObj.ExistInNetwork = true;
			netObj.AutoInvokeReady = false;
			netObj.NetworkedObjectID = data.NetworkID;
			netObj.AppliedSequence = data.Sequence;
			if (netObj is Instance i)
			{
				i.Index = data.Index;
			}
			netObj.SetNetworkAuthority(authority, false);

			netObj.ApplyNetProps(props, data.IsSyncOnce);
			return;
		}

		NetworkedObject? netobj = Globals.LoadNetworkedObject(className);

		if (netobj == null)
		{
			PT.Print("Unknown class: " + className);
			return;
		}

		netobj.Root = Root;
		netobj.NameOverride = objName;
		netobj.ExistInNetwork = true;
		netobj.NetworkedObjectID = data.NetworkID;
		netobj.AutoInvokeReady = false;
		netobj.AppliedSequence = data.Sequence;
		if (netobj is Instance i2)
		{
			i2.Index = data.Index;
		}
		netobj.SetNetworkAuthority(authority, false);
		netobj.NetworkParent = this;

		netobj.ApplyNetProps(props, data.IsSyncOnce);
	}

	public void SetNetworkAuthority(int peerID, bool recursive = true)
	{
		NetworkAuthority = peerID;
		if (recursive)
		{
			foreach (NetworkedObject item in GetNetworkedChildren())
			{
				item.SetNetworkAuthority(peerID, true);
			}
		}
	}

	internal NetworkedObject[] GetNetworkDescendants()
	{
		List<NetworkedObject> instances = [];

		instances.AddRange(NonInstanceChildren);

		if (this is Instance i)
		{
			foreach (Instance child in i.Children)
			{
				instances.Add(child);
				// Recursively add descendants
				instances.AddRange(child.GetNetworkDescendants());
			}
		}

		return [.. instances];
	}

	internal NetworkedObject[] GetReplicateDescendants()
	{
		List<NetworkedObject> instances = [];

		if (!ShouldReplicateChild) return [];

		instances.AddRange(NonInstanceChildren);

		if (this is Instance i)
		{
			foreach (Instance child in i.Children)
			{
				instances.Add(child);
				// Recursively add descendants
				instances.AddRange(child.GetReplicateDescendants());
			}
		}

		return [.. instances];
	}

	internal NetworkedObject[] GetNetworkedChildren()
	{
		List<NetworkedObject> instances = [];

		if (this is Instance i)
		{
			instances.AddRange(i.Children);
		}
		instances.AddRange(NonInstanceChildren);

		return [.. instances];
	}

	private void BroadcastPropUpdate(string propName, object? propValue, bool unreliable)
	{
		Root.Network.PropSync.BroadcastPropUpdate(this, propName, propValue, unreliable);
	}

	private void BroadcastPropUpdateToServer(string propName, object? propValue, bool unreliable)
	{
		Root.Network.PropSync.BroadcastPropUpdateToServer(this, propName, propValue, unreliable);
	}

	public void NetSendAllPropUpdate(int toPeerId)
	{
		Root.Network.PropSync.NetSendAllPropUpdate(this, toPeerId);
	}

	/// <summary>
	/// Apply network properties after first replication
	/// </summary>
	/// <param name="props"></param>
	/// <param name="isSyncOnce"></param>
	public void ApplyNetProps(NetPropReplicateData[] props, bool isSyncOnce)
	{
		foreach (NetPropReplicateData prop in props)
		{
			try
			{
				RecvPropUpdate(prop.Name, prop.ValueRaw, prop.Sequence);
			}
			catch (Exception ex)
			{
				PT.PrintErr(Name, " ", ex);
			}
		}
		if (isSyncOnce)
		{
			InvokePropReady();
		}
	}

	public NetPropReplicateData[] GetNetPropReplicateData()
	{
		IEnumerable<PropertyInfo> props = GetSyncProperties();

		List<NetPropReplicateData> propData = [];

		foreach (PropertyInfo prop in props)
		{
			object? value = prop.GetValue(this);

			if (value is NetworkedObject nobj)
			{
				value = nobj.GetObjectRef();
				if (value == null)
				{
					continue;
				}
			}

			if (value != null)
			{
				propData.Add(new NetPropReplicateData
				{
					Name = prop.Name,
					ValueRaw = NetworkPropSync.SerializePropValue(value),
					Sequence = GetSequenceForProp(prop.Name)
				});
			}
		}

		return [.. propData];
	}

	public NetPropNetworkedObjectRef? GetObjectRef()
	{
		return new()
		{ NetID = NetworkedObjectID };
	}

	internal PropertyInfo? GetSyncProperty(string propName)
	{
		// Get sync property from cache
		Dictionary<string, PropertyInfo?> nameCache = _syncPropertyByNameCache
			.GetOrAdd(GetType(), type =>
				GetSyncProperties().ToDictionary(p => p.Name, p => (PropertyInfo?)p));

		nameCache.TryGetValue(propName, out PropertyInfo? result);
		return result;
	}

	internal void RecvPropUpdate(string propName, byte[] propValueRaw, long sequence)
	{
		PropertyInfo? prop = GetSyncProperty(propName);

		if (prop == null) return;

		// Check sequence
		if (sequence != -1)
		{
			if (!CompareSequenceForProp(prop.Name, sequence))
			{
				return;
			}
		}

		Type targetType = prop.PropertyType;

		object? value = NetworkPropSync.DeserializePropValue(propValueRaw, targetType);

		// Handle NetworkedObject references
		if (targetType.IsAssignableTo(typeof(NetworkedObject)))
		{
			if (propValueRaw.Length == 0)
			{
				SetPropNoReplicate(prop, null);
				return;
			}

			if (value is NetPropNetworkedObjectRef nref && nref.NetID != null)
			{
				NetworkedObject? refObj = Root.GetNetObjectFromID(nref.NetID);
				if (refObj == null)
				{
					PendingProps.Add(propName);
					NetPropNetworkedObjectRef newRef = nref;
					newRef.TargetProp = prop;
					Root.Network.PropSync.PendingRefs[newRef] = this;
				}
				else
				{
					SetPropNoReplicate(prop, refObj);
				}
			}
			return;
		}

		SetPropNoReplicate(prop, value);
	}

	private void SetPropNoReplicate(PropertyInfo prop, object? value)
	{
		_isReplicating = false;
		try
		{
			_lastSyncedValues[prop.Name] = value;
			prop.SetValue(this, value);
		}
		catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
		{
			// This value cannot be set, ignore (eg. static class names)
		}
		catch (Exception ex)
		{
			GD.PushError(ex);
		}
		PendingProps.Remove(prop.Name);
		_isReplicating = true;
	}

	public T New<T>(NetworkedObject? parent = null) where T : NetworkedObject
	{
		NetworkedObject obj = NewInternal(typeof(T).Name, parent, Root);
		return (T)obj;
	}

	public static NetworkedObject? NewFromScript([ScriptingCaller] Script caller, string className, NetworkedObject? parent = null)
	{
		return NewInternal(className, parent, caller.Root);
	}

	protected static NetworkedObject NewInternal(string className, NetworkedObject? parent = null, World? root = null)
	{
		NetworkedObject netObj = Globals.LoadNetworkedObject(className) ?? throw new Exception(className + " doesn't exist");

		if (!netObj.GetType().IsDefined(typeof(InstantiableAttribute), false))
		{
			netObj.Delete();
			throw new Exception(className + " is not Instantiable");
		}

		if (parent != null)
		{
			netObj.Root = parent.Root;
			netObj.SetNetworkParent(parent);
		}
		else if (root != null)
		{
			netObj.Root = root;
			if (netObj is not BaseAsset)
			{
				netObj.AutoInvokeReady = false;
				netObj.SetNetworkParent(root.TemporaryContainer);
			}
		}

		netObj.Name = className;
		return netObj;
	}

	[ScriptMethod]
	public NetworkedObject Clone(NetworkedObject? parent = null)
	{
		return CloneInternal(parent, true);
	}

	internal NetworkedObject CloneInternal(NetworkedObject? parent = null, bool isRoot = false)
	{
		NetworkedObject clonedRoot = NewInternal(ClassName, null, Root)!;

		// Disable auto replicate on instances
		// NOTE: This could be reworked so it sends a chunk of networked objects.
		if (clonedRoot is Instance pi)
		{
			pi.AutoReplicate = false;
		}

		if (this is Instance ti)
		{
			foreach (Instance item in ti.GetChildren())
			{
				item.CloneInternal(clonedRoot);
			}
		}

		// Don't call init overrides, copy properties will overrides
		clonedRoot.CallInitOverrides = false;

		CopyProperties(this, clonedRoot);

		// Reassign model root
		if (clonedRoot is Instance i && this is Instance si && si.LinkedModel != null)
		{
			foreach (Instance item in i.GetDescendants())
			{
				item.ModelRoot ??= i;
			}
		}

		bool callReady = false;
		if (parent != null)
		{
			// Override invoke ready, we'll call them later in this function
			clonedRoot.AutoInvokeReadyOnParent = false;
			clonedRoot.SetNetworkParent(parent);
			clonedRoot.Name = Name;
			callReady = true;
		}

		if (this is Dynamic d && clonedRoot is Dynamic nd)
		{
			d.CopyTransformTo(nd, asGlobal: isRoot);
		}

		if (callReady)
		{
			// All properties are ready
			clonedRoot.InvokePropReady();
		}

		return clonedRoot;
	}

	public static void CopyProperties(NetworkedObject from, NetworkedObject to)
	{
		to.Root = from.Root;
		Type thisClass = from.GetType();
		Type cloneType = to.GetType();

		IEnumerable<PropertyInfo> creatorProperties = from.GetEditableProperties();

		IEnumerable<PropertyInfo> cloneIncludes = from.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Where(p => p.GetCustomAttribute<CloneIncludeAttribute>() != null);

		creatorProperties = creatorProperties.Concat(cloneIncludes);

		foreach (PropertyInfo prop in creatorProperties)
		{
			if (prop != null && prop.CanWrite && !prop.IsDefined(typeof(CloneIgnoreAttribute)))
			{
				try
				{
					object? val = prop.GetValue(from);

					// Handle assets (ignore filelinks)
					if (val is BaseAsset baseAsset && val is not FileLinkAsset)
					{
						NetworkedObject cloned = baseAsset.Clone();
						prop.SetValue(to, cloned);
					}
					// Handle referenced child
					else if (val is Instance i && from is Instance fi && i.IsDescendantOf(fi))
					{
						string origin = from.GetPathTo(i);
						NetworkedObject? newObj = to.GetNetObj(origin);
						prop.SetValue(to, newObj);
					}
					// Handle IData copy
					else if (val is IData d)
					{
						prop.SetValue(to, d.Clone());
					}
					else
					{
						prop.SetValue(to, val);
					}
				}
				catch (Exception ex)
				{
					GD.PushError(ex);
				}
			}
		}
	}

	public void Rpc(string methodName, params object?[]? args)
	{
		InternalNetMsg netmsg = new()
		{
			BroadcastAll = true,
			Target = ProcessRpcTarget(),
			TargetMethod = GetRpcMethodId(methodName)
		};

		if (args != null)
		{
			foreach (object? arg in args)
			{
				netmsg.AddValue(arg);
			}
		}

		MethodInfo? md = GetRpcMethod(methodName);
		NetRpcAttribute? rpcA = md.GetCustomAttribute<NetRpcAttribute>() ?? throw new NetworkException($"Tried to call Rpc function which is not marked as Rpc ({md.Name})");

		if (Root == null || Root.Network == null || Root.Network.NetInstance == null)
		{
			md.Invoke(this, args);
			return;
		}

		if (rpcA.CallLocal)
		{
			md.Invoke(this, args);
		}

		byte[] msg = netmsg.Serialize();

		if (Globals.UseLogRPC)
		{
			PT.Print($"RPC {methodName} ({msg.Length.Bytes().Kilobytes}kb) ({args?.Length ?? 0} args)");
		}

		if (Root.Network.IsServer)
		{
			Root.Network.NetInstance.BroadcastMessage(msg, rpcA.TransferMode, rpcA.TransferChannel);
		}
		else
		{
			Root.Network.NetInstance.SendMessage(1, msg, rpcA.TransferMode, rpcA.TransferChannel);
		}
	}

	private string ProcessRpcTarget()
	{
		// If this is marked as no sync, use network path instead. as ID will not be available
		if (GetType().IsDefined(typeof(NoSyncAttribute))) return NetworkPath;
		return string.IsNullOrEmpty(NetworkedObjectID) ? NetworkPath : "i:" + NetworkedObjectID;
	}

	public void RpcId(int id, string methodName, params object?[]? args)
	{
		InternalNetMsg netmsg = new()
		{
			BroadcastAll = false,
			Target = ProcessRpcTarget(),
			TargetMethod = GetRpcMethodId(methodName)
		};
		if (args != null)
		{
			foreach (object? arg in args)
			{
				netmsg.AddValue(arg);
			}
		}

		MethodInfo? md = GetRpcMethod(methodName);
		NetRpcAttribute? rpcA = md.GetCustomAttribute<NetRpcAttribute>() ?? throw new NetworkException($"Tried to call Rpc function which is not marked as Rpc ({md.Name})");

		if (Root == null || Root.Network == null || Root.Network.NetInstance == null)
		{
			md.Invoke(this, args);
			return;
		}

		if (rpcA.CallLocal && id == Root.Network.LocalPeerID)
		{
			md.Invoke(this, args);
		}

		if (id == 1 && Root.Network.IsServer) return;

		byte[] msg = netmsg.Serialize();

		if (Globals.UseLogRPC)
		{
			PT.Print($"RPCID {id} {methodName} ({msg.Length.Bytes().Kilobytes}kb) ({args?.Length ?? 0} args)");
		}
		Root.Network.NetInstance.SendMessage(id, msg, rpcA.TransferMode, rpcA.TransferChannel);
	}

	internal MethodInfo GetRpcMethod(string methodName)
	{
		MethodInfo? md = null;
		Type? currentType = GetType();

		// Search up the inheritance hierarchy
		while (currentType != null && md == null)
		{
#pragma warning disable IL2075 // Method reflection access is already defined
			md = currentType.GetMethod(methodName,
				BindingFlags.Public | BindingFlags.NonPublic |
				BindingFlags.Instance | BindingFlags.DeclaredOnly);
#pragma warning restore IL2075

			currentType = currentType.BaseType;
		}

		if (md == null)
			throw new NetworkException($"Target Rpc function doesn't exist ({methodName})");

		return md;
	}

	internal MethodInfo GetRpcMethod(int methodId)
	{
		if (!_typeRpcMethodMap.TryGetValue(GetType(), out var idToMethod))
			throw new Exception($"RPC methods not initialized for type {GetType().Name}");

		if (!idToMethod.TryGetValue(methodId, out var method))
			throw new Exception($"No RPC method found with id '{methodId}'");

		return method;
	}

	internal int GetRpcMethodId(string methodName)
	{
		if (!_typeRpcIdMap.TryGetValue(GetType(), out var nameToId))
			throw new Exception($"RPC methods not initialized for type {GetType().Name}");

		if (!nameToId.TryGetValue(methodName, out int id))
			throw new Exception($"No RPC method found with name '{methodName}'");

		return id;
	}


	[ScriptMethod]
	public async void Destroy(float time = 0f)
	{
		if (time != 0)
		{
			await Globals.Singleton.WaitAsync(time);
		}

		InternalDestroy(false);
	}

	internal void DeleteNow()
	{
		InternalDestroy(false);
	}

	internal void ForceDelete()
	{
		InternalDestroy(true);
	}

	internal void InternalDestroy(bool forceDestroy)
	{
		if (GetType().IsDefined(typeof(StaticAttribute), false) && !forceDestroy) throw new InvalidOperationException("Cannot destroy a static class");
		if (GetType().IsDefined(typeof(InternalAttribute), false) && !forceDestroy) throw new InvalidOperationException("Cannot destroy an internal class");
		if (this is Player && !forceDestroy) throw new InvalidOperationException("Cannot destroy a player, use Kick instead.");
		if (IsDeleted) return;

		Destroying?.Invoke();

		NetworkedObject? parent = NetworkParent;

		if (parent != null && parent is Instance prei)
		{
			prei.ChildDeleting.Invoke(this);
		}

		// Propagate deletion
		foreach (NetworkedObject item in GetNetworkedChildren())
		{
			item.DeletedAsChild = true;
			item.InternalDestroy(forceDestroy);
		}

		InvokeDeleted();
		NetworkParent = null;

		if (parent != null && parent is Instance i)
		{
			i.ChildDeleted.Invoke(this);
		}

		// Send remove command explicitly to make sure it's in order
		if (Root != null && Root.Network != null && Root.Network.IsServer && !DeletedAsChild)
		{
			Root.Network.ReplicateSync.SendNetReplicateRemove(this);
		}
	}

	public NetworkedObject? GetParent()
	{
		return NetworkParent;
	}

	internal void SetNetworkParent(NetworkedObject newParent, bool force = false)
	{
		if (this is Instance ri && newParent is Instance pi)
		{
			if (force)
				ri.ParentOverride = pi;
			else
				ri.Parent = pi;
		}
		else
		{
			NetworkParent = newParent;
		}
	}

	internal void OverrideGDNode(Node to)
	{
		// Ignore if use node is false
		if (!Globals.UseNodes) return;

		if (GDNode != null)
		{
			_netObjToProxy.TryRemove(this, out _);
			_proxyToNetObj.TryRemove(GDNode, out _);
			GDNode.QueueFree();
		}
		GDNode = to;
		SlotNode = to;
		_netObjToProxy[this] = to;
		_proxyToNetObj[to] = this;
		InitGDNode();
	}

	public void SetProcess(bool enabled)
	{
		if (IsDeleted) return;

		if (enabled)
		{
			if (_processRegistered) return;
			Globals.GodotProcess += Process;
			_processRegistered = true;
		}
		else
		{
			if (!_processRegistered) return;
			Globals.GodotProcess -= Process;
			_processRegistered = false;
		}
	}

	public void SetPhysicsProcess(bool enabled)
	{
		if (IsDeleted) return;

		if (enabled)
		{
			if (_physicsProcessRegistered) return;
			Globals.GodotPhysicsProcess += PhysicsProcess;
			_physicsProcessRegistered = true;
		}
		else
		{
			if (!_physicsProcessRegistered) return;
			Globals.GodotPhysicsProcess -= PhysicsProcess;
			_physicsProcessRegistered = false;
		}
	}

	protected byte[] BuildRpcPacket(string methodName, params object?[] args)
	{
		byte[][] msg = new byte[args.Length][];

		for (int i = 0; i < args.Length; i++)
		{
			msg[i] = NetworkPropSync.SerializePropValue(args[i]);
		}

		InternalNetMsg.InternalNetMsgPayload payload = new()
		{
			BroadcastAll = false,
			Target = ProcessRpcTarget(),
			TargetMethod = GetRpcMethodId(methodName),
			ByteArrays = msg,
			OriginSender = 0,
		};

#if DEBUG
		if (Globals.UseNetTrace)
		{
			payload.StackTrace = System.Environment.StackTrace;
		}
#endif

		return SerializeUtils.Serialize(payload);
	}

	[ScriptMethod]
	public void Delete(float time = 0f) => Destroy(time);


	[ScriptMetamethod(ScriptObjectMetamethod.ToString)]
	public static string ToString(NetworkedObject? obj)
	{
		if (obj == null) return "<NetworkedObject>";
		return "<" + obj.ClassName + ":" + obj.Name + ">";
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Eq)]
	public static bool MetamethodEquals(object? a, object? b)
	{
		if (a is not NetworkedObject) return false;
		if (b is not NetworkedObject) return false;
		return ((NetworkedObject)a).NetworkedObjectID.Equals(((NetworkedObject)b).NetworkedObjectID);
	}

	internal IEnumerable<PropertyInfo> GetEditableProperties()
	{
#pragma warning disable IL2070 // Reflection access is already defined
		return _editablePropertiesCache.GetOrAdd(GetType(), static type =>
		[.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy).Where(p => p.IsDefined(typeof(EditableAttribute)))]
		);
#pragma warning restore IL2070 // 'this' argument does not satisfy 'DynamicallyAccessedMembersAttribute' in call to target method. The parameter of method does not have matching annotations.
	}

	internal IEnumerable<PropertyInfo> GetScriptProperties()
	{
#pragma warning disable IL2070 // Reflection access is already defined
		return _scriptPropertiesCache.GetOrAdd(GetType(), static type =>
		[.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy).Where(p => p.IsDefined(typeof(ScriptPropertyAttribute)) || p.IsDefined(typeof(ScriptLegacyPropertyAttribute)))]
		);
#pragma warning restore IL2070 // 'this' argument does not satisfy 'DynamicallyAccessedMembersAttribute' in call to target method. The parameter of method does not have matching annotations.
	}

	internal IEnumerable<PropertyInfo> GetSyncProperties()
	{
#pragma warning disable IL2070 // Reflection access is already defined
		return _syncPropertiesCache.GetOrAdd(GetType(), static type =>
		[.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance| BindingFlags.FlattenHierarchy)
			.Where(p =>
				// Editable or SyncVar, but not NoSync
				(p.IsDefined(typeof(EditableAttribute)) ||
				 p.IsDefined(typeof(SyncVarAttribute))) &&
				!p.IsDefined(typeof(NoSyncAttribute))
			)]
		);
#pragma warning restore IL2070 // 'this' argument does not satisfy 'DynamicallyAccessedMembersAttribute' in call to target method. The parameter of method does not have matching annotations.
	}

	public override bool Equals(object? obj)
	{
		return obj is NetworkedObject item && NetworkedObjectID.Equals(item.NetworkedObjectID);
	}

	public override int GetHashCode()
	{
		return base.GetHashCode();
	}

#if DEBUG
	private void ValidateProcessRegistration()
	{
		const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

		var declaredProcess = GetType().GetMethod(nameof(Process), flags);
		var declaredPhysics = GetType().GetMethod(nameof(PhysicsProcess), flags);

		bool declaresProcess = declaredProcess != null && declaredProcess.DeclaringType != typeof(NetworkedObject);
		bool declaresPhysics = declaredPhysics != null && declaredPhysics.DeclaringType != typeof(NetworkedObject);

		if (declaresProcess && !IsProcessRegistered)
		{
			PT.PrintWarn($"{ClassName} declares Process() but doesn't call SetProcess(true)");
		}

		if (declaresPhysics && !IsPhysicsProcessRegistered)
		{
			PT.PrintWarn($"{ClassName} declares PhysicsProcess() but doesn't call SetPhysicsProcess(true)");
		}
	}
#endif
}
