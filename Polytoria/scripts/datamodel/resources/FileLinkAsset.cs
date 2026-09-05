// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Attributes;
using Polytoria.Shared;

namespace Polytoria.Datamodel.Resources;

/// <summary>
/// Base class for asset that link to files
/// </summary>
[Instantiable, SaveIgnore]
public partial class FileLinkAsset : BaseAsset
{
	private string _fileID = "";

	[Editable, ScriptProperty]
	public string LinkedID
	{
		get => _fileID; set
		{
			_fileID = value;
			if (_fileID != "")
			{
				Root.Assets.RegisterFileLink(this);
			}
		}
	}

	public string? LinkedPath => Root.IO.GetPathFromID(LinkedID);

	public override void Init()
	{
		base.Init();
	}

	public byte[]? ReadFile()
	{
		if (string.IsNullOrWhiteSpace(LinkedID)) return null;
		byte[]? data = Root.IO.ReadBytesFromID(LinkedID);
		if (data != null)
		{
			return data;
		}
		else
		{
			PT.PrintErr($"Failed to get file: LINKID: ", LinkedID);
		}
		return null;
	}

	public override void PreDelete()
	{
		Root.Assets.DeregisterFileLink(this);
		base.PreDelete();
	}
}
