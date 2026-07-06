// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Datamodel.Services;
using System;

namespace Polytoria.Tests;

public class DatamodelServiceTest
{
	[Fact]
	public void Test_HttpURLPass()
	{
		// -- Local test true -- //
		HttpService.CheckURLPass("https://example.com", true);
		HttpService.CheckURLPass("http://example.com", true);
		HttpService.CheckURLPass("http://localhost:8000", true);

		// Prevent access to non HTTP(S)
		Assert.Throws<InvalidOperationException>(() => HttpService.CheckURLPass("file://hello.txt", true));
		Assert.Throws<InvalidOperationException>(() => HttpService.CheckURLPass("ftp://127.0.0.1:6942", true));

		// -- Local test false -- //

		// Prevent access to non HTTP(S)
		Assert.Throws<InvalidOperationException>(() => HttpService.CheckURLPass("file://hello.txt", false));
		Assert.Throws<InvalidOperationException>(() => HttpService.CheckURLPass("ftp://127.0.0.1:6942", false));

		// Prevent access to private address ranges
		Assert.Throws<InvalidOperationException>(() => HttpService.CheckURLPass("http://localhost:8000", false));
		Assert.Throws<InvalidOperationException>(() => HttpService.CheckURLPass("https://localhost:8000", false));
		Assert.Throws<InvalidOperationException>(() => HttpService.CheckURLPass("https://127.0.0.1:8000", false));
		Assert.Throws<InvalidOperationException>(() => HttpService.CheckURLPass("https://0.0.0.0:8000", false));
		Assert.Throws<InvalidOperationException>(() => HttpService.CheckURLPass("https://127.0.0.1.nip.io:8000", false));
		Assert.Throws<InvalidOperationException>(() => HttpService.CheckURLPass("https://0.0.0.0.nip.io:8000", false));

		// Normal access should pass in production
		HttpService.CheckURLPass("https://example.com", false);
	}
}
