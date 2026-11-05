// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Client.Networking;
using System;

namespace Polytoria.Datamodel;

internal sealed class NetTimeline(World root)
{
	private const int DelaySampleCount = 500;
	private const double DelayWindow = 10;
	private const double MaxDelay = 1.5;
	private const double DelayPercentile = 0.9;
	public const double RiseRate = 0.2;
	public const double FallRate = 0.05;
	private const double Deadband = 0.01;
	private const double MaxFrameTime = 0.25;

	private readonly double[] _delays = new double[DelaySampleCount];
	private readonly double[] _delayTimes = new double[DelaySampleCount];
	private readonly double[] _sortedDelays = new double[DelaySampleCount];
	private int _delayCount;
	private int _delayIndex;
	private int _slotDelayCount;
	private bool _isDirty;
	private ulong _lastFrame;
	private double _targetDelay = NetworkTransformSync.TickInterval;
	private double _clockSnapTotal = root.ClockSnapTotal;

	public double Delay { get; private set; }
	public bool HasDelay { get; private set; }

	public void AddSample(double time, double gap)
	{
		double frameTime = Mathf.Min(World.LocalFrameTime, MaxFrameTime);
		double requiredDelay = root.NetTime - frameTime - time + Mathf.Max(gap, NetworkTransformSync.TickInterval);

		int lastIndex = (_delayIndex + DelaySampleCount - 1) % DelaySampleCount;
		if (_delayCount > 0 && World.LocalClock - _delayTimes[lastIndex] < DelayWindow / DelaySampleCount)
		{
			_slotDelayCount++;
			if (Random.Shared.Next(_slotDelayCount) == 0) _delays[lastIndex] = requiredDelay;
		}
		else
		{
			lastIndex = _delayIndex;
			_delays[lastIndex] = requiredDelay;
			_delayTimes[lastIndex] = World.LocalClock;
			_delayIndex = (_delayIndex + 1) % DelaySampleCount;
			_delayCount = Mathf.Min(_delayCount + 1, DelaySampleCount);
			_slotDelayCount = 1;
		}
		_isDirty = true;
	}

	public double Update(double delta)
	{
		ulong frame = Engine.GetProcessFrames();
		if (frame == _lastFrame && HasDelay) return Delay;
		_lastFrame = frame;

		if (_clockSnapTotal != root.ClockSnapTotal)
		{
			double snap = root.ClockSnapTotal - _clockSnapTotal;
			_clockSnapTotal = root.ClockSnapTotal;
			Delay += snap;
			_targetDelay += snap;
			_delayCount = 0;
			_delayIndex = 0;
			_isDirty = false;
		}

		if (_isDirty)
		{
			_isDirty = false;
			int count = 0;
			for (int i = 0; i < _delayCount; i++)
			{
				if (World.LocalClock - _delayTimes[i] <= DelayWindow)
				{
					_sortedDelays[count] = _delays[i];
					count++;
				}
			}
			if (count > 0)
			{
				Array.Sort(_sortedDelays, 0, count);
				_targetDelay = Mathf.Min(_sortedDelays[(int)(count * DelayPercentile)], MaxDelay);
			}
		}

		if (!HasDelay)
		{
			Delay = _targetDelay;
			HasDelay = true;
		}
		else if (Mathf.Abs(_targetDelay - Delay) > Deadband)
		{
			Delay += Mathf.Clamp(_targetDelay - Delay, -delta * FallRate, delta * RiseRate);
		}
		return Delay;
	}
}
