// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Text.Json.Serialization;

namespace Polytoria.Schemas.API;

public struct APIUserInfo
{
	[JsonPropertyName("id")]
	public int Id { get; set; }

	[JsonPropertyName("username")]
	public string Username { get; set; }

	[JsonPropertyName("description")]
	public string Description { get; set; }

	[JsonPropertyName("signature")]
	public string Signature { get; set; }

	[JsonPropertyName("thumbnail")]
	public APIUserThumbnail Thumbnail { get; set; }

	[JsonPropertyName("playing")]
	public object Playing { get; set; }

	[JsonPropertyName("netWorth")]
	public int NetWorth { get; set; }

	[JsonPropertyName("placeVisits")]
	public int PlaceVisits { get; set; }

	[JsonPropertyName("profileViews")]
	public int ProfileViews { get; set; }

	[JsonPropertyName("forumPosts")]
	public int ForumPosts { get; set; }

	[JsonPropertyName("assetSales")]
	public int AssetSales { get; set; }

	[JsonPropertyName("membershipType")]
	public string MembershipType { get; set; }

	[JsonPropertyName("isStaff")]
	public bool IsStaff { get; set; }

	[JsonPropertyName("userRoleClass")]
	public string UserRoleClass { get; set; }

	[JsonPropertyName("registeredAt")]
	public DateTime RegisteredAt { get; set; }

	[JsonPropertyName("lastSeenAt")]
	public DateTime LastSeenAt { get; set; }
}

public struct APIUserThumbnail
{
	[JsonPropertyName("avatar")]
	public string Avatar { get; set; }

	[JsonPropertyName("icon")]
	public string Icon { get; set; }
}

public struct APIAvatarResponse
{
	[JsonPropertyName("colors")]
	public APIAvatarBodyColors Colors { get; set; }
	[JsonPropertyName("assets")]
	public APIAvatarAsset[] Assets { get; set; }
	[JsonPropertyName("isDefault")]
	public bool IsDefault { get; set; }
}

public struct APIAvatarAsset
{
	[JsonPropertyName("id")]
	public int ID { get; set; }
	[JsonPropertyName("type")]
	public string Type { get; set; }
	[JsonPropertyName("accessoryType")]
	public string AccessoryType { get; set; }
	[JsonPropertyName("name")]
	public string Name { get; set; }
	[JsonPropertyName("thumbnail")]
	public string Thumbnail { get; set; }
	[JsonPropertyName("path")]
	public string Path { get; set; }
}

public struct APIAvatarBodyColors
{
	[JsonPropertyName("head")]
	public string Head { get; set; }
	[JsonPropertyName("torso")]
	public string Torso { get; set; }
	[JsonPropertyName("leftArm")]
	public string LeftArm { get; set; }
	[JsonPropertyName("rightArm")]
	public string RightArm { get; set; }
	[JsonPropertyName("leftLeg")]
	public string LeftLeg { get; set; }
	[JsonPropertyName("rightLeg")]
	public string RightLeg { get; set; }
}
public struct APILibraryResponse
{
	[JsonPropertyName("meta")]
	public APIMeta Meta { get; set; }

	[JsonPropertyName("data")]
	public APILibraryItem[] Data { get; set; }
}

public struct APITokenDataResponse
{
	[JsonPropertyName("success")]
	public bool? Success { get; set; }

	[JsonPropertyName("token")]
	public string Token { get; set; }

	[JsonPropertyName("userID")]
	public uint UserID { get; set; }

	[JsonPropertyName("placeID")]
	public uint PlaceID { get; set; }
}

public struct APILibraryItem
{
	[JsonPropertyName("id")]
	public uint ID { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("thumbnailUrl")]
	public string ThumbnailUrl { get; set; }

	[JsonPropertyName("creatorID")]
	public int CreatorID { get; set; }

	[JsonPropertyName("creatorName")]
	public string CreatorName { get; set; }

	[JsonPropertyName("creatorUrl")]
	public string CreatorUrl { get; set; }
}

public struct APIFontMeta
{
	[JsonPropertyName("fonts")]
	public APIFontData[] Fonts { get; set; }
	[JsonPropertyName("pages")]
	public int Pages { get; set; }
	[JsonPropertyName("total")]
	public int Total { get; set; }
}

public struct APIFontData
{
	[JsonPropertyName("name")]
	public string Name { get; set; }
	[JsonPropertyName("preview")]
	public string Preview { get; set; }
	[JsonPropertyName("font")]
	public string Font { get; set; }
}

public struct APIMobileTokenRequest
{
	[JsonPropertyName("code")]
	public string Code { get; set; }
	[JsonPropertyName("state")]
	public string State { get; set; }
}

public struct APIMobileTokenResponse
{
	[JsonPropertyName("success")]
	public bool Success { get; set; }
	[JsonPropertyName("userID")]
	public ulong UserID { get; set; }
	[JsonPropertyName("token")]
	public string Token { get; set; }
}

public struct APIPlaceCreator
{
	[JsonPropertyName("type")]
	public string Type { get; set; }

	[JsonPropertyName("id")]
	public int Id { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("thumbnail")]
	public string Thumbnail { get; set; }
}

public struct APIPlaceRating
{
	[JsonPropertyName("likes")]
	public int Likes { get; set; }

	[JsonPropertyName("dislikes")]
	public int Dislikes { get; set; }

	[JsonPropertyName("percent")]
	public string Percent { get; set; }
}

public struct APIPlaceInfo
{
	[JsonPropertyName("id")]
	public int Id { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("description")]
	public string Description { get; set; }

	[JsonPropertyName("creator")]
	public APIPlaceCreator Creator { get; set; }

	[JsonPropertyName("thumbnail")]
	public string Thumbnail { get; set; }

	[JsonPropertyName("genre")]
	public string Genre { get; set; }

	[JsonPropertyName("maxPlayers")]
	public int MaxPlayers { get; set; }

	[JsonPropertyName("isActive")]
	public bool IsActive { get; set; }

	[JsonPropertyName("isToolsEnabled")]
	public bool IsToolsEnabled { get; set; }

	[JsonPropertyName("isCopyable")]
	public bool IsCopyable { get; set; }

	[JsonPropertyName("visits")]
	public int Visits { get; set; }

	[JsonPropertyName("uniqueVisits")]
	public int UniqueVisits { get; set; }

	[JsonPropertyName("playing")]
	public int Playing { get; set; }

	[JsonPropertyName("rating")]
	public APIPlaceRating Rating { get; set; }

	[JsonPropertyName("accessType")]
	public string AccessType { get; set; }

	[JsonPropertyName("accessPrice")]
	public object AccessPrice { get; set; }

	[JsonPropertyName("createdAt")]
	public DateTime CreatedAt { get; set; }

	[JsonPropertyName("updatedAt")]
	public DateTime? UpdatedAt { get; set; }
}
public struct APIGuildCreator
{
	[JsonPropertyName("id")]
	public int Id { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("thumbnail")]
	public string Thumbnail { get; set; }
}
public struct APIGuildInfo
{
	[JsonPropertyName("id")]
	public int Id { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("description")]
	public string Description { get; set; }

	[JsonPropertyName("creator")]
	public APIGuildCreator Creator { get; set; }

	[JsonPropertyName("thumbnail")]
	public string Thumbnail { get; set; }

	[JsonPropertyName("banner")]
	public string Banner { get; set; }

	[JsonPropertyName("color")]
	public string Color { get; set; }

	[JsonPropertyName("joinType")]
	public string Jointype { get; set; }

	[JsonPropertyName("memberCount")]
	public int MemberCount { get; set; }

	[JsonPropertyName("vaultBalance")]
	public int VaultBalance { get; set; }

	[JsonPropertyName("isVerified")]
	public bool IsVerified { get; set; }

	[JsonPropertyName("createdAt")]
	public DateTime CreatedAt { get; set; }
}

public struct APIMeResponse
{
	[JsonPropertyName("id")]
	public int Id { get; set; }

	[JsonPropertyName("username")]
	public string Username { get; set; }

	[JsonPropertyName("bricksBalance")]
	public int BricksBalance { get; set; }

	[JsonPropertyName("avatarID")]
	public string AvatarID { get; set; }

	[JsonPropertyName("membershipType")]
	public string MembershipType { get; set; }

	[JsonPropertyName("description")]
	public string Description { get; set; }

	[JsonPropertyName("signature")]
	public string Signature { get; set; }
}

public struct APIWorldsData
{
	[JsonPropertyName("id")]
	public int Id { get; set; }

	[JsonPropertyName("creatorType")]
	public string CreatorType { get; set; }

	[JsonPropertyName("creatorID")]
	public int CreatorID { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("description")]
	public string Description { get; set; }

	[JsonPropertyName("genre")]
	public string Genre { get; set; }

	[JsonPropertyName("placeType")]
	public string PlaceType { get; set; }

	[JsonPropertyName("genreIcon")]
	public string GenreIcon { get; set; }

	[JsonPropertyName("creatorName")]
	public string CreatorName { get; set; }

	[JsonPropertyName("creatorThumbnail")]
	public string CreatorThumbnail { get; set; }

	[JsonPropertyName("visits")]
	public int Visits { get; set; }

	[JsonPropertyName("playing")]
	public int Playing { get; set; }

	[JsonPropertyName("rating")]
	public double? Rating { get; set; }

	[JsonPropertyName("iconUrl")]
	public string IconUrl { get; set; }
}

public struct APIMeta
{
	[JsonPropertyName("total")]
	public int Total { get; set; }

	[JsonPropertyName("perPage")]
	public int PerPage { get; set; }

	[JsonPropertyName("currentPage")]
	public int CurrentPage { get; set; }

	[JsonPropertyName("lastPage")]
	public int LastPage { get; set; }

	[JsonPropertyName("firstPage")]
	public int FirstPage { get; set; }

	[JsonPropertyName("firstPageURL")]
	public string? FirstPageURL { get; set; }

	[JsonPropertyName("lastPageURL")]
	public string? LastPageURL { get; set; }

	[JsonPropertyName("nextPageURL")]
	public string? NextPageURL { get; set; }

	[JsonPropertyName("previousPageURL")]
	public string? PreviousPageURL { get; set; }
}

public struct APIWorldsRoot
{
	[JsonPropertyName("meta")]
	public APIMeta Meta { get; set; }

	[JsonPropertyName("data")]
	public APIWorldsData[] Data { get; set; }
}

public struct APIJoinPlaceResponse
{
	[JsonPropertyName("success")]
	public bool Success { get; set; }

	[JsonPropertyName("token")]
	public string Token { get; set; }
}

public struct APIJoinPlaceRequest
{
	[JsonPropertyName("placeID")]
	public int PlaceID { get; set; }

	[JsonPropertyName("isBeta")]
	public bool IsBeta { get; set; }
}

public struct APIFeedPostAuthor
{
	[JsonPropertyName("id")]
	public int Id { get; set; }

	[JsonPropertyName("username")]
	public string Username { get; set; }

	[JsonPropertyName("avatarID")]
	public string AvatarID { get; set; }

	[JsonPropertyName("membershipType")]
	public string MembershipType { get; set; }

	[JsonPropertyName("isOnline")]
	public bool IsOnline { get; set; }

	[JsonPropertyName("avatarIconUrl")]
	public string AvatarIconUrl { get; set; }

	[JsonPropertyName("isStaff")]
	public bool IsStaff { get; set; }
}

public struct APIFeedPostComment
{
	[JsonPropertyName("id")]
	public int Id { get; set; }

	[JsonPropertyName("content")]
	public string Content { get; set; }

	[JsonPropertyName("postedAt")]
	public DateTime PostedAt { get; set; }

	[JsonPropertyName("author")]
	public APIFeedPostAuthor Author { get; set; }

	[JsonPropertyName("reportURL")]
	public string ReportURL { get; set; }
}

public struct APIFeedPostData
{
	[JsonPropertyName("id")]
	public int Id { get; set; }

	[JsonPropertyName("content")]
	public string Content { get; set; }

	[JsonPropertyName("postedAt")]
	public DateTime PostedAt { get; set; }

	[JsonPropertyName("placeID")]
	public int? PlaceID { get; set; }

	[JsonPropertyName("author")]
	public APIFeedPostAuthor Author { get; set; }

	[JsonPropertyName("likeCount")]
	public int LikeCount { get; set; }

	[JsonPropertyName("replyCount")]
	public int ReplyCount { get; set; }

	[JsonPropertyName("isLiked")]
	public bool IsLiked { get; set; }

	[JsonPropertyName("placeName")]
	public string? PlaceName { get; set; }

	[JsonPropertyName("mediaUrl")]
	public string? MediaUrl { get; set; }

	[JsonPropertyName("reportURL")]
	public string ReportURL { get; set; }

	[JsonPropertyName("canBeDeleted")]
	public bool CanBeDeleted { get; set; }

	[JsonPropertyName("comments")]
	public APIFeedPostComment[] Comments { get; set; }
}

public struct APIFeedPostRoot
{
	[JsonPropertyName("meta")]
	public APIMeta Meta { get; set; }

	[JsonPropertyName("data")]
	public APIFeedPostData[] Data { get; set; }
}

public struct APIStoreItemCreator
{
	[JsonPropertyName("type")]
	public string Type { get; set; }

	[JsonPropertyName("id")]
	public int Id { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("thumbnail")]
	public string Thumbnail { get; set; }
}

public struct APIStoreItem
{
	[JsonPropertyName("id")]
	public int Id { get; set; }

	[JsonPropertyName("type")]
	public string Type { get; set; }

	[JsonPropertyName("accessoryType")]
	public string? AccessoryType { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("description")]
	public string Description { get; set; }

	[JsonPropertyName("tags")]
	public string[] Tags { get; set; }

	[JsonPropertyName("creator")]
	public APIStoreItemCreator Creator { get; set; }

	[JsonPropertyName("thumbnail")]
	public string Thumbnail { get; set; }

	[JsonPropertyName("version")]
	public int Version { get; set; }

	[JsonPropertyName("sales")]
	public int? Sales { get; set; }

	[JsonPropertyName("price")]
	public int? Price { get; set; }

	[JsonPropertyName("favorites")]
	public int? Favorites { get; set; }

	[JsonPropertyName("isLimited")]
	public bool IsLimited { get; set; }

	[JsonPropertyName("createdAt")]
	public DateTime CreatedAt { get; set; }

	[JsonPropertyName("updatedAt")]
	public DateTime? UpdatedAt { get; set; }
}

public struct APIOwnsItem
{
	[JsonPropertyName("owned")]
	public bool Owned { get; set; }
}

public struct APIPlaceMedia
{
	[JsonPropertyName("id")]
	public int Id { get; set; }

	[JsonPropertyName("type")]
	public string Type { get; set; }

	[JsonPropertyName("url")]
	public string Url { get; set; }
}

public enum LibraryQueryTypeEnum
{
	Model,
	Audio,
	Image,
	Mesh,
	Addon
}

[JsonSerializable(typeof(APIMeta))]
[JsonSerializable(typeof(APIUserInfo))]
[JsonSerializable(typeof(APIUserThumbnail))]
[JsonSerializable(typeof(APIAvatarResponse))]
[JsonSerializable(typeof(APIAvatarAsset))]
[JsonSerializable(typeof(APIAvatarBodyColors))]
[JsonSerializable(typeof(APILibraryResponse))]
[JsonSerializable(typeof(APITokenDataResponse))]
[JsonSerializable(typeof(APILibraryItem))]
[JsonSerializable(typeof(APIFontMeta))]
[JsonSerializable(typeof(APIMobileTokenRequest))]
[JsonSerializable(typeof(APIMobileTokenResponse))]
[JsonSerializable(typeof(APIPlaceCreator))]
[JsonSerializable(typeof(APIPlaceRating))]
[JsonSerializable(typeof(APIPlaceInfo))]
[JsonSerializable(typeof(APIMeResponse))]
[JsonSerializable(typeof(APIWorldsRoot))]
[JsonSerializable(typeof(APIWorldsData))]
[JsonSerializable(typeof(APIStoreItem))]
[JsonSerializable(typeof(APIStoreItemCreator))]
[JsonSerializable(typeof(APIOwnsItem))]
[JsonSerializable(typeof(APIPlaceMedia))]
[JsonSerializable(typeof(APIGuildCreator))]
[JsonSerializable(typeof(APIGuildInfo))]

[JsonSerializable(typeof(APIJoinPlaceResponse))]
[JsonSerializable(typeof(APIJoinPlaceRequest))]

[JsonSerializable(typeof(APIFeedPostRoot))]
[JsonSerializable(typeof(APIFeedPostData))]
[JsonSerializable(typeof(APIFeedPostComment))]
[JsonSerializable(typeof(APIFeedPostAuthor))]

[JsonSerializable(typeof(APIFeedPostData[]))]
[JsonSerializable(typeof(APIFeedPostComment[]))]

[JsonSerializable(typeof(APIAvatarAsset[]))]
[JsonSerializable(typeof(APILibraryItem[]))]
[JsonSerializable(typeof(APIFontData[]))]
[JsonSerializable(typeof(APIWorldsData[]))]
[JsonSerializable(typeof(APIPlaceMedia[]))]

[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(uint))]
[JsonSerializable(typeof(ulong))]
[JsonSerializable(typeof(double))]
internal partial class APIGenerationContext : JsonSerializerContext { }
