using Godot;
using Polytoria.Client;
using Polytoria.Client.Settings;
using Polytoria.Client.Settings.Appliers;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Services;
using Polytoria.Formats;
using Polytoria.Shared;
using System;
using System.IO;

namespace Polytoria.DatamodelTest;

public partial class DatamodelTestEntry : Node3D
{
	private const float TestTimeoutSec = 30;
	public World Root = null!;
	public NetworkService NetworkService { get; private set; } = null!;
	public static bool IsTesting { get; private set; } = false;

	public DatamodelTestEntry()
	{
		Root = Globals.LoadInstance<World>();
	}

	public async void Entry()
	{
		// Fallsafe so test doesn't last forever
		PT.CallDeferred(async () =>
		{
			await Globals.Singleton.WaitAsync(TestTimeoutSec);
			Globals.Singleton.Quit(true, 1);
		});

		var cmdargs = Globals.ReadCmdArgs();

		// Setup essentials 
		ClientSettingsService settings = new()
		{
			Name = "ClientSettings"
		};
		AddChild(settings, true, InternalMode.Front);

		// Use init flow in case it can be stopped by Rendering device switcher
		settings.Init();

		settings.AddChild(new DisplaySettingsApplier { Name = "DisplaySettingsApplier" }, true, InternalMode.Front);
		settings.AddChild(new AudioSettingsApplier { Name = "AudioSettingsApplier" }, true, InternalMode.Front);
		settings.AddChild(new GraphicsSettingsApplier { Name = GraphicsSettingsApplier.NodeName, Settings = settings }, true, InternalMode.Front);

		DatamodelBridge bridge = new()
		{
			Name = "DatamodelBridge"
		};
		AddChild(bridge, true);

		NetworkService networkService = new()
		{
			Name = "NetworkService"
		};
		NetworkService = networkService;

		networkService.Attach(Root);
		networkService.IsServer = true;
		networkService.NetworkParent = Root;

		AddChild(Root.GDNode, true);
		Root.Root = Root;
		Root.World3D = GetWorld3D();
		Root.InitEntry();

		bridge.Attach(Root);
		World.Current = Root;

		Root.Setup();

		string tempPath = Path.GetTempPath();
		string placeFilePath = tempPath.PathJoin("pt_test_" + new DateTimeOffset(DateTime.Now).Millisecond + ".zip");

		IsTesting = true;

		await PackedFormat.PackProjectToFile(cmdargs["proj"], placeFilePath);

		PackedFormat.LoadPackedWorldFile(Root, placeFilePath);
		File.Delete(placeFilePath);

		networkService.CreateServer();
	}
}
