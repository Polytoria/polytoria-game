// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Attributes;
using Polytoria.Scripting;
using System.Collections.Generic;

namespace Polytoria.Datamodel.Data;

public partial class HttpRequestData : IScriptObject
{
	[ScriptProperty] public string URL { get; set; } = "";
	[ScriptProperty] public HttpRequestMethodEnum Method { get; set; } = HttpRequestMethodEnum.Get;
	[ScriptProperty] public string? Body { get; set; }
	[ScriptProperty] public Dictionary<string, string>? Headers { get; set; }

	[ScriptMethod]
	public static HttpRequestData New()
	{
		return new();
	}

	[ScriptEnum]
	public enum HttpRequestMethodEnum
	{
		Get,
		Post,
		Put,
		Delete,
		Patch
	}
}
