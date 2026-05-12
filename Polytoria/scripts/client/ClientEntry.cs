// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

#if CREATOR || DEBUG || PT_DOCKER
#define ALLOW_SELFHOST
#endif

using Godot;
using Polytoria.Client.Debugger;
using Polytoria.Client.Settings;
using Polytoria.Client.Settings.Appliers;
using Polytoria.Client.WebAPI;
using Polytoria.Shared.Settings;
#if CREATOR
using Polytoria.Creator.Utils;
#endif
using Polytoria.Datamodel;
using Polytoria.Datamodel.Services;
using Polytoria.Schemas.API;
using Polytoria.Shared;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Polytoria.Client;

public sealed partial class ClientEntry : Node3D
{
	private const int StatusPollIntervalSec = 2;
	private const string LocalTestLogPath = "user://logs/localtest";
	public event Action? NetworkEssentialsReady;
	public event Action? LeaveGameRequested;
	public event Action? TargetServerReady;
	public NetworkService NetworkService { get; private set; } = null!;
	public DatamodelBridge DatamodelBridge { get; private set; } = null!;

	public World Root = null!;
	public bool IsFocused = false;
	public bool IsContained = false;
	public bool IsNetEssentialsReady { get; private set; } = false;
	private readonly List<int> _clientProcesses = [];
	public bool IsSoloTest = false;

	public int TestUserID = 1144;
	public int TestClientCount = 0;
	public bool TestModeReady = false;

	private Timer? _connectTimer;
	private APIClientAuthResponseMessage? _clientConnectData;
#if ALLOW_SELFHOST
	public Vector3? DebugSpawnPos { get; private set; }
#endif

	private string? _debugAddress;
	private int? _debugPort;

	internal DebugAgent? DebugAgent { get; private set; }

	public ClientEntry()
	{
		Root = Globals.LoadInstance<World>();
	}

	public async void Entry(ClientEntryData? data = null)
	{
		// Wait process frame for scene to be ready
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		Stopwatch sw = new();
		sw.Start();
		Dictionary<string, string> cmdargs = Globals.ReadCmdArgs();

		bool isClient = false;
		bool isServer = false;
		bool isSubWorld = cmdargs.ContainsKey("subworld");

		cmdargs.TryGetValue("network", out string? networkMode);
		cmdargs.TryGetValue("entry", out string? pathEntry);
		cmdargs.TryGetValue("token", out string? token);
		cmdargs.TryGetValue("debug", out string? debugAddress);
		cmdargs.TryGetValue("debug-id", out string? debugID);
#if ALLOW_SELFHOST
		cmdargs.TryGetValue("address", out string? connectAddress);
		cmdargs.TryGetValue("world", out string? worldPath);
		cmdargs.TryGetValue("port", out string? portStr);
		cmdargs.TryGetValue("id", out string? testUserID);
		cmdargs.TryGetValue("solo", out string? soloPath);
		cmdargs.TryGetValue("nplr", out string? nPlrStr);
		cmdargs.TryGetValue("spawnpos", out string? spawnPosStr);
		cmdargs.TryGetValue("ctoken", out string? ctoken); // Creator test token

		connectAddress ??= "127.0.0.1";
		portStr ??= "24221";
		nPlrStr ??= "1";

#if PT_DOCKER
		portStr = "7777";
#endif

		int port = portStr.ToInt();
		int nPlr = nPlrStr.ToInt();

		if (testUserID != null)
		{
			TestUserID = int.Parse(testUserID);
		}
#endif
		networkMode ??= "client";

		if (networkMode == "server")
		{
			isServer = true;
		}
		else
		{
			isClient = true;
		}

		if (data != null)
		{
			isClient = !data.Value.TestIsServer ?? true;
			isServer = data.Value.TestIsServer ?? false;
#if ALLOW_SELFHOST
			worldPath = data.Value.TestWorldPath;
			TestUserID = data.Value.TestUserID ?? 1144;
			debugID = data.Value.TestDebugID ?? debugID;
			port = data.Value.ConnectPort ?? port;

			if (data.Value.ConnectAddress != null)
			{
				connectAddress = data.Value.ConnectAddress;
			}
#endif
			if (data.Value.Token != null)
			{
				token = data.Value.Token;
			}
		}

		if (!IsContained)
		{
			IsFocused = true;
		}

#if ALLOW_SELFHOST
		// Debug spawn position
		if (spawnPosStr != null)
		{
			string[] splited = spawnPosStr.TrimStart('v').Split(',');
			DebugSpawnPos = new(int.Parse(splited[0]), int.Parse(splited[1]), int.Parse(splited[2]));
		}

		// If localtest, spawn instance
		if (soloPath != null)
		{
			isClient = false;
			isServer = true;
			IsSoloTest = true;
			worldPath = soloPath;
		}
#endif

		if (debugAddress != null)
		{
			DebugAgent = new();
			sw.Restart();
			PT.Print($"Connecting to debug server {debugAddress}");
			string[] segments = debugAddress.Split(':');
			try
			{
				_debugAddress = segments[0];
				_debugPort = int.Parse(segments[1]);
				await DebugAgent.Start(_debugAddress, _debugPort.Value, debugID);
			}
			catch (Exception ex)
			{
				GD.PushError(ex);
				Globals.Singleton.Quit(true);
			}
			PT.Print($"Debug server connected in {sw.ElapsedMilliseconds}ms");
		}

		// Set landscape for mobile
		if (Globals.IsMobileBuild)
		{
			DisplayServer.ScreenSetOrientation(DisplayServer.ScreenOrientation.Landscape);
			DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
		}

		// Setup essentials 
		ClientSettingsService settings = new()
		{
			Name = "ClientSettings",
			Entry = this
		};
		AddChild(settings, true, InternalMode.Front);

		// Use init flow in case it can be stopped by Rendering device switcher
		settings.Init();

		settings.AddChild(new DisplaySettingsApplier { Name = "DisplaySettingsApplier" }, true, InternalMode.Front);
		settings.AddChild(new AudioSettingsApplier { Name = "AudioSettingsApplier" }, true, InternalMode.Front);
		settings.AddChild(new GraphicsSettingsApplier { Name = GraphicsSettingsApplier.NodeName, Settings = settings }, true, InternalMode.Front);

		DatamodelBridge = new()
		{
			Name = "DatamodelBridge"
		};
		AddChild(DatamodelBridge, true);

		NetworkService networkService = new()
		{
			Name = "NetworkService",
			Entry = this
		};
		NetworkService = networkService;

		networkService.Attach(Root);
		networkService.IsServer = isServer;
		networkService.NetworkParent = Root;

		AddChild(Root.GDNode, true);
		Root.Root = Root;
		Root.Entry = this;
		Root.World3D = GetWorld3D();
		Root.InitEntry();

		DatamodelBridge.Attach(Root);
		World.Current = Root;

		PT.Print("World current setup!");
		IsNetEssentialsReady = true;
		NetworkEssentialsReady?.Invoke();

		sw.Restart();
		Root.Setup();
		PT.Print($"World setup in {sw.ElapsedMilliseconds}ms");

#if CREATOR
		// Set creator token for testing (used for loading unapproved assets made by the user)
		if (ctoken != null)
		{
			PolyCreatorAPI.SetToken(ctoken);
		}
#endif

#if ALLOW_SELFHOST
		// Load the test world for server
		if (isServer)
		{
			if (string.IsNullOrWhiteSpace(pathEntry))
			{
				// null out path entry if null
				pathEntry = null;
			}
			FreeLook freeLook = new() { Name = "FreeLook" };
			Root.GDNode.AddChild(freeLook, false, @internal: Node.InternalMode.Back);

			freeLook.GlobalPosition = new(0, 2, -4);
			freeLook.RotationDegrees = new(-25, 180, 0);

			sw.Restart();

			if (worldPath != null)
			{
				worldPath = ProjectSettings.GlobalizePath(worldPath);
				PT.Print("Loading world with entry: ", pathEntry);
				try
				{
					await DatamodelLoader.LoadWorldFile(Root, worldPath, pathEntry);
				}
				catch (Exception ex)
				{
					PT.PrintErr(ex);
					OS.Alert("World load failed");
					Globals.Singleton.Quit();
					return;
				}
				PT.Print("World Loaded!");
			}
			Root.Environment.CameraOverride = freeLook;

			PT.Print($"World loaded in {sw.ElapsedMilliseconds}ms");
		}

		if (IsSoloTest && !isSubWorld)
		{
			for (int i = 1; i <= nPlr; i++)
			{
				LocalTestStartClient(port);
			}
			TestModeReady = true;

			Globals.BeforeQuit += () =>
			{
				foreach (int pid in _clientProcesses)
				{
					if (OS.IsProcessRunning(pid))
					{
						OS.Kill(pid);
					}
				}
			};
		}
#endif

		PT.Print("World setup!");

		if (OS.HasFeature("lowfps"))
		{
			Engine.MaxFps = 15;
		}
		else if (OS.HasFeature("potatofps"))
		{
			// Activate Potato Mode
			Engine.MaxFps = 2;
		}

		if (isServer)
		{
			if (token != null)
			{
				networkService.IsProd = true;
				PT.Print("Server Authenticating...");
				PolyAuthAPI.SetAuthToken(token);
				PolyServerAPI.SetAuthToken(token);
				Engine.MaxFps = 30;
				try
				{
					Stopwatch sw2 = new();
					sw2.Start();
					PT.Print("Sending listen...");
					APIServerListenResponse listenRes = await PolyAuthAPI.SendServerListen();

					PT.Print("Polytoria Server Info ----");
					PT.Print("Server ID: ", listenRes.ServerID);
					PT.Print("World ID: ", listenRes.WorldID);
					PT.Print("Port: ", listenRes.Port);
					PT.Print("Started at: ", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ"));
					PT.Print("--------------------------");
					Root.WorldID = listenRes.WorldID;
					Root.ServerID = listenRes.ServerID;

					PT.Print("Listen sent ", sw2.ElapsedMilliseconds, "ms");

					PT.Print("Downloading world...");

					sw2.Restart();
					byte[] worldContent = await PolyServerAPI.DownloadWorld(listenRes.WorldID);
					PT.Print("World downloaded in ", sw2.ElapsedMilliseconds, "ms");
					sw2.Restart();
					PT.Print("Constructing...");
					await DatamodelLoader.LoadWorldBytes(Root, worldContent, listenRes.PlacePath);
					PT.Print("Construction finishes in ", sw2.ElapsedMilliseconds, "ms");

					int fport = listenRes.Port;

#if PT_DOCKER
					// Override port on docker
					fport = 7777;
#endif

					networkService.CreateServer(fport);
				}
				catch (Exception ex)
				{
					// Error bruh
					PT.PrintErr(ex.ToString());
					Globals.Singleton.Quit();
				}
			}
			else
			{
#if ALLOW_SELFHOST
				try
				{
					// Start local server
					PT.Print("Starting local server on " + port);
					networkService.CreateServer(port);
					if (DebugAgent != null)
						await DebugAgent.SendServerReady();
				}
				catch (Exception ex)
				{
					GD.PushError(ex);
					OS.Alert("Local host start failure");
					Globals.Singleton.Quit(true);
				}
#endif
			}
		}

		if (isClient)
		{
			if (token != null)
			{
				PT.Print("Connecting to Polytoria...");
				// Request auth to server
				PolyAuthAPI.SetAuthToken(token);

				try
				{
					_clientConnectData = await PolyAuthAPI.SendClientConnect();

					PT.Print("Polytoria Network Info ----");
					PT.Print("World ID: ", _clientConnectData.Value.WorldID);
					PT.Print("Server ID: ", _clientConnectData.Value.ServerID);
					PT.Print("Connected at: ", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ"));
					PT.Print("--------------------------");

					Root.WorldID = _clientConnectData.Value.WorldID;
					Root.ServerID = _clientConnectData.Value.ServerID;
					networkService.IsProd = true;

					_connectTimer = new();
					AddChild(_connectTimer);
					_connectTimer.Timeout += PollServerStatus;
					_connectTimer.Start(StatusPollIntervalSec);
				}
				catch (Exception ex)
				{
					PT.PrintErr(ex);
					networkService.DisconnectSelf(ex.Message, NetworkService.DisconnectionCodeEnum.ConnectionFailure);
				}
			}
#if ALLOW_SELFHOST
			else
			{
				// Local testing
				networkService.CreateClient(connectAddress, port);
			}
#endif
		}
	}

	private async void PollServerStatus()
	{
		if (_connectTimer == null) return;
		if (!_clientConnectData.HasValue) return;

		try
		{
			APIServerStatus status = await PolyAuthAPI.CheckServerStatus();

			PT.Print(status.Status);
			if (status.Status == "started")
			{
				TargetServerReady?.Invoke();
				NetworkService.CreateClient(_clientConnectData.Value.IP, _clientConnectData.Value.Port);
				_connectTimer.QueueFree();
				return;
			}
		}
		catch (Exception ex)
		{
			GD.PushError(ex);
		}

		_connectTimer.Start(StatusPollIntervalSec);
	}

	public override void _UnhandledKeyInput(InputEvent @event)
	{
		if (@event.IsActionPressed("toggle_fullscreen"))
		{
			ClientSettingsService.Instance.Set(SharedSettingKeys.Display.Fullscreen, !ClientSettingsService.Instance.Get<bool>(SharedSettingKeys.Display.Fullscreen));
		}
		base._UnhandledKeyInput(@event);
	}

	public override void _Process(double delta)
	{
		if (IsSoloTest && TestModeReady)
		{
			foreach (int pid in _clientProcesses.ToArray())
			{
				if (!OS.IsProcessRunning(pid))
				{
					_clientProcesses.Remove(pid);
				}
			}

			if (_clientProcesses.Count == 0)
			{
				// Quit the process when all client close
				Globals.Singleton.Quit();
			}
		}
		base._Process(delta);
	}

	public void LeaveGame()
	{
		if (OS.HasFeature("mobile-ui"))
		{
			Globals.Singleton.SwitchEntry(Globals.AppEntryEnum.MobileUI);
		}
		else
		{
			if (!IsContained)
			{
				Globals.Singleton.Quit();
			}
			else
			{
				LeaveGameRequested?.Invoke();
			}
		}
	}

	public void LocalTestStartClient(int port = 24221)
	{
		TestClientCount++;
		int plrID = TestClientCount;
		string exePath = OS.GetExecutablePath();

		string abs = ProjectSettings.GlobalizePath(LocalTestLogPath);

		if (!DirAccess.DirExistsAbsolute(abs))
		{
			DirAccess.MakeDirRecursiveAbsolute(abs);
		}

		string logFilePath = abs.PathJoin(plrID + ".txt");

		List<string> args = ["--windowed", "--log-file", logFilePath, "-network", "client", "-id", plrID.ToString(), "-ltchild", "-port", port.ToString()];

		if (Globals.IsInGDEditor)
		{
			args.AddRange(["--remote-debug", "tcp://127.0.0.1:6007"]);
		}

		if (_debugAddress != null && _debugPort != null)
		{
			args.AddRange(["-debug", $"{_debugAddress}:{_debugPort.Value}"]);
		}

		args.AddRange("--rendering-method", RenderingDeviceSwitcher.GetCurrentDriverName());

		// Ignore rendering method switcher flag, use the same one as creator's
		args.Add("-rmswignore");

		int procID = OS.CreateProcess(exePath, [.. args]);

		_clientProcesses.Add(procID);

		PT.Print($"Started new client process with ID {procID}");
	}

	public override void _ExitTree()
	{
		if (!Globals.IsExiting)
		{
			Root.ForceDelete();
			DatamodelBridge.Free();
		}
		base._ExitTree();
	}

	public struct ClientEntryData
	{
		public string? ConnectAddress;
		public int? ConnectPort;
		public string? Token;
		public int? TestUserID;
		public bool? TestIsServer;
		public string? TestWorldPath;
		public string? TestDebugID;
	}
}
