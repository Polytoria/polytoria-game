// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Client.Networking;
using Polytoria.Utils.DTOs;
using System.Collections.Generic;

namespace Polytoria.Datamodel;

public sealed class NetMotion(Dynamic dyn)
{
	public const double MaxGap = 0.5;
	private const float SnapDistance = 8f;
	private const int MaxSamples = 48;
	private const double MinSampleSpacing = 0.01;
	private const double SnapSpacing = 0.0001;
	private const double StaleTimeout = 1.0;

	private const double BlendTime = 0.15;
	private const float BlendGap = 0.25f;
	private const float BlendSettle = 0.001f;

	private const double HandoffExtrapolateLimit = 0.5;
	private const double ExtrapolateLimit = 0.25;
	private const double FallExtrapolateLimit = 1;
	private const int FallSteps = 4;

	private const double GainMinTime = 0.15;
	private const double GainMaxTime = 0.5;

	private const float StopRatio = 0.75f;
	private const float CurveTolerance = 0.25f;
	private const float MaxTangentScale = 3f;
	private const float MinSpeed = 2f;
	private const float MoverSpeed = 8f;
	private const float FallTolerance = 0.4f;

	private static PhysicsShapeQueryParameters3D? _hitQuery;
	private static ulong _moverFrame = ulong.MaxValue;
	private static Godot.Collections.Array<Rid> _moverRids = [];
	private static ulong _gravityFrame = ulong.MaxValue;
	private static Rid _gravitySpace;
	private static Vector3 _spaceGravity;

	public readonly List<Sample> Samples = [];
	public bool IsBridging;
	public (Vector3 Linear, Vector3 Angular)? GainMotion;
	public bool IsMoving;
	public bool IsStopSent;
	public double LastMoveTime;
	public Transform3D? RestTransform;

	private NetTimeline? _timeline;
	private double _renderDelay;
	private double _delayOffset;
	private bool _hasRenderDelay;
	private bool _hasDelayOffset;
	private int _sendPeriod = 1;
	private bool _isCatchingUp;
	private bool _isSeeded;
	private bool _isProvisional;
	private bool _isAtRest;
	private bool _canExtrapolate;
	private Vector3 _lastRenderVelocity;
	private Vector3? _lastRawPosition;
	private Vector3 _blendOffset;
	private double _lastRenderTime;
	private double _hitSampleTime;
	private double _hitTime;
	private bool _hasHit;
	private double _gainStart;
	private double _gainDelay;
	private double _gainTime;
	private Vector3 _derivePosition;
	private double _deriveTime = double.MinValue;
	private double _lastSnapTime = double.MinValue;

	public bool IsGaining { get; private set; }

	public double GainElapsed => dyn.Root.NetTime - _gainStart;

	public double GainTime => _gainTime;

	public double LastSampleTime => Samples.Count > 0 ? Samples[Samples.Count - 1].Time : double.MinValue;

	public bool IsResting => _isAtRest || Samples.Count == 0 || dyn.Root.NetTime - Samples[Samples.Count - 1].Time > MaxGap;

	public float Speed => IsResting ? 0 : Samples[Samples.Count - 1].Velocity.Length();

	public bool IsPlaying => Samples.Count > 0 && _hasRenderDelay && dyn.Root.NetTime - _renderDelay < Samples[Samples.Count - 1].Time;

	public bool IsAhead(double time)
	{
		if (Samples.Count == 0) return false;
		if (IsPlaying) return dyn.Root.NetTime - _renderDelay < time;

		_timeline ??= dyn.Root.GetNetTimeline(dyn.NetTransformAuthority);
		return _timeline.HasDelay && dyn.Root.NetTime - _timeline.Delay < time;
	}

	public bool CanGain => Samples.Count >= 2 && _canExtrapolate && _hasRenderDelay &&
		dyn.Root.NetTime - Samples[Samples.Count - 1].Time < MaxGap;

	public bool IsProvisional => _isProvisional;

	public void Reset()
	{
		Samples.Clear();
		_lastRawPosition = null;
		_isProvisional = false;
	}

	public void Seed(Transform3D local, Vector3 linear, Vector3 angular, double time)
	{
		float step = (float)NetworkTransformSync.TickInterval;
		Quaternion rotation = local.Basis.Orthonormalized().GetRotationQuaternion();
		Quaternion previous = rotation;
		if (angular.Length() > 0.01f)
		{
			previous = new Quaternion(angular.Normalized(), -angular.Length() * step) * rotation;
		}

		Samples.Clear();
		Samples.Add(new(time - step, local.Origin - linear * step, previous.Normalized(), linear, false, angular));
		Samples.Add(new(time, local.Origin, rotation, linear, false, angular));
		_lastRawPosition = null;
		_canExtrapolate = true;
		_isCatchingUp = false;
		_isSeeded = true;
		_isProvisional = true;
		_renderDelay = 0;
		_hasRenderDelay = true;
		_hasDelayOffset = false;
	}

	public void StartGain()
	{
		IsGaining = true;
		_gainStart = dyn.Root.NetTime;
		_gainDelay = Mathf.Max(_renderDelay, 0);
		_gainTime = Mathf.Clamp(_gainDelay * 2, GainMinTime, GainMaxTime);
		_isSeeded = true;
	}

	public void StopGain()
	{
		IsGaining = false;
		_hasDelayOffset = false;
	}

	public void FinishGain()
	{
		IsGaining = false;
		Reset();
		_isSeeded = false;
	}

	public (Vector3 Velocity, Vector3 AngularVelocity, double Time) GetMotion()
	{
		var last = Samples[Samples.Count - 1];
		Vector3 velocity = Vector3.Zero;
		Vector3 angular = Vector3.Zero;
		if (!_isAtRest && dyn.Root.NetTime - last.Time < MaxGap)
		{
			velocity = last.Velocity;
			angular = last.Spin;
		}
		return (velocity, angular, last.Time);
	}

	public (Vector3 Position, Quaternion Rotation) GetLatest()
	{
		var last = Samples[Samples.Count - 1];
		Vector3 position = last.Position;
		if (_canExtrapolate && Samples.Count >= 2)
		{
			position += RemoveFall(last.Velocity) * (float)Mathf.Clamp(dyn.Root.NetTime - last.Time, 0, HandoffExtrapolateLimit);
		}
		return (position, last.Rotation);
	}

	public bool TryGetVelocity(out Vector3 linear, out Vector3 angular)
	{
		linear = Vector3.Zero;
		angular = Vector3.Zero;
		if (!_canExtrapolate || Samples.Count < 2 || dyn.Root.NetTime - Samples[Samples.Count - 1].Time > MaxGap) return false;

		var last = Samples[Samples.Count - 1];
		linear = last.Velocity;
		angular = last.Spin;
		return true;
	}

	public Vector3 DeriveVelocity(Vector3 position, double time)
	{
		double elapsed = time - _deriveTime;
		Vector3 velocity = elapsed > 0.001 && elapsed < MaxGap ? (position - _derivePosition) / (float)elapsed : Vector3.Zero;
		_derivePosition = position;
		_deriveTime = time;
		return velocity;
	}

	public bool AddSample(TransformPayloadDto transform, double time, int period, bool rest, Vector3 velocity,
		bool snap = false, Vector3 spin = default)
	{
		if (snap && _isProvisional && Samples.Count > 0 && time < Samples[Samples.Count - 1].Time)
		{
			Samples.Clear();
		}
		int count = Samples.Count;
		if (count > 0 && time < Samples[count - 1].Time + MinSampleSpacing)
		{
			if (!(rest || snap) || time < Samples[count - 1].Time) return false;
			count--;
			Samples.RemoveAt(count);
		}
		var previous = count > 0 ? Samples[count - 1] : default;
		bool canMeasureSpin = count > 0 && time - previous.Time > MinSampleSpacing && time - previous.Time < MaxGap;

		double gap = 0;
		if (count > 0 && !rest && previous.Velocity.Length() >= MinSpeed && time - previous.Time <= MaxGap)
		{
			gap = time - previous.Time;
		}

		period = Mathf.Max(period, 1);
		if (!rest && !snap)
		{
			if (period != _sendPeriod)
			{
				_isCatchingUp = period < _sendPeriod;
				_hasDelayOffset = false;
			}
			_sendPeriod = period;
		}

		World root = dyn.Root;
		NetTimeline timeline = root.GetNetTimeline(dyn.NetTransformAuthority);
		if (timeline != _timeline)
		{
			_timeline = timeline;
			_hasDelayOffset = false;
		}

		Vector3 position = transform.Position;
		if (!snap && count > 0)
		{
			snap = ShouldSnap(previous.Time, previous.Position, previous.Velocity, time, position, velocity, GetLocalGravity());
		}
		if (snap && count > 0 && time - SnapSpacing > previous.Time)
		{
			var end = Extrapolate(time - SnapSpacing);
			Samples.Add(new(time - SnapSpacing, end.Position, previous.Rotation, end.Velocity, false, Vector3.Zero));
		}

		_canExtrapolate = true;
		if (!snap && Samples.Count > 0 &&
			time - Samples[Samples.Count - 1].Time >= MaxGap && Samples[Samples.Count - 1].Velocity.Length() < MinSpeed)
		{
			var lastSample = Samples[Samples.Count - 1];
			double holdTime = time - NetworkTransformSync.TickInterval * period;
			if (!rest && velocity.Length() > MinSpeed)
			{
				double travelTime = lastSample.Position.DistanceTo(position) / velocity.Length();
				holdTime = Mathf.Min(holdTime, time - travelTime - MinSampleSpacing * 2);
			}
			holdTime = Mathf.Max(holdTime, lastSample.Time + MinSampleSpacing);
			Samples.Add(new(holdTime, lastSample.Position, lastSample.Rotation, Vector3.Zero, false, Vector3.Zero));
			_delayOffset = 0;
			_hasDelayOffset = true;
		}

		if (!snap && !rest && Samples.Count > 0 &&
			Samples[Samples.Count - 1].Velocity.Length() < velocity.Length() * 0.25f && velocity.Length() > MinSpeed)
		{
			var lastSample = Samples[Samples.Count - 1];
			double elapsed = time - lastSample.Time;
			Vector3 offset = position - lastSample.Position - lastSample.Velocity * (float)elapsed;
			double startTime = time - GetChangeTime(offset, velocity - lastSample.Velocity, elapsed);
			if (startTime > lastSample.Time + MinSampleSpacing)
			{
				Vector3 startPosition = lastSample.Position + lastSample.Velocity * (float)(startTime - lastSample.Time);
				Samples.Add(new(startTime, startPosition, lastSample.Rotation, velocity, false, Vector3.Zero));
			}
		}

		if (!rest && !snap && root.IsClockSynced && dyn.GDNode3D.GetProcessDeltaTime() < NetworkTransformSync.TickInterval * 2)
		{
			timeline.AddSample(time, gap);
		}

		if (rest)
		{
			_canExtrapolate = false;
		}

		if (spin == Vector3.Zero && !rest && !snap && canMeasureSpin)
		{
			spin = GetAngularVelocity(previous.Rotation, transform.Rotation, time - previous.Time);
		}

		Samples.Add(new(time, position, transform.Rotation, rest ? Vector3.Zero : velocity, snap, rest ? Vector3.Zero : spin));
		_isProvisional = false;
		while (Samples.Count > MaxSamples)
		{
			Samples.RemoveAt(0);
		}
		return true;
	}

	public void KeepSample(TransformPayloadDto transform, double time, bool rest, Vector3 velocity)
	{
		_isAtRest = rest;
		Vector3 position = transform.Position;
		Vector3 spin = Vector3.Zero;
		if (Samples.Count > 0)
		{
			var lastSample = Samples[Samples.Count - 1];
			if (time <= lastSample.Time) return;
			_canExtrapolate = time - lastSample.Time < MaxGap && lastSample.Position.DistanceTo(position) <= SnapDistance;
			if (_canExtrapolate && !rest && time - lastSample.Time > MinSampleSpacing)
			{
				spin = GetAngularVelocity(lastSample.Rotation, transform.Rotation, time - lastSample.Time);
			}
		}

		Samples.Add(new(time, position, transform.Rotation, rest ? Vector3.Zero : velocity, false, spin));
		while (Samples.Count > 2)
		{
			Samples.RemoveAt(0);
		}
	}

	public void SetSample(TransformPayloadDto transform, double time, Vector3 velocity)
	{
		Reset();
		Samples.Add(new(time, transform.Position, transform.Rotation, velocity, false, Vector3.Zero));
	}

	public double UpdateRenderTime(double delta)
	{
		World root = dyn.Root;
		if (IsGaining)
		{
			double progress = Mathf.Min((root.NetTime - _gainStart) / _gainTime, 1);
			_renderDelay = _gainDelay * (1 - progress * progress * (3 - 2 * progress));
			return root.NetTime - _renderDelay;
		}

		_timeline ??= root.GetNetTimeline(dyn.NetTransformAuthority);
		double sharedDelay = _timeline.Update(delta);
		if (!_hasDelayOffset)
		{
			_delayOffset = _hasRenderDelay ? _renderDelay - sharedDelay : 0;
			_hasDelayOffset = true;
		}
		if (_delayOffset > 0 && HasPendingSnap())
		{
			_delayOffset = 0;
		}
		if (_delayOffset > 0)
		{
			_delayOffset = Mathf.Max(_delayOffset - delta * (_isCatchingUp ? NetTimeline.RiseRate : NetTimeline.FallRate), 0);
		}
		else if (_delayOffset < 0)
		{
			_delayOffset = Mathf.Min(_delayOffset + delta * NetTimeline.RiseRate, 0);
		}
		if (_delayOffset == 0)
		{
			_isCatchingUp = false;
			_isSeeded = false;
		}

		_renderDelay = sharedDelay + _delayOffset;
		_hasRenderDelay = true;
		return root.NetTime - _renderDelay;
	}

	private bool HasPendingSnap()
	{
		foreach (var sample in Samples)
		{
			if (sample.Snap && sample.Time > _lastSnapTime) return true;
		}
		return false;
	}

	public bool Interpolate(double renderTime, double delta, bool first, out Vector3 position, out Quaternion rotation, out bool settled)
	{
		while (Samples.Count >= 3 && Samples[1].Time <= renderTime)
		{
			if (Samples[0].Snap && Samples[0].Time > _lastSnapTime)
			{
				_lastSnapTime = Samples[0].Time;
				_lastRawPosition = null;
			}
			Samples.RemoveAt(0);
		}

		var from = Samples[0];
		var last = Samples[Samples.Count - 1];
		Vector3 renderVelocity = Vector3.Zero;
		settled = false;

		if (renderTime <= from.Time)
		{
			position = from.Position;
			rotation = from.Rotation;
			if (!first) return false;
		}
		else if (renderTime <= last.Time)
		{
			var next = Samples[1];
			float duration = (float)(next.Time - from.Time);
			float weight = (float)(renderTime - from.Time) / duration;
			float rotationWeight = weight;
			Vector3 travel = next.Position - from.Position;
			float travelLength = travel.Length();
			float fromSpeed = from.Velocity.Length();
			float nextSpeed = next.Velocity.Length();
			bool isSmooth = (travel - (from.Velocity + next.Velocity) * (duration / 2)).Length() < CurveTolerance * travelLength + 0.01f;
			float expectedTravel = fromSpeed * duration;
			renderVelocity = travel / duration;
			if (travelLength > 0.0001f && travelLength < expectedTravel * StopRatio && nextSpeed < fromSpeed * 0.5f)
			{
				float elapsed = weight * duration;
				float stopTime = Mathf.Clamp((travelLength - nextSpeed * duration) / (fromSpeed - nextSpeed), 0, duration);
				float travelled = elapsed < stopTime ? fromSpeed * elapsed : fromSpeed * stopTime + nextSpeed * (elapsed - stopTime);
				float speed = elapsed < stopTime ? fromSpeed : nextSpeed;
				weight = Mathf.Min(1f, travelled / travelLength);
				renderVelocity = weight < 1f ? travel / travelLength * speed : Vector3.Zero;
				position = from.Position.Lerp(next.Position, weight);
			}
			else if (isSmooth)
			{
				position = HermitePosition(from, next, weight, duration);
				renderVelocity = HermiteVelocity(from, next, weight, duration);
			}
			else
			{
				position = from.Position.Lerp(next.Position, weight);
			}
			rotation = InterpolateRotation(from, next, rotationWeight, duration);
		}
		else
		{
			double sinceLast = renderTime - last.Time;
			position = last.Position;
			rotation = last.Rotation;
			if (_canExtrapolate && Samples.Count >= 2)
			{
				double extrapolateLimit = HandoffExtrapolateLimit;
				if (!_isSeeded)
				{
					int ticks = Mathf.Max(Mathf.Max(2, _sendPeriod), dyn.Root.SnapshotThrottle);
					extrapolateLimit = Mathf.Min(ExtrapolateLimit, NetworkTransformSync.TickInterval * ticks);
				}
				var previous = Samples[Samples.Count - 2];
				Vector3 velocity = last.Velocity;
				Vector3 gravity = GetLocalGravity();
				bool falling = IsFalling(previous, last, gravity);
				Vector3 fallAcceleration = falling ? gravity : Vector3.Zero;
				double extrapolateTime = Mathf.Min(sinceLast, extrapolateLimit);
				if (falling)
				{
					if (_hitSampleTime != last.Time)
					{
						_hitSampleTime = last.Time;
						_hitTime = 0;
						_hasHit = false;
					}
					if (!_hasHit && _hitTime < Mathf.Min(sinceLast, FallExtrapolateLimit))
					{
						_hitTime = GetHitTime(last.Position, last.Rotation, velocity, gravity, _hitTime, sinceLast, out _hasHit);
					}
					extrapolateTime = Mathf.Min(sinceLast, _hitTime);
				}
				else
				{
					velocity = RemoveFall(last.Velocity);
				}
				Vector3 offset = velocity * (float)extrapolateTime + fallAcceleration * (float)(extrapolateTime * extrapolateTime / 2);
				position += offset;
				renderVelocity = sinceLast <= extrapolateTime ? velocity + fallAcceleration * (float)extrapolateTime : Vector3.Zero;
				if (last.Spin != Vector3.Zero)
				{
					rotation = (new Quaternion(last.Spin.Normalized(), last.Spin.Length() * (float)extrapolateTime) * last.Rotation).Normalized();
				}
				else if (_isSeeded)
				{
					Quaternion rotationDelta = GetRotationDelta(previous.Rotation, last.Rotation);
					float angle = rotationDelta.GetAngle() * (float)(extrapolateTime / Mathf.Max(last.Time - previous.Time, 0.001));
					if (angle > 0.0001f && rotationDelta.GetAxis().Length() > 0.01f)
					{
						rotation = (new Quaternion(rotationDelta.GetAxis().Normalized(), angle) * last.Rotation).Normalized();
					}
				}
			}
			settled = sinceLast > StaleTimeout && !IsGaining && _blendOffset.Length() < BlendSettle;
		}

		for (int i = 0; i < Mathf.Min(Samples.Count, 2); i++)
		{
			var sample = Samples[i];
			if (sample.Snap && sample.Time > _lastSnapTime && sample.Time <= renderTime)
			{
				_lastSnapTime = sample.Time;
				_lastRawPosition = null;
			}
		}

		float renderAdvance = (float)(renderTime - _lastRenderTime);
		if (_lastRawPosition is Vector3 lastPosition && renderAdvance >= 0 && renderAdvance < BlendGap)
		{
			Vector3 deviation = position - lastPosition - renderVelocity * renderAdvance;
			float speed = Mathf.Max(renderVelocity.Length(), _lastRenderVelocity.Length());
			if (deviation.Length() > SnapDistance + Mathf.Max(speed, Samples[0].Velocity.Length()) * MaxGap)
			{
				_blendOffset = Vector3.Zero;
			}
			else if (deviation.Length() > Mathf.Max(1.5f * speed * renderAdvance, 0.03f))
			{
				_blendOffset -= deviation;
			}
		}
		else
		{
			_blendOffset = Vector3.Zero;
		}
		_blendOffset *= (float)Mathf.Exp(-delta / BlendTime);
		_lastRawPosition = position;
		_lastRenderTime = renderTime;
		_lastRenderVelocity = renderVelocity;
		position += _blendOffset;
		return true;
	}

	private static Quaternion InterpolateRotation(Sample from, Sample next, float weight, float duration)
	{
		if (from.Spin == Vector3.Zero && next.Spin == Vector3.Zero) return from.Rotation.Slerp(next.Rotation, weight);
		Quaternion delta = GetRotationDelta(from.Rotation, next.Rotation);
		float angle = delta.GetAngle();
		Vector3 axis = angle > 0.0001f ? delta.GetAxis().Normalized() : (from.Spin + next.Spin).Normalized();
		float startTangent = from.Spin.Dot(axis) * duration;
		float endTangent = next.Spin.Dot(axis) * duration;
		float nearestTangent = Mathf.Abs(startTangent) < Mathf.Abs(endTangent) ? startTangent : endTangent;
		angle += Mathf.Tau * Mathf.Round((nearestTangent - angle) / Mathf.Tau);
		if (Mathf.Abs(angle) < 0.0001f)
		{
			startTangent = 0;
			endTangent = 0;
		}
		else
		{
			float startScale = Mathf.Max(startTangent / angle, 0);
			float endScale = Mathf.Max(endTangent / angle, 0);
			float scaleSquared = startScale * startScale + endScale * endScale;
			if (scaleSquared > MaxTangentScale * MaxTangentScale)
			{
				float shrink = MaxTangentScale / Mathf.Sqrt(scaleSquared);
				startScale *= shrink;
				endScale *= shrink;
			}
			startTangent = startScale * angle;
			endTangent = endScale * angle;
		}
		float w2 = weight * weight;
		float w3 = w2 * weight;
		float turnAngle = startTangent * (w3 - 2 * w2 + weight) + angle * (3 * w2 - 2 * w3) + endTangent * (w3 - w2);
		return (new Quaternion(axis, turnAngle) * from.Rotation).Normalized();
	}

	private static Vector3 HermitePosition(Sample from, Sample next, float weight, float duration)
	{
		float w2 = weight * weight;
		float w3 = w2 * weight;
		return from.Position * (2 * w3 - 3 * w2 + 1) +
			from.Velocity * (duration * (w3 - 2 * w2 + weight)) +
			next.Position * (-2 * w3 + 3 * w2) +
			next.Velocity * (duration * (w3 - w2));
	}

	private static Vector3 HermiteVelocity(Sample from, Sample next, float weight, float duration)
	{
		float w2 = weight * weight;
		return (from.Position * (6 * w2 - 6 * weight) + next.Position * (-6 * w2 + 6 * weight)) / duration +
			from.Velocity * (3 * w2 - 4 * weight + 1) +
			next.Velocity * (3 * w2 - 2 * weight);
	}

	private static bool IsFalling(Sample previous, Sample last, Vector3 gravity)
	{
		double sampleGap = last.Time - previous.Time;
		if (sampleGap > MinSampleSpacing &&
			previous.Velocity != Vector3.Zero &&
			last.Velocity != Vector3.Zero &&
			Mathf.Max(previous.Velocity.Length(), last.Velocity.Length()) > MoverSpeed)
		{
			Vector3 acceleration = (last.Velocity - previous.Velocity) / (float)sampleGap;
			return (acceleration - gravity).Length() < gravity.Length() * FallTolerance + MinSpeed;
		}
		return false;
	}

	private static Quaternion GetRotationDelta(Quaternion from, Quaternion to)
	{
		Quaternion delta = (to * from.Inverse()).Normalized();
		if (delta.W < 0) delta = -delta;
		return delta;
	}

	public static Vector3 GetAngularVelocity(Quaternion from, Quaternion to, double time)
	{
		Quaternion delta = GetRotationDelta(from, to);
		return delta.GetAxis() * (delta.GetAngle() / (float)Mathf.Max(time, 0.001));
	}

	public static bool ShouldSnap(double lastTime, Vector3 lastPosition, Vector3 lastVelocity,
		double time, Vector3 position, Vector3 velocity, Vector3 gravity)
	{
		double elapsed = time - lastTime;
		float fallDistance = gravity.Length() * (float)(elapsed * elapsed / 2);
		Vector3 down = gravity.Normalized();
		Vector3 travel = position - lastPosition;
		Vector3 averageVelocity = (lastVelocity + velocity) / 2;
		double travelTime = Mathf.Clamp(travel.Dot(averageVelocity) / Mathf.Max(averageVelocity.LengthSquared(), 0.0001f), 0, elapsed);
		float distance = GetSnapDistance(travel - averageVelocity * (float)travelTime, down, fallDistance);
		Vector3 velocityChange = velocity - lastVelocity;
		if (velocityChange.Length() > MinSpeed)
		{
			Vector3 offset = travel - lastVelocity * (float)elapsed;
			double changeTime = GetChangeTime(offset, velocityChange, elapsed);
			distance = Mathf.Min(distance, GetSnapDistance(offset - velocityChange * (float)changeTime, down, fallDistance));
		}
		return distance > SnapDistance * Mathf.Clamp(elapsed / NetworkTransformSync.TickInterval, 1, 4);
	}

	private static float GetSnapDistance(Vector3 offset, Vector3 down, float maxFall)
	{
		return (offset - down * Mathf.Clamp(offset.Dot(down), 0, maxFall)).Length();
	}

	private static double GetChangeTime(Vector3 offset, Vector3 velocityChange, double elapsed)
	{
		return Mathf.Clamp(offset.Dot(velocityChange) / Mathf.Max(velocityChange.LengthSquared(), 0.0001f), 0, elapsed);
	}

	private (Vector3 Position, Vector3 Velocity) Extrapolate(double time)
	{
		var last = Samples[Samples.Count - 1];
		if (Samples.Count < 2 || last.Velocity == Vector3.Zero) return (last.Position, Vector3.Zero);
		var previous = Samples[Samples.Count - 2];
		double sinceLast = time - last.Time;
		Vector3 gravity = GetLocalGravity();
		if (!IsFalling(previous, last, gravity))
		{
			double extrapolateTime = Mathf.Min(sinceLast, ExtrapolateLimit);
			Vector3 velocity = RemoveFall(last.Velocity);
			return (last.Position + velocity * (float)extrapolateTime, extrapolateTime < sinceLast ? Vector3.Zero : velocity);
		}
		double fallTime = Mathf.Min(sinceLast, GetHitTime(last.Position, last.Rotation, last.Velocity, gravity, 0, sinceLast, out _));
		Vector3 position = last.Position + last.Velocity * (float)fallTime + gravity * (float)(fallTime * fallTime / 2);
		return (position, fallTime < sinceLast ? Vector3.Zero : last.Velocity + gravity * (float)fallTime);
	}

	private double GetHitTime(Vector3 from, Quaternion rotation, Vector3 velocity, Vector3 gravity,
		double fromTime, double toTime, out bool hit)
	{
		hit = false;
		if (dyn.GDNode3D is not CollisionObject3D body || !body.IsInsideTree()) return FallExtrapolateLimit;
		Rid rid = body.GetRid();
		if (PhysicsServer3D.BodyGetShapeCount(rid) == 0) return FallExtrapolateLimit;
		Transform3D parent = body.GetParentNode3D() is Node3D node ? node.GlobalTransform : Transform3D.Identity;
		Transform3D shape = PhysicsServer3D.BodyGetShapeTransform(rid, 0);
		var query = _hitQuery ??= new PhysicsShapeQueryParameters3D();
		query.ShapeRid = PhysicsServer3D.BodyGetShape(rid, 0);
		query.CollisionMask = body.CollisionMask;
		query.Exclude = GetMoverRids();
		var space = body.GetWorld3D().DirectSpaceState;
		double clearTime = fromTime;
		for (int i = (int)(fromTime * FallSteps / FallExtrapolateLimit); i < FallSteps && clearTime < toTime; i++)
		{
			double segmentStart = FallExtrapolateLimit * i / FallSteps;
			double segmentEnd = FallExtrapolateLimit * (i + 1) / FallSteps;
			Vector3 startPosition = from + velocity * (float)segmentStart + gravity * (float)(segmentStart * segmentStart / 2);
			Vector3 endPosition = from + velocity * (float)segmentEnd + gravity * (float)(segmentEnd * segmentEnd / 2);
			query.Transform = parent * new Transform3D(new Basis(rotation), startPosition) * shape;
			query.Motion = parent.Basis * (endPosition - startPosition);
			for (int tries = 0; tries < FallSteps; tries++)
			{
				float[] cast = space.CastMotion(query);
				if (cast[0] >= 1) break;
				if (!IsMoverHit(space, query, cast[1], rid))
				{
					hit = true;
					return segmentStart + (segmentEnd - segmentStart) * cast[0];
				}
			}
			clearTime = segmentEnd;
		}
		return clearTime;
	}

	private Godot.Collections.Array<Rid> GetMoverRids()
	{
		ulong frame = Engine.GetProcessFrames();
		if (frame == _moverFrame) return _moverRids;
		_moverFrame = frame;
		_moverRids = [];
		foreach (Dynamic mover in dyn.Root.Interpolating)
		{
			if (!mover.IsDeleted && mover is Physical { Anchored: false } && mover.GetNetSpeed() > MoverSpeed &&
				mover.GDNode3D is CollisionObject3D body && GodotObject.IsInstanceValid(body) && body.IsInsideTree())
			{
				_moverRids.Add(body.GetRid());
			}
		}
		return _moverRids;
	}

	private static bool IsMoverHit(PhysicsDirectSpaceState3D space, PhysicsShapeQueryParameters3D query, float reach, Rid self)
	{
		Transform3D castStart = query.Transform;
		query.Transform = castStart.Translated(query.Motion * reach);
		var hits = space.IntersectShape(query, 8);
		query.Transform = castStart;
		if (hits.Count == 0) return false;
		var exclude = query.Exclude;
		foreach (var hit in hits)
		{
			Rid rid = (Rid)hit["rid"];
			if (rid != self)
			{
				if (hit["collider"].AsGodotObject() is not Node node) return false;
				if (NetworkedObject.GetNetObjFromProxy(node) is not Physical physical ||
					physical.Anchored || physical.GetNetSpeed() <= MoverSpeed)
				{
					return false;
				}
			}
			exclude.Add(rid);
		}
		query.Exclude = exclude;
		return true;
	}

	public Vector3 GetLocalGravity()
	{
		if (dyn.GDNode3D is not Node3D node || !node.IsInsideTree()) return Vector3.Zero;
		if (dyn is NPC npc) return dyn.GetNetParentBasis().Inverse() * (npc.Vertical * dyn.Root.Environment.Gravity.Dot(npc.Vertical));
		Rid space = node.GetWorld3D().Space;
		ulong frame = Engine.GetProcessFrames();
		if (frame != _gravityFrame || space != _gravitySpace)
		{
			_gravityFrame = frame;
			_gravitySpace = space;
			Vector3 direction = PhysicsServer3D.AreaGetParam(space, PhysicsServer3D.AreaParameter.GravityVector).AsVector3();
			float strength = PhysicsServer3D.AreaGetParam(space, PhysicsServer3D.AreaParameter.Gravity).AsSingle();
			_spaceGravity = direction * strength;
		}
		float scale = node is RigidBody3D body ? body.GravityScale : 1f;
		return dyn.GetNetParentBasis().Inverse() * (_spaceGravity * scale);
	}

	private Vector3 RemoveFall(Vector3 velocity)
	{
		Vector3 down = GetLocalGravity().Normalized();
		float fallSpeed = velocity.Dot(down);
		return fallSpeed > 0 ? velocity - down * fallSpeed : velocity;
	}

	public readonly record struct Sample(double Time, Vector3 Position, Quaternion Rotation, Vector3 Velocity, bool Snap, Vector3 Spin);
}
