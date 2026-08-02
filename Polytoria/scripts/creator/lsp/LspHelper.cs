using System;
using System.IO;

namespace Polytoria.Creator.LSP;

public static class LspHelper
{
	public static string PathToUri(string path)
	{
		return new Uri(Path.GetFullPath(path)).AbsoluteUri;
	}
}
