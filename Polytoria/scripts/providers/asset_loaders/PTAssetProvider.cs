// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
#if CREATOR
using Polytoria.Creator.Utils;
#endif
using Polytoria.Shared;
using Polytoria.Shared.AssetLoaders;
using System;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Polytoria.Providers.AssetLoaders;

public class PTAssetProvider : IAssetProvider
{
	private const string RootUrl = Globals.ApiEndpoint + "v1/assets/";
	private const string ServeURL = RootUrl + "serve/";
	private const string ServeMeshURL = RootUrl + "serve-mesh/";
	private const string ServeAudioURL = RootUrl + "serve-audio/";
	private readonly PTHttpClient _client = new();

	public async Task<CacheItem> LoadResource(CacheItem item)
	{
#if CREATOR
		_client.DefaultRequestHeaders["Authorization"] = PolyCreatorAPI.Token;
#endif

		string url = GetAssetServeURL(item.ID, item.Type);

		ServeResponse response = await _client.GetFromJsonAsync(url, ServeResponseGenerationContext.Default.ServeResponse);
		byte[] buffer = await _client.GetByteArrayAsync(response.Url);
		item.SizeBytes = buffer.LongLength;

		item.DirectURL = response.Url;

		switch (item.Type)
		{
			case ResourceType.Mesh:
				{
					GltfDocument document = new();
					GltfState state = new() { CreateAnimations = true };

					document.AppendFromBuffer(buffer, null, state);

					Node3D scene = (Node3D)document.GenerateScene(state);

					// Remove arbitrary nodes that may come with the GLTF (eg. Rigidbodies)
					RemoveNonMeshNodes(scene);

					// Set mipmap texture filter for meshes
					SetMipmapTextureFilter(scene);

					TaskCompletionSource<PackedScene> callback = new();

					Callable.From(() =>
					{
						PackedScene mesh = new();
						mesh.Pack(scene);
						scene.Free();

						callback.SetResult(mesh);
					}).CallDeferred();

					item.Resource = await callback.Task;

					return item;
				}
			case ResourceType.Audio:
				{
					item.Resource = new AudioStreamMP3() { Data = buffer };

					return item;
				}
			case ResourceType.Asset:
			case ResourceType.Decal:
			case ResourceType.AssetThumbnail:
			case ResourceType.PlaceThumbnail:
			case ResourceType.PlaceIcon:
			case ResourceType.UserThumbnail:
			case ResourceType.UserHeadshot:
			case ResourceType.GuildThumbnail:
			case ResourceType.GuildBanner:
				{
					Image image = new();
					image.LoadPngFromBuffer(buffer);
					image.GenerateMipmaps();
					image.FixAlphaEdges();

					if (item.Resize != null)
					{
						image.Resize(item.Resize.Value.X, item.Resize.Value.Y, Image.Interpolation.Lanczos);
					}

					item.Resource = ImageTexture.CreateFromImage(image);

					return item;
				}
			default: throw new NotImplementedException();
		}
	}

	public string GetAssetServeURL(uint id, ResourceType itemType)
	{
		string url = itemType switch
		{
			ResourceType.Mesh => ServeMeshURL + id,
			ResourceType.Asset => ServeURL + id + "/asset",
			ResourceType.Decal => ServeURL + id + "/decal",
			ResourceType.Audio => ServeAudioURL + id,
			ResourceType.AssetThumbnail => ServeURL + id + "/assetThumbnail",
			ResourceType.PlaceThumbnail => ServeURL + id + "/placeThumbnail",
			ResourceType.PlaceIcon => ServeURL + id + "/placeIcon",
			ResourceType.UserThumbnail => ServeURL + id + "/userAvatar",
			ResourceType.UserHeadshot => ServeURL + id + "/userAvatarHeadshot",
			ResourceType.GuildThumbnail => ServeURL + id + "/guildIcon",
			ResourceType.GuildBanner => ServeURL + id + "/guildBanner",
			_ => throw new NotImplementedException()
		};

		return url;
	}

	public void Dispose()
	{
		GC.SuppressFinalize(this);
	}

	private static void RemoveNonMeshNodes(Node node)
	{
		foreach (Node child in node.GetChildren())
		{
			RemoveNonMeshNodes(child); // recurse first

			bool isMesh = child is MeshInstance3D;
			bool isSkeleton = child is Skeleton3D;
			bool isExactNode3D = child.GetType() == typeof(Node3D);
			bool isAnimationPlayer = child is AnimationPlayer;
			bool isAnimationTree = child is AnimationTree;

			if (!isMesh && !isSkeleton && !isExactNode3D && !isAnimationPlayer && !isAnimationTree)
			{
				child.Free();
			}
		}
	}

	private static void SetMipmapTextureFilter(Node node)
	{
		foreach (Node child in node.GetChildren())
		{
			SetMipmapTextureFilter(child);

			if (child is MeshInstance3D meshInstance)
			{
				for (int s = 0; s < meshInstance.Mesh.GetSurfaceCount(); s++)
				{
					if (meshInstance.GetActiveMaterial(s) is BaseMaterial3D material)
					{
						if (material.AlbedoTexture is ImageTexture albedoTex)
						{
							Image img = albedoTex.GetImage();
							img.GenerateMipmaps();
							material.AlbedoTexture = ImageTexture.CreateFromImage(img);
						}

						material.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps;
					}
				}
			}
		}
	}
}

internal struct ServeResponse
{
	[JsonPropertyName("url")]
	public string Url { get; set; }
}

[JsonSerializable(typeof(ServeResponse))]
[JsonSerializable(typeof(string))]
internal partial class ServeResponseGenerationContext : JsonSerializerContext { }
