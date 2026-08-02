// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Scripting;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Polytoria.Tests;

public class PTSignalTest
{
	private static (PTCallback cb, List<object?[]> calls) MakeCallback()
	{
		List<object?[]> calls = [];
		PTCallback cb = new(calls.Add);
		return (cb, calls);
	}

	[Fact]
	public void Invoke_NoSubscribers_DoesNotThrow()
	{
		PTSignal signal = new();
		var ex = Record.Exception(() => signal.Invoke("hello"));
		Assert.Null(ex);
	}

	[Fact]
	public void Invoke_CallsConnectedCallback()
	{
		PTSignal signal = new();
		var (cb, calls) = MakeCallback();

		signal.Connect(cb);
		signal.Invoke("arg1", 42);

		Assert.Single(calls);
		Assert.Equal(["arg1", 42], calls[0]);
	}

	[Fact]
	public void Invoke_CallsMultipleCallbacksInOrder()
	{
		PTSignal signal = new();
		List<int> order = [];

		PTCallback cb1 = new(_ => order.Add(1));
		PTCallback cb2 = new(_ => order.Add(2));
		PTCallback cb3 = new(_ => order.Add(3));

		signal.Connect(cb1);
		signal.Connect(cb2);
		signal.Connect(cb3);
		signal.Invoke();

		// InvokeDirect iterates in reverse — all three must fire
		Assert.Equal(3, order.Count);
		Assert.Contains(1, order);
		Assert.Contains(2, order);
		Assert.Contains(3, order);
	}

	[Fact]
	public void Invoke_NullArgs_TreatedAsEmptyArray()
	{
		PTSignal signal = new();
		var (cb, calls) = MakeCallback();

		signal.Connect(cb);
		signal.Invoke(null); // passes null → converted to []

		Assert.Single(calls);
	}

	[Fact]
	public void Connect_SameCallbackTwice_NotDuplicated()
	{
		PTSignal signal = new();
		int invocations = 0;
		PTCallback cb = new(_ => invocations++);

		signal.Connect(cb);
		signal.Connect(cb); // duplicate — should be ignored
		signal.Invoke();

		Assert.Equal(1, invocations);
	}

	[Fact]
	public void Connect_RaisesSubscribedEvent()
	{
		PTSignal signal = new();
		int raised = 0;
		signal.Subscribed += () => raised++;

		PTCallback cb = new(_ => { });
		signal.Connect(cb);

		Assert.Equal(1, raised);
	}

	[Fact]
	public void Connect_DuplicateCallback_DoesNotRaiseSubscribedEvent()
	{
		PTSignal signal = new();
		int raised = 0;
		signal.Subscribed += () => raised++;

		PTCallback cb = new(_ => { });
		signal.Connect(cb);
		signal.Connect(cb);

		Assert.Equal(1, raised);
	}

	[Fact]
	public void Connect_ActionOverload_InvokesWhenSignalFires()
	{
		PTSignal signal = new();
		bool fired = false;
		signal.Connect(() => fired = true);
		signal.Invoke();
		Assert.True(fired);
	}

	[Fact]
	public void Connect_ActionObjectOverload_PassesFirstArg()
	{
		PTSignal signal = new();
		object? received = null;
		signal.Connect(arg => received = arg);
		signal.Invoke("hello");
		Assert.Equal("hello", received);
	}


	[Fact]
	public void Disconnect_RemovesCallback_StopsInvocation()
	{
		PTSignal signal = new();
		int count = 0;
		PTCallback cb = new(_ => count++);

		signal.Connect(cb);
		signal.Disconnect(cb);
		signal.Invoke();

		Assert.Equal(0, count);
	}

	[Fact]
	public void Disconnect_RaisesUnsubscribedEvent()
	{
		PTSignal signal = new();
		int raised = 0;
		signal.Unsubscribed += () => raised++;

		PTCallback cb = new(_ => { });
		signal.Connect(cb);
		signal.Disconnect(cb);

		Assert.Equal(1, raised);
	}

	[Fact]
	public void Disconnect_UnknownCallback_DoesNotThrow()
	{
		PTSignal signal = new();
		PTCallback cb = new(_ => { });

		var ex = Record.Exception(() => signal.Disconnect(cb));
		Assert.Null(ex);
	}

	[Fact]
	public void Disconnect_ViaConnection_RemovesCallback()
	{
		PTSignal signal = new();
		int count = 0;
		PTCallback cb = new(_ => count++);

		var conn = signal.Connect(cb);
		conn.Disconnect();
		signal.Invoke();

		Assert.Equal(0, count);
	}

	[Fact]
	public void Disconnect_ActionOverload_RemovesCorrectCallback()
	{
		PTSignal signal = new();
		int countA = 0, countB = 0;

		void a() => countA++;
		void b() => countB++;

		signal.Connect(a);
		signal.Connect(b);
		signal.Disconnect(a);
		signal.Invoke();

		Assert.Equal(0, countA);
		Assert.Equal(1, countB);
	}

	[Fact]
	public void Once_PTCallback_FiresOnlyOnFirstInvoke()
	{
		PTSignal signal = new();
		int count = 0;
		PTCallback cb = new(_ => count++);

		signal.Once(cb);
		signal.Invoke();
		signal.Invoke();
		signal.Invoke();

		Assert.Equal(1, count);
	}

	[Fact]
	public void Once_Action_FiresOnlyOnce()
	{
		PTSignal signal = new();
		int count = 0;
		signal.Once(_ => count++);

		signal.Invoke("x");
		signal.Invoke("y");

		Assert.Equal(1, count);
	}

	[Fact]
	public void Once_PassesCorrectArgs()
	{
		PTSignal signal = new();
		object? got = null;
		signal.Once(arg => got = arg);
		signal.Invoke("expected");
		Assert.Equal("expected", got);
	}

	[Fact]
	public async Task Wait_ReturnsArgsOnNextInvoke()
	{
		PTSignal signal = new();

		Task<object?[]> waitTask = signal.Wait();

		// Fire the signal from another context
		await Task.Run(() => signal.Invoke("a", "b"), TestContext.Current.CancellationToken);

		object?[] result = await waitTask;
		Assert.Equal(["a", "b"], result);
	}

	[Fact]
	public async Task Wait_WithNoArgs_ReturnsEmptyArray()
	{
		PTSignal signal = new();
		Task<object?[]> waitTask = signal.Wait();
		await Task.Run(() => signal.Invoke(), TestContext.Current.CancellationToken);
		object?[] result = await waitTask;
		Assert.Empty(result);
	}

	[Fact]
	public async Task Wait_OnlyResolvesOnce()
	{
		PTSignal signal = new();
		Task<object?[]> waitTask = signal.Wait();

		signal.Invoke("first");
		signal.Invoke("second");

		object?[] result = await waitTask;
		Assert.Equal("first", result[0]);
	}

	[Fact]
	public void DisconnectAll_StopsAllCallbacks()
	{
		PTSignal signal = new();
		int count = 0;

		signal.Connect(new PTCallback(_ => count++));
		signal.Connect(new PTCallback(_ => count++));
		signal.Connect(new PTCallback(_ => count++));

		signal.DisconnectAll();
		signal.Invoke();

		Assert.Equal(0, count);
	}

	[Fact]
	public void DisconnectAll_ThenConnect_WorksNormally()
	{
		PTSignal signal = new();
		int count = 0;

		signal.Connect(new PTCallback(_ => count++));
		signal.DisconnectAll();

		signal.Connect(new PTCallback(_ => count++));
		signal.Invoke();

		Assert.Equal(1, count);
	}

	[Fact]
	public void Invoke_SkipsDisposedCallbacks_WithoutThrowing()
	{
		PTSignal signal = new();
		int count = 0;

		var goodCb = new PTCallback(_ => count++);
		var disposedCb = new PTCallback(_ => count++);

		signal.Connect(goodCb);
		signal.Connect(disposedCb);

		disposedCb.Dispose(); // mark as disposed before invoke

		var ex = Record.Exception(() => signal.Invoke());
		Assert.Null(ex);
		Assert.Equal(1, count); // only goodCb should have fired
	}

	[Fact]
	public void GenericSubclasses_WorkLikePTSignal()
	{
		PTSignal<string> signal = new();
		int count = 0;
		signal.Connect(new PTCallback(_ => count++));
		signal.Invoke("hello");
		Assert.Equal(1, count);
	}

	[Fact]
	public void ToString_ReturnsExpectedString()
	{
		string result = PTSignal.ToString(null);
		Assert.Equal("<PTSignal>", result);
	}
}
