// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Enums;
using Polytoria.Scripting;
using Polytoria.Utils;
using System;
using System.Collections.Generic;

namespace Polytoria.Datamodel.Services;

[Static("Tween"), ExplorerExclude, SaveIgnore]
public sealed partial class TweenService : Instance
{
	private readonly Dictionary<int, WeakReference<Tween>> _legacyTweenIDs = [];
	private int _tweenCount = 0;

	private void CleanupTweens()
	{
		List<int> dead = [];

		foreach (var (twid, twr) in _legacyTweenIDs)
		{
			if (!twr.TryGetTarget(out _))
			{
				dead.Add(twid);
			}
		}

		foreach (int id in dead)
		{
			_legacyTweenIDs.Remove(id);
		}
	}

	[ScriptMethod]
	public TweenObject NewTween()
	{
		TweenObject tween = new()
		{
			tween = GDNode.CreateTween()
		};
		tween.Init();
		return tween;
	}

	private int RegisterTween(Tween tw)
	{
		CleanupTweens();

		_tweenCount++;
		int myID = _tweenCount;
		_legacyTweenIDs.Add(myID, new(tw));

		void f()
		{
			_legacyTweenIDs.Remove(myID);
			tw.Finished -= f;
		}

		tw.Finished += f;

		return myID;
	}

	private static void InitLeanEase(Tween tw, LeanTweenType tweenType)
	{
		Tween.TransitionType trans = Tween.TransitionType.Linear;
		Tween.EaseType ease = Tween.EaseType.InOut;

		switch (tweenType)
		{
			case LeanTweenType.linear:
				trans = Tween.TransitionType.Linear;
				ease = Tween.EaseType.InOut;
				break;
			case LeanTweenType.easeInQuad:
				trans = Tween.TransitionType.Quad;
				ease = Tween.EaseType.In;
				break;
			case LeanTweenType.easeOutQuad:
				trans = Tween.TransitionType.Quad;
				ease = Tween.EaseType.Out;
				break;
			case LeanTweenType.easeInOutQuad:
				trans = Tween.TransitionType.Quad;
				ease = Tween.EaseType.InOut;
				break;
			case LeanTweenType.easeInCubic:
				trans = Tween.TransitionType.Cubic;
				ease = Tween.EaseType.In;
				break;
			case LeanTweenType.easeOutCubic:
				trans = Tween.TransitionType.Cubic;
				ease = Tween.EaseType.Out;
				break;
			case LeanTweenType.easeInOutCubic:
				trans = Tween.TransitionType.Cubic;
				ease = Tween.EaseType.InOut;
				break;
			case LeanTweenType.easeInQuart:
				trans = Tween.TransitionType.Quart;
				ease = Tween.EaseType.In;
				break;
			case LeanTweenType.easeOutQuart:
				trans = Tween.TransitionType.Quart;
				ease = Tween.EaseType.Out;
				break;
			case LeanTweenType.easeInOutQuart:
				trans = Tween.TransitionType.Quart;
				ease = Tween.EaseType.InOut;
				break;
			case LeanTweenType.easeInQuint:
				trans = Tween.TransitionType.Quint;
				ease = Tween.EaseType.In;
				break;
			case LeanTweenType.easeOutQuint:
				trans = Tween.TransitionType.Quint;
				ease = Tween.EaseType.Out;
				break;
			case LeanTweenType.easeInOutQuint:
				trans = Tween.TransitionType.Quint;
				ease = Tween.EaseType.InOut;
				break;
			case LeanTweenType.easeInSine:
				trans = Tween.TransitionType.Sine;
				ease = Tween.EaseType.In;
				break;
			case LeanTweenType.easeOutSine:
				trans = Tween.TransitionType.Sine;
				ease = Tween.EaseType.Out;
				break;
			case LeanTweenType.easeInOutSine:
				trans = Tween.TransitionType.Sine;
				ease = Tween.EaseType.InOut;
				break;
			case LeanTweenType.easeInExpo:
				trans = Tween.TransitionType.Expo;
				ease = Tween.EaseType.In;
				break;
			case LeanTweenType.easeOutExpo:
				trans = Tween.TransitionType.Expo;
				ease = Tween.EaseType.Out;
				break;
			case LeanTweenType.easeInOutExpo:
				trans = Tween.TransitionType.Expo;
				ease = Tween.EaseType.InOut;
				break;
			case LeanTweenType.easeInCirc:
				trans = Tween.TransitionType.Circ;
				ease = Tween.EaseType.In;
				break;
			case LeanTweenType.easeOutCirc:
				trans = Tween.TransitionType.Circ;
				ease = Tween.EaseType.Out;
				break;
			case LeanTweenType.easeInOutCirc:
				trans = Tween.TransitionType.Circ;
				ease = Tween.EaseType.InOut;
				break;
			case LeanTweenType.easeInBounce:
				trans = Tween.TransitionType.Bounce;
				ease = Tween.EaseType.In;
				break;
			case LeanTweenType.easeOutBounce:
				trans = Tween.TransitionType.Bounce;
				ease = Tween.EaseType.Out;
				break;
			case LeanTweenType.easeInOutBounce:
				trans = Tween.TransitionType.Bounce;
				ease = Tween.EaseType.InOut;
				break;
			case LeanTweenType.easeInBack:
				trans = Tween.TransitionType.Back;
				ease = Tween.EaseType.In;
				break;
			case LeanTweenType.easeOutBack:
				trans = Tween.TransitionType.Back;
				ease = Tween.EaseType.Out;
				break;
			case LeanTweenType.easeInOutBack:
				trans = Tween.TransitionType.Back;
				ease = Tween.EaseType.InOut;
				break;
			case LeanTweenType.easeInElastic:
				trans = Tween.TransitionType.Elastic;
				ease = Tween.EaseType.In;
				break;
			case LeanTweenType.easeOutElastic:
				trans = Tween.TransitionType.Elastic;
				ease = Tween.EaseType.Out;
				break;
			case LeanTweenType.easeInOutElastic:
				trans = Tween.TransitionType.Elastic;
				ease = Tween.EaseType.InOut;
				break;
			case LeanTweenType.easeSpring:
				trans = Tween.TransitionType.Spring;
				ease = Tween.EaseType.Out;
				break;
		}

		tw.SetTrans(trans);
		tw.SetEase(ease);
	}


	[ScriptLegacyMethod("TweenPosition")]
	public int CompatTweenPosition(Dynamic target, Vector3 destination, float time, LeanTweenType tweenType = LeanTweenType.linear, PTCallback? callOnComplete = null)
	{
		return CompatTweenVector3(target.Position, destination, time, new((v3) =>
		{
			target.Position = (Vector3)v3[0]!;
		}), tweenType, callOnComplete);
	}

	[ScriptLegacyMethod("TweenRotation")]
	public int CompatTweenRotation(Dynamic target, Vector3 destination, float time, LeanTweenType tweenType = LeanTweenType.linear, PTCallback? callOnComplete = null)
	{
		Tween tw = GDNode.CreateTween();
		InitLeanEase(tw, tweenType);

		tw.TweenMethod(Callable.From((Quaternion val) =>
		{
			target.GDNode3D.Quaternion = val;
		}), target.GDNode3D.Quaternion, Quaternion.FromEuler(destination.FlipEuler()), time);

		void callComplete()
		{
			callOnComplete?.Invoke();
			tw.Finished -= callComplete;
		}

		tw.Finished += callComplete;

		return RegisterTween(tw);
	}

	[ScriptLegacyMethod("TweenSize")]
	public int CompatTweenSize(Dynamic target, Vector3 destination, float time, LeanTweenType tweenType = LeanTweenType.linear, PTCallback? callOnComplete = null)
	{
		return CompatTweenVector3(target.Size, destination, time, new((v3) =>
		{
			target.Size = (Vector3)v3[0]!;
		}), tweenType, callOnComplete);
	}

	[ScriptLegacyMethod("TweenNumber")]
	public int CompatTweenNumber(float from, float to, float time, PTCallback? callback, LeanTweenType tweenType = LeanTweenType.linear, PTCallback? callOnComplete = null)
	{
		Tween tw = GDNode.CreateTween();
		InitLeanEase(tw, tweenType);

		tw.TweenMethod(Callable.From((float val) =>
		{
			callback?.Invoke(val);
		}), from, to, time);

		void callComplete()
		{
			callOnComplete?.Invoke();
			tw.Finished -= callComplete;
		}

		tw.Finished += callComplete;

		return RegisterTween(tw);
	}

	[ScriptLegacyMethod("TweenColor")]
	public int CompatTweenColor(Color from, Color to, float time, PTCallback? callback, LeanTweenType tweenType = LeanTweenType.linear, PTCallback? callOnComplete = null)
	{
		Tween tw = GDNode.CreateTween();
		InitLeanEase(tw, tweenType);

		tw.TweenMethod(Callable.From((Color val) =>
		{
			callback?.Invoke(val);
		}), from, to, time);

		void callComplete()
		{
			callOnComplete?.Invoke();
			tw.Finished -= callComplete;
		}

		tw.Finished += callComplete;

		return RegisterTween(tw);
	}

	[ScriptLegacyMethod("TweenVector3")]
	public int CompatTweenVector3(Vector3 from, Vector3 to, float time, PTCallback? callback, LeanTweenType tweenType = LeanTweenType.linear, PTCallback? callOnComplete = null)
	{
		Tween tw = GDNode.CreateTween();
		InitLeanEase(tw, tweenType);

		tw.TweenMethod(Callable.From((Vector3 val) =>
		{
			callback?.Invoke(val);
		}), from, to, time);

		void callComplete()
		{
			callOnComplete?.Invoke();
			tw.Finished -= callComplete;
		}

		tw.Finished += callComplete;

		return RegisterTween(tw);
	}

	[ScriptLegacyMethod("TweenVector2")]
	public int CompatTweenVector2(Vector2 from, Vector2 to, float time, PTCallback? callback, LeanTweenType tweenType = LeanTweenType.linear, PTCallback? callOnComplete = null)
	{
		Tween tw = GDNode.CreateTween();
		InitLeanEase(tw, tweenType);

		tw.TweenMethod(Callable.From((Vector2 val) =>
		{
			callback?.Invoke(val);
		}), from, to, time);

		void callComplete()
		{
			callOnComplete?.Invoke();
			tw.Finished -= callComplete;
		}

		tw.Finished += callComplete;

		return RegisterTween(tw);
	}

	[ScriptLegacyMethod("Cancel")]
	public void CompatCancel(int id, bool _ = false)
	{
		if (_legacyTweenIDs.TryGetValue(id, out WeakReference<Tween>? wtw) && wtw.TryGetTarget(out var tw))
		{
			tw.Stop();
		}
		_legacyTweenIDs.Remove(id);
	}

	[ScriptLegacyMethod("CancelAll")]
	public void CompatCancelAll()
	{
		foreach ((int id, _) in _legacyTweenIDs)
		{
			CompatCancel(id);
		}
	}

	public class TweenObject : IScriptObject
	{
		internal Tween tween = null!;
		private bool _looped = false;
		private bool _parallel = true;
		private float _speedScale = 1;
		private TweenDirectionEnum _direction;
		private TweenTransitionEnum _transition;

		[ScriptProperty]
		public bool Looped
		{
			get => _looped;
			set
			{
				_looped = value;
				tween.SetLoops(_looped ? 0 : 1);
			}
		}

		[ScriptProperty]
		public bool Parallel
		{
			get => _parallel;
			set
			{
				_parallel = value;
				tween.SetParallel(_parallel);
			}
		}


		[ScriptProperty]
		public float SpeedScale
		{
			get => _speedScale;
			set
			{
				_speedScale = value;
				tween.SetSpeedScale(_speedScale);
			}
		}

		[ScriptProperty]
		public TweenDirectionEnum Direction
		{
			get => _direction;
			set
			{
				_direction = value;
				tween.SetEase(value switch
				{
					TweenDirectionEnum.In => Tween.EaseType.In,
					TweenDirectionEnum.Out => Tween.EaseType.Out,
					TweenDirectionEnum.InOut => Tween.EaseType.InOut,
					TweenDirectionEnum.OutIn => Tween.EaseType.OutIn,
					_ => throw new ArgumentOutOfRangeException(nameof(value), "Tween direction is out of range"),
				});
			}
		}

		[ScriptProperty]
		public TweenTransitionEnum Transition
		{
			get => _transition;
			set
			{
				_transition = value;
				tween.SetTrans(value switch
				{
					TweenTransitionEnum.Linear => Tween.TransitionType.Linear,
					TweenTransitionEnum.Sine => Tween.TransitionType.Sine,
					TweenTransitionEnum.Quint => Tween.TransitionType.Quint,
					TweenTransitionEnum.Quart => Tween.TransitionType.Quart,
					TweenTransitionEnum.Quad => Tween.TransitionType.Quad,
					TweenTransitionEnum.Expo => Tween.TransitionType.Expo,
					TweenTransitionEnum.Elastic => Tween.TransitionType.Elastic,
					TweenTransitionEnum.Cubic => Tween.TransitionType.Cubic,
					TweenTransitionEnum.Circ => Tween.TransitionType.Circ,
					TweenTransitionEnum.Bounce => Tween.TransitionType.Bounce,
					TweenTransitionEnum.Back => Tween.TransitionType.Back,
					TweenTransitionEnum.Spring => Tween.TransitionType.Spring,
					_ => throw new ArgumentOutOfRangeException(nameof(value), "Tween transition is out of range"),
				});
			}
		}

		[ScriptProperty] public bool IsRunning => tween.IsRunning();
		[ScriptProperty] public double ElapsedTime => tween.GetTotalElapsedTime();
		[ScriptProperty] public PTSignal Finished { get; private set; } = new();
		[ScriptProperty] public PTSignal Canceled { get; private set; } = new();

		public void Init()
		{
			Looped = false;
			Parallel = true;
			tween.Finished += () => { Finished.Invoke(); };
		}

		[ScriptMethod]
		public TweenObject SetDirection(TweenDirectionEnum dir)
		{
			Direction = dir;
			return this;
		}

		[ScriptMethod]
		public TweenObject SetTrans(TweenTransitionEnum trans)
		{
			Transition = trans;
			return this;
		}

		[ScriptMethod]
		public void TweenPosition(Dynamic target, Vector3 destination, float time)
		{
			TweenVector3(target.Position, destination, time, new((v3) =>
			{
				target.Position = (Vector3)v3[0]!;
			}));
		}

		[ScriptMethod]
		public void TweenRotation(Dynamic target, Vector3 destination, float time)
		{
			tween.TweenMethod(Callable.From((Quaternion val) =>
			{
				target.GDNode3D.Quaternion = val;
			}), target.GDNode3D.Quaternion, Quaternion.FromEuler(destination.FlipEuler()), time);
		}

		[ScriptMethod]
		public void TweenSize(Dynamic target, Vector3 destination, float time)
		{
			TweenVector3(target.Size, destination, time, new((v3) =>
			{
				target.Size = (Vector3)v3[0]!;
			}));
		}

		[ScriptMethod]
		public void TweenColor(Color from, Color to, float time, PTCallback callback)
		{
			tween.TweenMethod(Callable.From((Color val) =>
			{
				callback.Invoke(val);
			}), from, to, time);
		}

		[ScriptMethod]
		public void TweenNumber(float from, float to, float time, PTCallback callback)
		{
			tween.TweenMethod(Callable.From((float val) =>
			{
				callback.Invoke(val);
			}), from, to, time);
		}

		[ScriptMethod]
		public void TweenVector2(Vector2 from, Vector2 to, float time, PTCallback callback)
		{
			tween.TweenMethod(Callable.From((Vector2 val) =>
			{
				callback.Invoke(val);
			}), from, to, time);
		}

		[ScriptMethod]
		public void TweenVector3(Vector3 from, Vector3 to, float time, PTCallback callback)
		{
			tween.TweenMethod(Callable.From((Vector3 val) =>
			{
				callback.Invoke(val);
			}), from, to, time);
		}

		[ScriptMethod]
		public void TweenQuaternion(Quaternion from, Quaternion to, float time, PTCallback callback)
		{
			tween.TweenMethod(Callable.From((Quaternion val) =>
			{
				callback.Invoke(val);
			}), from, to, time);
		}

		[ScriptMethod]
		public void Play()
		{
			tween.Play();
		}


		[ScriptMethod]
		public void Pause()
		{
			tween.Pause();
		}

		[ScriptMethod]
		public void Stop()
		{
			tween.Stop();
		}

		[ScriptMethod]
		public void Interval(float sec)
		{
			tween.TweenInterval(sec);
		}

		[ScriptMethod]
		public TweenObject Chain()
		{
			tween.Chain();
			return this;
		}

		[ScriptMethod]
		public void Cancel(bool callFinished = false)
		{
			tween.Kill();
			Canceled.Invoke();
			if (callFinished)
			{
				Finished.Invoke();
			}
		}
	}

	[ScriptEnum]
	public enum TweenTransitionEnum
	{
		Linear,
		Sine,
		Quint,
		Quart,
		Quad,
		Expo,
		Elastic,
		Cubic,
		Circ,
		Bounce,
		Back,
		Spring
	}

	[ScriptEnum]
	public enum TweenDirectionEnum
	{
		In,
		Out,
		InOut,
		OutIn
	}
}
