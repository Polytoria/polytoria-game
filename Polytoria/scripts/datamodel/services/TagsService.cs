// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using Polytoria.Attributes;
using Polytoria.Scripting;
using Polytoria.Shared;

namespace Polytoria.Datamodel.Services;

[Static("TagsService")]
[ExplorerExclude]
public sealed partial class TagsService : Instance
{
	private readonly Dictionary<string, List<Instance>> _registeredTags = [];

	[ScriptProperty] new public PTSignal<Instance, string> TagAdded { get; private set; } = new();
	[ScriptProperty] new public PTSignal<Instance, string> TagRemoved { get; private set; } = new();

	[ScriptMethod]
	public Instance[] GetTagged(string tag)
	{
		return _registeredTags.TryGetValue(tag, out var values) ? values.ToArray() : [];
	}

	[ScriptMethod]
	public string[]? GetTags()
	{
		return _registeredTags.Keys.ToArray();
	}

	public void RegisterTagAdded(Instance instance, string tag)
	{
		if (_registeredTags.GetValueOrDefault(tag) == null)
		{
			_registeredTags.TryAdd(tag, []);
		}

		if (_registeredTags[tag].Contains(instance))
		{
			return;
		}

		_registeredTags[tag].Add(instance);
		TagAdded?.Invoke(instance, tag);
	}

	public void RegisterTagRemoved(Instance instance, string tag)
	{
		if (_registeredTags.GetValueOrDefault(tag) == null)
		{
			return;
		}

		if (!_registeredTags[tag].Contains(instance))
		{
			return;
		}

		_registeredTags[tag].Remove(instance);
		TagRemoved?.Invoke(instance, tag);
		if (_registeredTags[tag].Count == 0)
		{
			_registeredTags.Remove(tag);
		}
	}

	public override void PreDelete()
	{
		_registeredTags.Clear();
		base.PreDelete();
	}
}
