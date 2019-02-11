using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Diagnostics.Runtime;
using DynaMD;

namespace ClrSpy
{
    public class NetFrameworkClrDriver : ClrDriver
    {
        protected readonly ClrType typeTimerQueue, typeTimerQueueTimer, typeMoveNextRunner;
        protected readonly ClrStaticField fieldSQueue;
        protected readonly ClrInstanceField fieldTimers, fieldNext, fieldState, fieldDelayPromiseContinuationObject, fieldStateMachine;

        protected override IEnumerable<ulong> EnumerateThreadPoolWorkQueue(ulong workQueueRef)
        {
            var queue = heap.GetProxy(workQueueRef);
            var tail = queue.queueTail;

            for (; tail != null; tail = tail.Next) {
                var indexes = (int)tail.indexes;
                var lower = indexes & 0xFFFF;
                var upper = (indexes >> 16) & 0xFFFF;
                var nodes = tail.nodes;
                for (int i = lower; i < upper; ++i) {
                    var node = nodes[i];
                    yield return (ulong)node;
                }
            }

            var typeQueue = heap.GetObjectType(workQueueRef);
            var fieldAllThreadQueues = typeQueue.GetStaticFieldByName("allThreadQueues");
            var allThreadQueues = heap.GetProxy((ulong)fieldAllThreadQueues.GetValue(domain));

            var marray = allThreadQueues.m_array;
            int marrayLen = marray.Length;
            for (int i = 0; i < marrayLen; ++i) {
                var workStealingQueue = marray[i];
                if (workStealingQueue != null) {
                    var subArray = workStealingQueue.m_array;;
                    var subArrayLen = subArray.Length;
                    for (int j = 0; j < subArrayLen; ++j) {
                        var node = subArray[j];
                        if (node != null) {
                            yield return (ulong)node;
                        }
                    }
                }
            }
        }

        public override IEnumerable<ulong> EnumerateTimerTasks()
        {
            if (fieldSQueue.IsInitialized(domain)) {
                var timeQueue = (ulong)fieldSQueue.GetValue(domain);
                for (ulong timer = (ulong)fieldTimers.GetValue(timeQueue); timer != 0; timer = (ulong)fieldNext.GetValue(timer)) {
                    var state = (ulong)fieldState.GetValue(timer);
                    if (state != 0) {
                        var typeState = heap.GetObjectType(state);
                        if (typeState == typeDelayPromise) {
                            var continuation = (ulong)fieldDelayPromiseContinuationObject.GetValue(state);
                            var target = (ulong)fieldDelegateTarget.GetValue(continuation);
                            var stateMachine = (ulong)fieldStateMachine.GetValue(target);
                            yield return stateMachine;
                        }
                    }
                }
            }
        }

        public NetFrameworkClrDriver(ClrRuntime runtime) : base(runtime)
        {
            typeTimerQueue = heap.GetTypeByName("System.Threading.TimerQueue");
            fieldSQueue = typeTimerQueue.GetStaticFieldByName("s_queue");
            fieldTimers = typeTimerQueue.GetFieldByName("m_timers");
            typeTimerQueueTimer = heap.GetTypeByName("System.Threading.TimerQueueTimer");
            fieldNext = typeTimerQueueTimer.GetFieldByName("m_next");
            fieldState = typeTimerQueueTimer.GetFieldByName("m_state");
            fieldDelayPromiseContinuationObject = typeDelayPromise.GetFieldByName("m_continuationObject");
            typeMoveNextRunner = heap.GetTypeByName("System.Runtime.CompilerServices.AsyncMethodBuilderCore+MoveNextRunner");
            fieldStateMachine = typeMoveNextRunner.GetFieldByName("m_stateMachine");
        }
    }
}
