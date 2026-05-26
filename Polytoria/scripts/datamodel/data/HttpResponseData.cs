// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Attributes;
using Polytoria.Scripting;
using System.Collections.Generic;
using System.Net.Http;

namespace Polytoria.Datamodel.Data;

public partial class HttpResponseData : IScriptObject
{
	[ScriptProperty] public bool Success { get; internal set; }
	[ScriptProperty] public int StatusCode { get; internal set; }
	[ScriptProperty] public Dictionary<string, string>? Headers { get; internal set; }
	[ScriptProperty] public string Body { get; internal set; } = "";
	[ScriptProperty] public byte[] Buffer { get; internal set; } = [];
	[ScriptProperty] public bool Trusted { get; internal set; } = false;
	

	internal HttpResponseMessage responseMsg = null!;
}
