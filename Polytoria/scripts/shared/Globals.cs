// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
#if CREATOR
using Polytoria.Creator.Properties;
using Polytoria.Datamodel.Creator;
using System.IO;
#endif
using Polytoria.Datamodel;
using Polytoria.Datamodel.Resources;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Mesh = Godot.Mesh;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;

namespace Polytoria.Shared;

public sealed partial class Globals : Node
{
	public const string MainEndpoint = "https://polytoria.com/";
	public const string ApiEndpoint = "https://api.polytoria.com/";
	public const float AlphaThreshold = 0.025f;

	public const string ToolboxFolderName = "toolbox";
#if CREATOR
	public const string ProjectMetaFileName = "project.ptproj";
	public const string ProjectIndexName = "file-lock.json";
	public const string ProjectInputMapName = "input.json";
	public const string ModelFileExtension = "model";
	public static readonly string[] ScriptFileExtensions = ["lua", "luau"];
#endif

	public static Globals Singleton { get; private set; } = null!;
	private const string DatamodelScenesPath = "res://scenes/datamodel/";
#if CREATOR
	private const string PropertiesPath = "res://scenes/creator/properties/";
	private const string SubViewPropertiesPath = "res://scenes/creator/properties/subviews/";
	private const string DatamodelIconsPath = "res://assets/textures/datamodel/";
#endif
	private const string ShapesMeshesPath = "res://resources/shapes/meshes/";
	private const string SkyboxesPath = "res://resources/materials/skyboxes/";
	private const string UIIconsPath = "res://assets/textures/ui-icons/";

	public Globals()
	{
		Singleton = this;
	}

	public readonly static Dictionary<string, PackedScene> CachedScenes = [];
	private static readonly Dictionary<string, PackedScene> _scenesCache = [];
#if CREATOR
	private static readonly Dictionary<string, PackedScene> _propertiesCache = [];
	private static readonly Dictionary<string, PackedScene> _subViewPropertiesCache = [];
#endif
	private static readonly Dictionary<string, Texture2D> _iconsCache = [];
	private static readonly Dictionary<string, Texture2D> _uiIconsCache = [];
	private static readonly Dictionary<string, (Mesh, Shape3D)> _shapesCache = [];
	private static readonly Dictionary<string, Material> _skyboxesCache = [];

	private static Dictionary<(Part.PartMaterialEnum, bool), Material> _materialCache = [];

	private static bool _isExiting = false;

	public static bool IsExiting => _isExiting;

	public const string BuiltInFontLocation = "res://assets/fonts/built-in";
	public const string BuiltInAudioLocation = "res://assets/audio/built-in";
	public const float MobileScale = 2.5f;
	public static string AppVersion { get; private set; } = "";
	public static string MajorAppVersion { get; private set; } = "2";
	public static Node? CurrentAppEntryNode { get; private set; }
	public static AppEntryEnum CurrentAppEntry { get; private set; }

	/// <summary>
	/// Determine RPC logging. "rpclog" can be set in feature flags to turn this on
	/// </summary>
	public static bool UseLogRPC { get; private set; } = false;
	/// <summary>
	/// Determine network stack trace logging in network errors, useful if you want to see where RPC was called from in the origin.
	/// "nettrace" can be set in feature flags to turn this on (only on the error issuer is needed). This do consume a portion of bandwidth
	/// </summary>
	public static bool UseNetTrace { get; private set; } = false;
	/// <summary>
	/// Determine no http mode, Can be used to disable http entirely
	/// "nohttp" can be set in feature flags to turn this on
	/// </summary>
	public static bool UseNoHttp { get; private set; } = false;
	/// <summary>
	/// Determine if node will be enabled, this can be disabled in non Godot environment (eg. unit tests)
	/// </summary>
	public static bool UseNodes { get; set; } = true;
	/// <summary>
	/// Check if is currently running inside Godot Editor
	/// </summary>
	public static bool IsInGDEditor { get; private set; } = false;
	/// <summary>
	/// Check if this build is a beta build
	/// </summary>
	public static bool IsBetaBuild { get; private set; } = false;
	/// <summary>
	/// Check if this build is a server build
	/// </summary>
	public static bool IsServerBuild { get; private set; } = false;
	/// <summary>
	/// Check if this build is a mobile build
	/// </summary>
	public static bool IsMobileBuild { get; private set; } = false;
	/// <summary>
	/// Check if Godot is available, this can be false in unit testing environments
	/// </summary>
	public static bool GDAvailable { get; private set; } = false;

	public static event Action? BeforeQuit;
	public static event Action<InputEvent>? GodotInputEvent;
	public static event Action<double>? GodotProcess;
	public static event Action<double>? GodotPhysicsProcess;
	public static event Action<int>? GodotNotification;

	private readonly static ConditionalWeakTable<string, Type> _typesCache = [];

	static Globals()
	{
		NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(), DllImportResolver);

		// Register asset types
		// TODO: Maybe this could be automated via source generation?
		PTImageAsset.RegisterAsset();
		PTAudioAsset.RegisterAsset();
		PTMeshAsset.RegisterAsset();
		BuiltInAudioAsset.RegisterAsset();
		BuiltInFontAsset.RegisterAsset();
		FileLinkAsset.RegisterAsset();
		GradientImageAsset.RegisterAsset();
		PTMeshAnimationAsset.RegisterAsset();
	}

	public override void _EnterTree()
	{
		UseLogRPC = OS.HasFeature("rpclog");
		UseNetTrace = OS.HasFeature("nettrace");
		UseNoHttp = OS.HasFeature("nohttp");
		IsBetaBuild = OS.HasFeature("beta");
		IsServerBuild = OS.HasFeature("server");
		IsInGDEditor = OS.HasFeature("editor");
		IsMobileBuild = OS.HasFeature("mobile");

		GDAvailable = true;

		AppVersion = (string)ProjectSettings.GetSetting("application/config/version");

#if !PRODUCTION
		AppVersion += "+dev";
#endif

		PT.Print($"Polytoria v{AppVersion}");
		PT.Print("https://polytoria.com/");
		PT.Print("-- System Info --");
		PT.Print("OS Name: ", OS.GetName() + " " + OS.GetVersionAlias());
		PT.Print("Architecture: ", OS.GetProcessorName(), " cores: ", OS.GetProcessorCount());
		PT.Print("Video adapter: ", OS.GetVideoAdapterDriverInfo().Join(", "));
		PT.Print("----");

		GetTree().AutoAcceptQuit = false;
		GetTree().QuitOnGoBack = false;

		// Link with Polytoria's Private API Components
		// NOTE: If you wanted to implement your own, search for "MissingComponentException" to see which part requires it.
#if PT_PRIVATE_API
		Polytoria.Private.PrivateNode pv = new();
		AddChild(pv);
#endif

		// Initialize Native
		try
		{
			NativeBinHelper.Init();
		}
		catch (Exception ex)
		{
			PT.PrintErr("Failure initializing native: ", ex);
		}

#if CREATOR
		string creatorPath = ProjectSettings.GlobalizePath("user://creator");
		if (!Directory.Exists(creatorPath))
		{
			Directory.CreateDirectory(creatorPath);
		}
#endif
	}

	public override void _Process(double delta)
	{
		if (_isExiting) return;
		GodotProcess?.Invoke(delta);
		base._Process(delta);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_isExiting) return;
		GodotPhysicsProcess?.Invoke(delta);
		base._PhysicsProcess(delta);
	}

	public override void _Input(InputEvent @event)
	{
		GodotInputEvent?.Invoke(@event);
		base._Input(@event);
	}

	public static T LoadInstance<T>(World? root = null) where T : Instance
	{
		return (T)LoadNetworkedObject(typeof(T).Name, root)!;
	}

	public static T? LoadInstance<T>(string className, World? root = null) where T : Instance
	{
		return (T?)LoadNetworkedObject(className, root);
	}

	[return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
	private static Type? GetTypeByName(string className)
	{
		if (_typesCache.TryGetValue(className, out Type? t))
			return t;

		string[] namespacesToCheck =
		[
			"Polytoria.Datamodel.",
		"Polytoria.Datamodel.Services.",
		"Polytoria.Datamodel.Creator.",
		"Polytoria.Datamodel.Resources.",
	];

		foreach (string ns in namespacesToCheck)
		{
			t = Type.GetType(ns + className);
			if (t != null)
			{
				_typesCache.AddOrUpdate(className, t);
				return t;
			}
		}
		return null;
	}

	public static NetworkedObject? LoadNetworkedObject(string className, World? root = null)
	{
		Type? type = GetTypeByName(className);
		if (type != null)
		{
			object? obj = Activator.CreateInstance(type);
			if (obj is NetworkedObject netObj)
			{
				netObj.NameOverride = className;
				netObj.Root = root!;
				return netObj;
			}
		}

		return null;
	}

	public static Node? LoadNetworkedObjectScene(string className)
	{
		PackedScene? packedScene = LoadCachedResource(_scenesCache, className, $"{DatamodelScenesPath}{className}.tscn");
		Node? scene = packedScene?.Instantiate<Node>();
		scene?.SceneFilePath = "";
		return scene;
	}

#if CREATOR
	public static IProperty LoadProperty(Type type)
	{
		string cacheToLoad = type.IsEnum ? "Enum" : type.Name;
		if (type.IsAssignableTo(typeof(BaseAsset)))
		{
			cacheToLoad = "BaseAsset";
		}
		else if (type.IsAssignableTo(typeof(Instance)))
		{
			cacheToLoad = "Instance";
		}

		PackedScene packedScene = ForceLoadResource(_propertiesCache, cacheToLoad, $"{PropertiesPath}{cacheToLoad}Property.tscn");
		return packedScene.Instantiate<IProperty>();
	}

	public static IPropertySubview? LoadSubviewProperty(Type type)
	{
		string cacheToLoad = type.Name;
		PackedScene? packedScene = LoadCachedResource(_subViewPropertiesCache, cacheToLoad, $"{SubViewPropertiesPath}{cacheToLoad}Subview.tscn");
		return packedScene?.Instantiate<IPropertySubview>();
	}
#endif

#if CREATOR
	public static Texture2D LoadIcon(string className)
	{
		return LoadCachedTexture(_iconsCache, className, DatamodelIconsPath, "Unknown");
	}
#endif

	public static Texture2D LoadUIIcon(string iconName)
	{
		return LoadCachedTexture(_uiIconsCache, iconName, UIIconsPath, "empty");
	}

	public static (Mesh, Shape3D) LoadShape(string shapeName)
	{
		if (_shapesCache.TryGetValue(shapeName, out (Mesh, Shape3D) cachedShape))
		{
			return cachedShape;
		}

		string path = $"{ShapesMeshesPath}{shapeName}.tres";
		Mesh mesh = ResourceLoader.Load<Mesh>(path, cacheMode: ResourceLoader.CacheMode.IgnoreDeep) ?? throw new KeyNotFoundException($"Shape '{shapeName}' was not found at '{path}'.");
		Shape3D shape = CreateShape(mesh, shapeName);
		(Mesh, Shape3D) loadedShape = (mesh, shape);
		_shapesCache[shapeName] = loadedShape;
		return loadedShape;
	}

	public static Material LoadMaterial(Part.PartMaterialEnum material, float alpha)
	{
		bool isOpaque = alpha >= 1.0f - AlphaThreshold;

		if (_materialCache.TryGetValue((material, isOpaque), out Material? mat))
		{
			return mat;
		}

		mat = ResourceLoader.Load<Material>($"res://resources/materials/parts/{material}.tres", cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
		if (!isOpaque && mat is ShaderMaterial shadMat && shadMat.Shader.ResourcePath.EndsWith("part.gdshader"))
		{
			Shader shader = ResourceLoader.Load<Shader>("res://resources/shaders/part/part_transparent.gdshader", cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
			shadMat.Shader = shader;
		}
		_materialCache[(material, isOpaque)] = mat;
		return mat;
	}

	public static void SetNormalMapsEnabled(bool enabled)
	{
		foreach (var mat in _materialCache.Values)
		{
			if (mat is ShaderMaterial shaderMat)
			{
				shaderMat.SetShaderParameter("use_normal_texture", enabled);
			}
		}
	}

	public static Material LoadSkybox(string materialName)
	{
		return ForceLoadResource(_skyboxesCache, materialName, $"{SkyboxesPath}{materialName}.tres");
	}

	private static TResource? LoadCachedResource<TResource>(Dictionary<string, TResource> cache, string key, string path) where TResource : Resource
	{
		if (cache.TryGetValue(key, out TResource? cachedResource))
		{
			return cachedResource;
		}

		if (!ResourceLoader.Exists(path))
		{
			return null;
		}

		TResource resource = ResourceLoader.Load<TResource>(path, cacheMode: ResourceLoader.CacheMode.IgnoreDeep) ?? throw new InvalidOperationException($"Failed to load resource at '{path}'.");
		cache[key] = resource;
		return resource;
	}

	private static TResource ForceLoadResource<TResource>(Dictionary<string, TResource> cache, string key, string path) where TResource : Resource
	{
		return LoadCachedResource(cache, key, path) ?? throw new KeyNotFoundException($"Resource '{key}' was not found at '{path}'.");
	}

	private static Texture2D LoadCachedTexture(Dictionary<string, Texture2D> cache, string key, string directoryPath, string fallbackKey)
	{
		if (cache.TryGetValue(key, out Texture2D? cachedTexture))
		{
			return cachedTexture;
		}

		string? path = ResolveTexturePath(directoryPath, key);
		if (path == null)
		{
			if (key == fallbackKey)
			{
				throw new KeyNotFoundException($"Texture '{fallbackKey}' was not found in '{directoryPath}'.");
			}

			Texture2D fallbackTexture = LoadCachedTexture(cache, fallbackKey, directoryPath, fallbackKey);
			cache[key] = fallbackTexture;
			return fallbackTexture;
		}

		Texture2D texture = ResourceLoader.Load<Texture2D>(path, cacheMode: ResourceLoader.CacheMode.IgnoreDeep) ?? throw new InvalidOperationException($"Failed to load texture at '{path}'.");
		cache[key] = texture;
		return texture;
	}

	private static string? ResolveTexturePath(string directoryPath, string name)
	{
		string svgPath = $"{directoryPath}{name}.svg";
		if (ResourceLoader.Exists(svgPath))
		{
			return svgPath;
		}

		string pngPath = $"{directoryPath}{name}.png";
		if (ResourceLoader.Exists(pngPath))
		{
			return pngPath;
		}

		return null;
	}

	private static Shape3D CreateShape(Mesh mesh, string shapeName)
	{
		if (shapeName == "Truss" || shapeName == "Frame")
		{
			return new BoxShape3D();
		}

		if (mesh is ArrayMesh)
		{
			ConcavePolygonShape3D concave = new();
			concave.SetFaces(mesh.GetFaces());
			return concave;
		}

		return mesh.CreateConvexShape();
	}

	public static Dictionary<string, string> ReadCmdArgs()
	{
		Dictionary<string, string> result = [];
		string[] args = OS.GetCmdlineArgs();

		for (int i = 0; i < args.Length; i++)
		{
			string arg = args[i];

			if (arg.StartsWith('-'))
			{
				string key = arg.TrimStart('-');
				string value = "";

				// If next arg exists and is not another flag, treat it as value
				if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
				{
					value = args[i + 1];
					i++;
				}

				result[key] = value;
			}
		}

		return result;
	}

	public override void _Notification(int what)
	{
		GodotNotification?.Invoke(what);
		if (what == NotificationWMCloseRequest)
		{
			Quit();
		}
		base._Notification(what);
	}

	public async Task WaitAsync(float time)
	{
		var start = Time.GetTicksUsec();
		var target = start + (ulong)(time * 1_000_000.0);

		while (Time.GetTicksUsec() < target)
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	public async Task WaitFrame()
	{
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	public async Task WaitPhysicsFrame()
	{
		await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
	}

	public async void Quit(bool force = false, int code = 0)
	{
#if CREATOR
		// Request confirmation from interface
		if (CreatorService.Interface != null && !force)
		{
			if (!await CreatorService.Interface.OnQuitRequested()) return;
		}
#endif

		// Starts quit the app
		_isExiting = true;
		await AppCloseDimmer.Show();

		try
		{
			BeforeQuit?.Invoke();
		}
		catch (Exception ex)
		{
			PT.PrintWarn("Error present when quitting: ", ex);
		}
		Callable.From(() =>
		{
			CurrentAppEntryNode?.QueueFree();
			Callable.From(() =>
			{
				GetTree().Quit(code);
			}).CallDeferred();
		}).CallDeferred();
	}

	public enum AppEntryEnum
	{
		Client,
		Creator,
		MobileUI,
		Renderer
	}

	public Node SwitchEntry(AppEntryEnum appEntry)
	{
		PT.Print("Switching entry to: ", appEntry);
		CurrentAppEntryNode?.QueueFree();
		CurrentAppEntry = appEntry;
		Node node = LoadEntry(appEntry);
		CurrentAppEntryNode = node;
		GetNode("/root/").AddChild(node);
		return node;
	}

	public static Node LoadEntry(AppEntryEnum appEntry)
	{
		string sceneToLoad = appEntry switch
		{
			AppEntryEnum.Client => "res://scenes/client/client.tscn",
			AppEntryEnum.Creator => "res://scenes/creator/creator.tscn",
			AppEntryEnum.MobileUI => "res://scenes/mobile/mobile.tscn",
			AppEntryEnum.Renderer => "res://scenes/renderer/renderer.tscn",
			_ => "res://scenes/client/client.tscn",
		};
		string? iconToLoad = appEntry switch
		{
			AppEntryEnum.Client => "client",
			AppEntryEnum.Creator => "creator",
			_ => null
		};

		// Set app icon
		if (iconToLoad != null)
		{
			string platform = "windows";

			if (OS.HasFeature("macos"))
			{
				platform = "mac";
			}

			if (OS.HasFeature("linux"))
			{
				platform = "linux";
			}

			string iconPath = $"res://assets/textures/logo/{iconToLoad}/{platform}.png";
			DisplayServer.SetIcon(GD.Load<Image>(iconPath));
		}

		PT.Print(appEntry, ": Loading Entry scene");
		Node node = CreateInstanceFromScene<Node>(sceneToLoad);
		return node;
	}

	private static IntPtr DllImportResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
	{
		if (!IsInGDEditor)
		{
			return IntPtr.Zero;
		}

		if (IsMobileBuild)
		{
			// Use the mobile default resolver
			return IntPtr.Zero;
		}

		if (!OS.HasFeature("x86_64"))
		{
			if (IsInGDEditor)
			{
				PT.PrintWarn("Unsupported platform for development");
			}
			return IntPtr.Zero;
		}

		string platform = ResolveCurrentPlatform();
		string? dllPath = ResolveDllPath(libraryName, platform);

		if (dllPath == null)
		{
			return IntPtr.Zero;
		}

		return NativeLibrary.Load(dllPath, assembly, searchPath);
	}

	internal static string ResolveCurrentPlatform()
	{
		string platform;

		if (OS.HasFeature("windows"))
		{
			platform = "windows";
		}
		else if (OS.HasFeature("macos"))
		{
			platform = "macos";
		}
		else if (OS.HasFeature("android"))
		{
			platform = "android";
		}
		else
		{
			platform = "linux";
		}

		return platform;
	}

	internal static string? ResolveDllPath(string libraryName, string platform)
	{
		Dictionary<string, string> platformExtensions = new()
		{
			["windows"] = "dll",
			["macos"] = "dylib",
			["linux"] = "so"
		};

		Dictionary<string, string> libraryPaths = new()
		{
			["discord_game_sdk"] = "native/discord",
			["Luau.Compiler"] = "native/Luau.Compiler",
			["Luau.VM"] = "native/Luau.VM",
		};

		if (!libraryPaths.TryGetValue(libraryName, out string? pathb))
		{
			return null;
		}

		if (!IsInGDEditor)
		{
			return $"{libraryName}.{platformExtensions[platform]}";
		}

		if (IsServerBuild)
		{
			return $"native/{platform}/{libraryName}.{platformExtensions[platform]}";
		}
		else
		{
			return $"{pathb}/{platform}/{libraryName}.{platformExtensions[platform]}";
		}
	}

	// Workaround for instance create
	public static T CreateInstanceFromScene<T>(string path) where T : Node
	{
		if (CachedScenes.ContainsKey(path) == false)
		{
			CachedScenes[path] = ResourceLoader.Load<PackedScene>(path, null, ResourceLoader.CacheMode.IgnoreDeep);
		}
		return CachedScenes[path].Instantiate<T>();
	}

	[JsonSerializable(typeof(string))]
	[JsonSerializable(typeof(bool))]
	[JsonSerializable(typeof(int))]
	[JsonSerializable(typeof(object))]
	internal partial class GenericJsonContext : JsonSerializerContext { }
}

public class MissingComponentException(string msg) : Exception(msg) { }
