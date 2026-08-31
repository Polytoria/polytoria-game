using System.Collections.Generic;
using Polytoria.Datamodel;

namespace Polytoria.Physics;

public static class WeldGraph
{
	private static readonly Dictionary<RigidBody, List<Weld>> _welds = [];

	internal static void Add(Weld weld, RigidBody a, RigidBody b)
	{
		AddOne(a, weld);
		AddOne(b, weld);
	}

	internal static void Remove(Weld weld, RigidBody? a, RigidBody? b)
	{
		if (a != null)
		{
			RemoveOne(a, weld);
		}

		if (b != null)
		{
			RemoveOne(b, weld);
		}
	}

	private static void AddOne(RigidBody part, Weld weld)
	{
		if (!_welds.TryGetValue(part, out List<Weld>? list))
		{
			list = [];
			_welds[part] = list;

			part.Deleted += () =>
			{
				WeldAssemblyManager.OnPartDeleted(part);
			};
		}

		if (!list.Contains(weld))
		{
			list.Add(weld);
		}
	}

	private static void RemoveOne(RigidBody part, Weld weld)
	{
		if (!_welds.TryGetValue(part, out List<Weld>? list))
		{
			return;
		}

		list.Remove(weld);

		if (list.Count == 0)
		{
			_welds.Remove(part);
		}
	}

	internal static List<Weld> GetWelds(RigidBody part)
	{
		if (!_welds.TryGetValue(part, out List<Weld>? list))
		{
			return [];
		}

		return list;
	}

	internal static RigidBody? GetOtherPart(Weld weld, RigidBody part)
	{
		if (weld.Part0 == part)
		{
			return weld.Part1 as RigidBody;
		}
		else if (weld.Part1 == part)
		{
			return weld.Part0 as RigidBody;
		}

		return null;
	}

	internal static bool AreConnected(RigidBody a, RigidBody b)
	{
		foreach (Weld weld in GetWelds(a))
		{
			RigidBody? other = GetOtherPart(weld, a);
			if (other == b)
			{
				return true;
			}
		}

		return false;
	}

	internal static bool TryGetParts(Weld weld, out RigidBody a, out RigidBody b)
	{
		if (weld.Part0 is RigidBody p0 && weld.Part1 is RigidBody p1 && p0 != p1)
		{
			a = p0;
			b = p1;
			return true;
		}

		a = null!;
		b = null!;
		return false;
	}

	internal static bool AreConnected(RigidBody start, RigidBody target, HashSet<RigidBody> limit)
	{
		if (start == target)
		{
			return true;
		}

		HashSet<RigidBody> visited = [];
		Queue<RigidBody> queue = [];

		visited.Add(start);
		queue.Enqueue(start);

		while (queue.Count > 0)
		{
			RigidBody current = queue.Dequeue();

			foreach (Weld weld in GetWelds(current))
			{
				RigidBody? next = GetOtherPart(weld, current);
				if (next == null || next.IsDeleted || !limit.Contains(next))
				{
					continue;
				}

				if (next == target)
				{
					return true;
				}

				if (visited.Add(next))
				{
					queue.Enqueue(next);
				}
			}
		}

		return false;
	}

	internal static HashSet<RigidBody> GetComponent(RigidBody start)
	{
		HashSet<RigidBody> result = new(ReferenceEqualityComparer.Instance);
		Queue<RigidBody> queue = new();

		if (start.IsDeleted || start.IsInTemporary)
		{
			return result;
		}

		result.Add(start);
		queue.Enqueue(start);

		while (queue.Count > 0)
		{
			RigidBody part = queue.Dequeue();

			if (!_welds.TryGetValue(part, out List<Weld>? welds))
			{
				continue;
			}

			foreach (Weld weld in welds)
			{
				RigidBody? other = GetOtherPart(weld, part);

				if (other == null)
				{
					continue;
				}

				if (other.IsDeleted || other.IsInTemporary)
				{
					continue;
				}

				if (result.Add(other))
				{
					queue.Enqueue(other);
				}
			}
		}

		return result;
	}

	internal static HashSet<RigidBody> GetComponentWithin(RigidBody start, HashSet<RigidBody> limit)
	{
		HashSet<RigidBody> visited = new(ReferenceEqualityComparer.Instance);
		Queue<RigidBody> queue = new();

		if (!limit.Contains(start))
		{
			return visited;
		}

		visited.Add(start);
		queue.Enqueue(start);

		while (queue.Count > 0)
		{
			RigidBody current = queue.Dequeue();

			foreach (Weld weld in GetWelds(current))
			{
				RigidBody? next = GetOtherPart(weld, current);
				if (next == null || next.IsDeleted || !limit.Contains(next))
				{
					continue;
				}

				if (visited.Add(next))
				{
					queue.Enqueue(next);
				}
			}
		}

		return visited;
	}
}
