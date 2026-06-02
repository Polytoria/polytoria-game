// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Client;
#if CREATOR
using Polytoria.Creator;
using Polytoria.Datamodel.Creator;
using Polytoria.Creator.UI;
#endif
using Polytoria.Datamodel.Services;
using Polytoria.Schemas.API;
using Polytoria.Scripting;
using Polytoria.Shared;
using Polytoria.Utils;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;
using Polytoria.Networking;
using Polytoria.Shared.AssetLoaders;
using System.Collections.Concurrent;

namespace Polytoria.Datamodel;

[Static("world")]
public sealed partial class World : Instance
{
	private const double SyncInterval = 5.0;
	private const double VitalInterval = 3.0;
	private const int SyncSampleCount = 5;

	private double _vitalTimer = 0;

	private decimal _serverTime = 0;

	private decimal _clientTimeOffset = 0;
	private double _lastSyncRequest = 0;
	private uint _nextId = 0;
	private long _vitalAssetMemory = 0;

	private bool _isLegacyWorld = false;

	private readonly Queue<double> _rttSamples = new();
	private double _averageRtt = 0;

	private const int ServerHighLoadThreshold = 10;
	private static World? _current;
	private int _worldID = 0;
	private bool _serverUnderLoad = false;
	private readonly ConcurrentDictionary<string, TaskCompletionSource<NetworkedObject?>> _pendingRequests = [];
	private readonly ConcurrentDictionary<string, TaskCompletionSource<NetworkedObject?>> _pendingReadyRequests = [];

	internal int WorldSessionID = 0;

	public PTSignal Loaded { get; private set; } = new();

	[ScriptProperty]
	public PTSignal<double> Rendered { get; private set; } = new();

	[ScriptProperty]
	public bool IsLocalTest => _worldID == 0;

	public SessionTypeEnum SessionType { get; set; } = SessionTypeEnum.Client;

	// TODO: Server Vitals/world properties doesn't work yet, make it work
	[ScriptProperty, SyncVar]
	public bool IsLegacyWorld
	{
		get => _isLegacyWorld;
		internal set
		{
			_isLegacyWorld = value;
			OnPropertyChanged();
		}
	}

	[SyncVar]
	public long VitalAssetMemory
	{
		get => _vitalAssetMemory;
		set
		{
			_vitalAssetMemory = value;
			OnPropertyChanged();
		}
	}

	public bool IsLoaded { get; private set; }

	public event Action<Instance>? InstanceEnteredTree;
	public event Action<Instance>? InstanceExitingTree;
	public event Action? ClientScriptRunDispatch;
	internal event Action<APIPlaceInfo>? WorldInfoReady;
	internal event Action<APIPlaceMedia[]>? WorldMediaReady;

	public Environment Environment => FindChild<Environment>("Environment")!;
	public Players Players => FindChild<Players>("Players")!;
	public Lighting Lighting => FindChild<Lighting>("Lighting")!;
	public PlayerDefaults PlayerDefaults => FindChild<PlayerDefaults>("PlayerDefaults")!;
	public ScriptService ScriptService => FindChild<ScriptService>("ScriptService")!;
	public Hidden Hidden => FindChild<Hidden>("Hidden")!;
	public ServerHidden ServerHidden => FindChild<ServerHidden>("ServerHidden")!;
	public PlayerGUI PlayerGUI => FindChild<PlayerGUI>("PlayerGUI")!;
	public ChatService Chat => FindChild<ChatService>("Chat")!;
	public InputService Input => FindChild<InputService>("Input")!;
	public FilterService Filter => FindChild<FilterService>("Filter")!;
	public AssetsService Assets => FindChild<AssetsService>("Assets")!;
	public AchievementsService Achievements => FindChild<AchievementsService>("Achievements")!;
	public CoreUIService CoreUI => FindChild<CoreUIService>("CoreUI")!;
	public Stats Stats => FindChild<Stats>("Stats")!;
	public Teams Teams => FindChild<Teams>("Teams")!;
	public DatastoreService Datastore => FindChild<DatastoreService>("Datastore")!;
	public HttpService Http => FindChild<HttpService>("Http")!;
	public InsertService Insert => FindChild<InsertService>("Insert")!;
	public PurchasesService Purchases => FindChild<PurchasesService>("Purchases")!;
	public TweenService Tween => FindChild<TweenService>("Tween")!;
	public CaptureService Capture => FindChild<CaptureService>("Capture")!;
	public PresenceService Presence => FindChild<PresenceService>("Presence")!;
	public PreferencesService Preferences => FindChild<PreferencesService>("Preferences")!;
	public IOService IO => FindChild<IOService>("IO")!;
	public WorldsService Worlds => FindChild<WorldsService>("Worlds")!;
	public SocialService Social => FindChild<SocialService>("Social")!;
#if CREATOR
	public CreatorContextService CreatorContext => FindChild<CreatorContextService>("CreatorContext")!;
#endif
	public Temporary TemporaryContainer => FindChild<Temporary>("Temporary")!;

	internal NetworkService Network { get; set; } = null!;
	internal DatamodelBridge Bridge { get; set; } = null!;

	internal World3D World3D { get; set; } = null!;
#if CREATOR
	internal WorldContainer? Container { get; set; } = null!;

	internal CreatorSession LinkedSession = null!;
	internal string? WorldFilePath;
#endif
	internal Viewport? RootViewport { get; set; }
	internal ClientEntry? Entry { get; set; }

	public static World? Current
	{
		get => _current;
		set
		{
			_current = value;

#if CREATOR
			Menu.Singleton?.SwitchTo(value);
			Explorer.Singleton?.SwitchTo(value);
			Properties.Singleton?.SwitchTo(value);
			FileBrowser.Singleton?.SwitchTo(value?.LinkedSession);

			// Set window title
			if (value != null && value.LinkedSession != null)
			{
				DisplayServer.WindowSetTitle($"{value.LinkedSession.Metadata.ProjectName} - Polytoria Creator v{Globals.AppVersion}");
				CreatorService.CurrentSession = value.LinkedSession;
			}
#endif
		}
	}

	public readonly ConcurrentDictionary<string, NetworkedObject> NetworkObjects = [];
	public readonly ConcurrentDictionary<string, NetworkedObject> Objects = [];

	[ScriptProperty, ScriptLegacyProperty("GameID")]
	public int WorldID
	{
		get => _worldID;
		internal set
		{
			_worldID = value;
			if (_worldID != 0)
			{
				FetchWorldInfo();
			}
		}
	}

	[ScriptProperty]
	public int ServerID { get; internal set; }

	[ScriptProperty]
	public decimal UpTime { get; private set; } = 0;

	[ScriptProperty]
	public decimal ServerTime
	{
		get
		{
			if (Network.IsServer)
			{
				return _serverTime;
			}
			else
			{
				// Client: local time + offset - half RTT to account for network delay
				decimal localTime = Time.GetTicksMsec() / 1000m;
				decimal rttCompensation = (decimal)(_averageRtt / 2.0);
				return localTime + _clientTimeOffset - rttCompensation;
			}
		}
	}

	[ScriptProperty, Attributes.Obsolete("Use Players.PlayersCount instead")]
	public int PlayersConnected => Players.PlayersCount;

	[ScriptProperty]
	public int InstanceCount { get; private set; } = 0;

	[ScriptProperty, Attributes.Obsolete("Use InstanceCount instead")]
	public int LocalInstanceCount => InstanceCount;

	[SyncVar]
	public bool ServerUnderLoad
	{
		get => _serverUnderLoad;
		set
		{
			if (_serverUnderLoad != value)
			{
				_serverUnderLoad = value;
				OnPropertyChanged();
			}
		}
	}

	internal APIPlaceInfo? WorldInfo { get; private set; }
	internal APIPlaceMedia[]? WorldMedia { get; private set; }
	internal int FirstWorldMedia = 0;

	public override void Init()
	{
		base.Init();
		SetProcess(true);
#if CREATOR
		Properties.Singleton?.Insert(this);
#endif
		if (SessionType == SessionTypeEnum.Client)
		{
			RegisterNewNetworkedObject(this, "1");
		}
	}

	public override void PreDelete()
	{
		NetworkObjects.Clear();
		Objects.Clear();
		_pendingReadyRequests.Clear();
		_pendingRequests.Clear();
		base.PreDelete();
	}

	public override void Process(double delta)
	{
		UpTime += (decimal)delta;
		Rendered.Invoke(delta);

		// Clock sync
		if (Network != null)
		{
			if (Network.IsServer)
			{
				// Server: increment authoritative time
				_serverTime += (decimal)delta;

				_vitalTimer += delta;

				if (_vitalTimer > VitalInterval)
				{
					_vitalTimer = 0;
					SyncVitals();
				}
			}
			else if (SessionType == SessionTypeEnum.Client)
			{
				// Client: periodically request sync
				double currentTime = Time.GetTicksMsec() / 1000.0;
				if (currentTime - _lastSyncRequest >= SyncInterval)
				{
					_lastSyncRequest = currentTime;
					RequestClockSync();
				}
			}
		}
		base.Process(delta);
	}

	// update server vital signs
	private void SyncVitals()
	{
		VitalAssetMemory = AssetLoader.Singleton.AssetSizeBytes;

		// Check if under load
		ServerUnderLoad = Engine.GetFramesPerSecond() <= ServerHighLoadThreshold;
	}

	#region Clock Sync

	private void RequestClockSync()
	{
		double clientSendTime = Time.GetTicksMsec() / 1000.0;
		RpcId(1, nameof(NetRecvClockSyncRequest), clientSendTime);
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.Reliable)]
	private void NetRecvClockSyncRequest(double clientSendTime)
	{
		RpcId(RemoteSenderId, nameof(NetRecvSyncResponse), _serverTime, clientSendTime);
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private void NetRecvSyncResponse(decimal serverTime, double clientSendTime)
	{
		double clientReceiveTime = Time.GetTicksMsec() / 1000.0;
		double rtt = clientReceiveTime - clientSendTime;

		// Track RTT samples for averaging
		_rttSamples.Enqueue(rtt);
		if (_rttSamples.Count > SyncSampleCount)
		{
			_rttSamples.Dequeue();
		}

		// Calculate average RTT
		double totalRtt = 0;
		foreach (double sample in _rttSamples)
		{
			totalRtt += sample;
		}
		_averageRtt = totalRtt / _rttSamples.Count;

		// Calculate offset, server time was accurate at (clientSendTime + RTT/2)
		decimal estimatedServerTimeNow = serverTime + (decimal)(rtt / 2.0);
		decimal currentLocalTime = (decimal)clientReceiveTime;

		// Update offset
		_clientTimeOffset = estimatedServerTimeNow - currentLocalTime;
	}

	#endregion

	public void ListenToNetworkService()
	{
		// Fire ready for client/server
		if (Network != null)
		{
			Network.ClientReady += InvokeReady;
			Network.ServerStarted += InvokeReady;
		}
	}

	internal void ReportNetworkObjectEnterTree(NetworkedObject netObj)
	{
		if (netObj is Instance instance)
		{
			InstanceEnteredTree?.Invoke(instance);
			InstanceCount++;
		}
	}

	internal void ReportNetworkObjectExitTree(NetworkedObject netObj)
	{
		if (netObj is Instance instance)
		{
			InstanceExitingTree?.Invoke(instance);
			InstanceCount--;
		}
	}

	public NetworkedObject? GetNetObjectFromID(string networkID)
	{
		return NetworkObjects.TryGetValue(networkID, out NetworkedObject? netObj) ? netObj : null;
	}

	public NetworkedObject? GetObjectFromID(string objID)
	{
		return Objects.TryGetValue(objID, out NetworkedObject? netObj) ? netObj : null;
	}

	/// <summary>
	/// Wait for net object, this will also ensure that it's ready
	/// </summary>
	/// <param name="networkID"></param>
	/// <param name="timeoutMs"></param>
	/// <returns></returns>
	public async Task<NetworkedObject?> WaitForNetObjectAsync(string networkID, int timeoutMs = 5000)
	{
		NetworkedObject? obj = await WaitForNetObjectExistAsync(networkID, timeoutMs);
		if (obj != null)
		{
			await WaitForNetObjectReadyAsync(networkID, timeoutMs);
			return obj;
		}
		else
		{
			return null;
		}
	}

	private Task<NetworkedObject?> WaitForNetObjectExistAsync(string networkID, int timeoutMs = 5000)
	{
		if (NetworkObjects.TryGetValue(networkID, out NetworkedObject? netObj))
			return Task.FromResult<NetworkedObject?>(netObj);

		TaskCompletionSource<NetworkedObject?> tcs = new();
		_pendingRequests[networkID] = tcs;

		CancellationTokenSource cts = new(timeoutMs);
		cts.Token.Register(() =>
		{
			_pendingRequests.Remove(networkID, out _);
			tcs.TrySetResult(null);
		});

		return tcs.Task;
	}

	private Task<NetworkedObject?> WaitForNetObjectReadyAsync(string networkID, int timeoutMs = 5000)
	{
		// Try to get the object first
		if (!NetworkObjects.TryGetValue(networkID, out NetworkedObject? netObj))
			return Task.FromResult<NetworkedObject?>(null);

		if (netObj.IsPropReady)
			return Task.FromResult<NetworkedObject?>(netObj);

		TaskCompletionSource<NetworkedObject?> tcs = new();
		_pendingReadyRequests[networkID] = tcs;

		CancellationTokenSource cts = new(timeoutMs);
		cts.Token.Register(() =>
		{
			if (_pendingReadyRequests.Remove(networkID, out _))
				tcs.TrySetResult(null);
		});

		return tcs.Task;
	}


	internal void RegisterNewNetworkedObject(NetworkedObject netObj, string? forceId = null)
	{
		if (netObj.NetworkedObjectID == "")
		{
			_nextId++;
			string targetId = WorldSessionID + (forceId ?? _nextId.ToString());
			netObj.NetworkedObjectID = targetId;
		}
	}

	internal void RegisterNetworkedObject(NetworkedObject netObj)
	{
		NetworkObjects[netObj.NetworkedObjectID] = netObj;
		if (_pendingRequests.TryGetValue(netObj.NetworkedObjectID, out var tcs))
		{
			_pendingRequests.Remove(netObj.NetworkedObjectID, out _);
			tcs.SetResult(netObj);
		}
	}

	internal void UnregisterNetworkedObject(NetworkedObject netObj)
	{
		NetworkObjects.TryRemove(netObj.NetworkedObjectID, out _);
	}

	internal void RegisterObject(NetworkedObject netObj)
	{
		Objects[netObj.ObjectID] = netObj;
	}

	internal void UnregisterObject(NetworkedObject netObj)
	{
		Objects.TryRemove(netObj.ObjectID, out _);
	}

	internal void ReportNetworkedObjectReady(NetworkedObject netObj)
	{
		if (_pendingReadyRequests.TryGetValue(netObj.NetworkedObjectID, out var tcs))
		{
			_pendingReadyRequests.Remove(netObj.NetworkedObjectID, out _);
			tcs.SetResult(netObj);
		}
	}

	internal void InvokeReady()
	{
		if (IsLoaded) return;
		Callable.From(() =>
		{
			IsLoaded = true;
			Loaded?.Invoke();
		}).CallDeferred();
	}

	private async void FetchWorldInfo()
	{
		WorldInfo = await PolyAPI.GetWorldFromID(WorldID);
		if (WorldInfo.HasValue)
		{
			// Set Window title to game name
			DisplayServer.WindowSetTitle($"{TitleEllipsis(WorldInfo.Value.Name, 50)} - Polytoria v{Globals.AppVersion}");

			WorldInfoReady?.Invoke(WorldInfo.Value);
		}
		WorldMedia = await PolyAPI.GetWorldMedia(WorldID);
		if (WorldMedia != null && WorldMedia.Length != 0)
		{
			FirstWorldMedia = WorldMedia[0].Id;
			WorldMediaReady?.Invoke(WorldMedia);
		}
	}

	private static string TitleEllipsis(string text, int maxLength) =>
	text.Length > maxLength ? string.Concat(text.AsSpan(0, maxLength), "...") : text;


	internal void DispatchClientScriptRun()
	{
		PT.Print("Dispatch Client run");
		ClientScriptRunDispatch?.Invoke();
	}

	/// <summary>
	/// Setup full game hierarchy
	/// </summary>
	/// <returns></returns>
	public World Setup()
	{
		InputService? inputService = FindChild<InputService>("Input");

		if (inputService == null)
		{
			inputService = Globals.LoadInstance<InputService>(Root);
			inputService.NameOverride = "Input";
			inputService.NetworkParent = this;
		}

		AssetsService? assetsService = FindChild<AssetsService>("Assets");

		if (assetsService == null)
		{
			assetsService = Globals.LoadInstance<AssetsService>(Root);
			assetsService.NameOverride = "Assets";
			assetsService.NetworkParent = this;
		}

		Temporary? temporary = FindChild<Temporary>("Temporary");

		if (temporary == null)
		{
			temporary = Globals.LoadInstance<Temporary>(Root);
			temporary.NameOverride = "Temporary";
			temporary.NetworkParent = this;
		}

		// Root classes
		Environment? environment = FindChild<Environment>("Environment");

		if (environment == null)
		{
			environment = Globals.LoadInstance<Environment>(Root);
			environment.NetworkParent = this;
		}

		Lighting? lighting = FindChild<Lighting>("Lighting");

		if (lighting == null)
		{
			lighting = Globals.LoadInstance<Lighting>(Root);
			lighting.NetworkParent = this;
		}

		Players? players = FindChild<Players>("Players");

		if (players == null)
		{
			players = Globals.LoadInstance<Players>(Root);
			players.NetworkParent = this;
		}

		ScriptService? scriptService = FindChild<ScriptService>("ScriptService");

		if (scriptService == null)
		{
			scriptService = Globals.LoadInstance<ScriptService>(Root);
			scriptService.NetworkParent = this;
		}

		Hidden? hidden = FindChild<Hidden>("Hidden");

		if (hidden == null)
		{
			hidden = Globals.LoadInstance<Hidden>(Root);
			hidden.NetworkParent = this;
		}

		ServerHidden? serverHidden = FindChild<ServerHidden>("ServerHidden");

		if (serverHidden == null)
		{
			serverHidden = Globals.LoadInstance<ServerHidden>(Root);
			serverHidden.NetworkParent = this;
		}

		PlayerDefaults? playerDefaults = FindChild<PlayerDefaults>("PlayerDefaults");

		if (playerDefaults == null)
		{
			playerDefaults = Globals.LoadInstance<PlayerDefaults>(Root);
			playerDefaults.NetworkParent = this;
		}

		PlayerGUI? playerGUI = FindChild<PlayerGUI>("PlayerGUI");

		if (playerGUI == null)
		{
			playerGUI = Globals.LoadInstance<PlayerGUI>(Root);
			playerGUI.NetworkParent = this;
		}

		ChatService? chatService = FindChild<ChatService>("Chat");

		if (chatService == null)
		{
			chatService = Globals.LoadInstance<ChatService>(Root);
			chatService.NameOverride = "Chat";
			chatService.NetworkParent = this;
		}

		FilterService? filterService = FindChild<FilterService>("Filter");

		if (filterService == null)
		{
			filterService = Globals.LoadInstance<FilterService>(Root);
			filterService.NameOverride = "Filter";
			filterService.NetworkParent = this;
		}

#if CREATOR
		if (SessionType == SessionTypeEnum.Creator)
		{
			CreatorContextService? creatorContext = FindChild<CreatorContextService>("CreatorContext");
			if (creatorContext == null)
			{
				creatorContext = Globals.LoadInstance<CreatorContextService>(Root);
				creatorContext.NetworkParent = this;
			}
		}
#endif

		AchievementsService? achievementsService = FindChild<AchievementsService>("Achievements");
		if (achievementsService == null)
		{
			achievementsService = Globals.LoadInstance<AchievementsService>(Root);
			achievementsService.NameOverride = "Achievements";
			achievementsService.NetworkParent = this;
		}

		CoreUIService? coreUIService = FindChild<CoreUIService>("CoreUI");
		if (coreUIService == null)
		{
			coreUIService = Globals.LoadInstance<CoreUIService>(Root);
			coreUIService.NameOverride = "CoreUI";
			coreUIService.NetworkParent = this;
		}

		Stats? stats = FindChild<Stats>("Stats");
		if (stats == null)
		{
			stats = Globals.LoadInstance<Stats>(Root);
			stats.NameOverride = "Stats";
			stats.NetworkParent = this;
		}

		Teams? teams = FindChild<Teams>("Teams");
		if (teams == null)
		{
			teams = Globals.LoadInstance<Teams>(Root);
			teams.NameOverride = "Teams";
			teams.NetworkParent = this;
		}

		DatastoreService? datastoreService = FindChild<DatastoreService>("Datastore");
		if (datastoreService == null)
		{
			datastoreService = Globals.LoadInstance<DatastoreService>(Root);
			datastoreService.NameOverride = "Datastore";
			datastoreService.NetworkParent = this;
		}

		HttpService? httpService = FindChild<HttpService>("Http");
		if (httpService == null)
		{
			httpService = Globals.LoadInstance<HttpService>(Root);
			httpService.NameOverride = "Http";
			httpService.NetworkParent = this;
		}

		InsertService? insertService = FindChild<InsertService>("Insert");
		if (insertService == null)
		{
			insertService = Globals.LoadInstance<InsertService>(Root);
			insertService.NameOverride = "Insert";
			insertService.NetworkParent = this;
		}

		PurchasesService? purchasesService = FindChild<PurchasesService>("Purchases");
		if (purchasesService == null)
		{
			purchasesService = Globals.LoadInstance<PurchasesService>(Root);
			purchasesService.NameOverride = "Purchases";
			purchasesService.NetworkParent = this;
		}

		TweenService? tweenService = FindChild<TweenService>("Tween");
		if (tweenService == null)
		{
			tweenService = Globals.LoadInstance<TweenService>(Root);
			tweenService.NameOverride = "Tween";
			tweenService.NetworkParent = this;
		}

		CaptureService? captureService = FindChild<CaptureService>("Capture");
		if (captureService == null)
		{
			captureService = Globals.LoadInstance<CaptureService>(Root);
			captureService.NameOverride = "Capture";
			captureService.NetworkParent = this;
		}

		PresenceService? presenceService = FindChild<PresenceService>("Presence");
		if (presenceService == null)
		{
			presenceService = Globals.LoadInstance<PresenceService>(Root);
			presenceService.NameOverride = "Presence";
			presenceService.NetworkParent = this;
		}

		PreferencesService? preferencesService = FindChild<PreferencesService>("Preferences");
		if (preferencesService == null)
		{
			preferencesService = Globals.LoadInstance<PreferencesService>(Root);
			preferencesService.NameOverride = "Preferences";
			preferencesService.NetworkParent = this;
		}

		IOService? ioService = FindChild<IOService>("IO");
		if (ioService == null)
		{
			ioService = Globals.LoadInstance<IOService>(Root);
			ioService.NameOverride = "IO";
			ioService.NetworkParent = this;
		}

		WorldsService? worldsService = FindChild<WorldsService>("Worlds");
		if (worldsService == null)
		{
			worldsService = Globals.LoadInstance<WorldsService>(Root);
			worldsService.NameOverride = "Worlds";
			worldsService.NetworkParent = this;
		}

		SocialService? socialService = FindChild<SocialService>("Social");
		if (socialService == null)
		{
			socialService = Globals.LoadInstance<SocialService>(Root);
			socialService.NameOverride = "Social";
			socialService.NetworkParent = this;
		}

		// Sub childrens
		Camera? camera = Environment.FindChild<Camera>("Camera");

		if (camera == null)
		{
			camera = Globals.LoadInstance<Camera>(Root);
			camera.NetworkParent = Environment;
		}

		SunLight? sunLight = Lighting.FindChild<SunLight>("SunLight");

		if (sunLight == null)
		{
			sunLight = Globals.LoadInstance<SunLight>(Root);
			sunLight.NetworkParent = Lighting;
		}

		Inventory? inventory = playerDefaults.FindChild<Inventory>("Inventory");

		if (inventory == null)
		{
			inventory = Globals.LoadInstance<Inventory>(Root);
			inventory.NetworkParent = playerDefaults;
		}

		// Sort childrens
		List<Instance> orderedChildren =
		[
			environment,
			lighting,
			players,
			scriptService,
			hidden,
			serverHidden,
			playerDefaults,
			playerGUI,
			chatService,
			inputService,
			assetsService,
			filterService,
			achievementsService,
			coreUIService,
			stats,
			teams,
			datastoreService,
			httpService,
			insertService,
			purchasesService,
			captureService,
			presenceService,
			tweenService,
			preferencesService,
			ioService,
			worldsService,
			socialService,
		];

		var targetPositions = orderedChildren
			.Select((child, index) => (child, index))
			.ToDictionary(x => x.child, x => x.index);
		Instance[] childrenToMove = [.. Children.Where(targetPositions.ContainsKey)];
		Instance[] childrenToKeep = [.. Children.Where(c => !targetPositions.ContainsKey(c))];
		Instance[] sortedMovedChildren = [.. childrenToMove.OrderBy(c => targetPositions[c])];

		// Rebuild the Children list
		Children.Clear();

		int keptIndex = 0;
		for (int i = 0; i < sortedMovedChildren.Length + childrenToKeep.Length; i++)
		{
			Instance? movedChild = sortedMovedChildren.FirstOrDefault(c => targetPositions[c] == i);

			if (movedChild != null)
			{
				Children.Add(movedChild);
			}
			else if (keptIndex < childrenToKeep.Length)
			{
				Children.Add(childrenToKeep[keptIndex]);
				keptIndex++;
			}
		}

		// Add any remaining children at the end
		while (keptIndex < childrenToKeep.Length)
		{
			Children.Add(childrenToKeep[keptIndex]);
			keptIndex++;
		}

		// Update all indices
		for (int i = 0; i < Children.Count; i++)
		{
			Children[i].Index = i;
#if CREATOR
			Explorer.Move(Children[i], i);
#endif
			if (Children[i].GDNode != null)
			{
				GDNode.MoveChild(Children[i].GDNode, i);
			}
		}

		environment.MoveChild(camera, 0);
		lighting.MoveChild(sunLight, 0);
		playerDefaults.MoveChild(inventory, 0);

		environment.CurrentCamera = (Camera)environment.FindChildByClass("Camera")!;

		// Use freelook in creator
#if CREATOR
		if (SessionType == SessionTypeEnum.Creator && CreatorContext?.Freelook != null)
			environment.CurrentCamera = CreatorContext.Freelook;
#endif

		return this;
	}

	public enum SessionTypeEnum
	{
		Client,
		Creator,
		Renderer
	}
}
