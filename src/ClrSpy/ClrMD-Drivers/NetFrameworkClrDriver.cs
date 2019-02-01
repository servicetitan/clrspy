using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Diagnostics.Runtime;

namespace ClrSpy
{
    public class NetFrameworkClrDriver : ClrDriver
    {
        /*
        protected override ulong GetTaskAction(ulong task)
        {
            var typeTask = heap.GetTypeByName("System.Threading.Tasks.Task");
            var fieldTaskContinuationObject = typeTask.GetFieldByName("m_continuationObject");
            return (ulong)fieldTaskContinuationObject.GetValue(task);
        }*/

        protected override IEnumerable<ulong> EnumerateThreadPoolWorkQueue(ulong workQueueRef)
        {
            var typeQueue = heap.GetObjectType(workQueueRef);
            var fieldTail = typeQueue.GetFieldByName("queueTail");
            var tail = (ulong)fieldTail.GetValue(workQueueRef);

            var typeSegment = heap.GetObjectType(tail);
            var fieldNext = typeSegment.GetFieldByName("Next");
            var fieldIndexes = typeSegment.GetFieldByName("indexes");
            var fieldNodes = typeSegment.GetFieldByName("nodes");

            for (; tail != 0; tail = (ulong)fieldNext.GetValue(tail)) {
                var indexes = (int)fieldIndexes.GetValue(tail);
                var lower = indexes & 0xFFFF;
                var upper = (indexes >> 16) & 0xFFFF;
                var nodes = (ulong)fieldNodes.GetValue(tail);
                var typeNodes = heap.GetObjectType(nodes);
                for (int i = lower; i < upper; ++i) {
                    var node = (ulong)typeNodes.GetArrayElementValue(nodes, i);
                    yield return node;
                }
            }

            var fieldAllThreadQueues = typeQueue.GetStaticFieldByName("allThreadQueues");
            var allThreadQueues = (ulong)fieldAllThreadQueues.GetValue(domain);
            var typeSparseArray = heap.GetObjectType(allThreadQueues);
            var fieldMArray = typeSparseArray.GetFieldByName("m_array");
            var marray = (ulong)fieldMArray.GetValue(allThreadQueues);
            var typeMArray = heap.GetObjectType(marray);
            var marrayLen = typeMArray.GetArrayLength(marray);

            for (int i = 0; i < marrayLen; ++i) {
                var workStealingQueue = (ulong)typeMArray.GetArrayElementValue(marray, i);
                if (workStealingQueue != 0) {
                    var typeWorkStealingQueue = heap.GetObjectType(workStealingQueue);
                    var fieldSubArray = typeWorkStealingQueue.GetFieldByName("m_array");
                    var subArray = (ulong)fieldSubArray.GetValue(workStealingQueue);
                    var typeSubMArray = heap.GetObjectType(subArray);
                    var subArrayLen = typeMArray.GetArrayLength(subArray);

                    for (int j = 0; j < subArrayLen; ++j) {
                        var node = (ulong)typeSubMArray.GetArrayElementValue(subArray, j);
                        if (node != 0) {
                            yield return node;
                        }
                    }
                }
            }
        }

        public override IEnumerable<ulong> EnumerateTimerTasks()
        {
            var typeTimerQueue = heap.GetTypeByName("System.Threading.TimerQueue");
            var typeTimerQueueTimer = heap.GetTypeByName("System.Threading.TimerQueueTimer");
            var fieldSQueue = typeTimerQueue.GetStaticFieldByName("s_queue");

            var fieldTimers = typeTimerQueue.GetFieldByName("m_timers");
            var fieldNext = typeTimerQueueTimer.GetFieldByName("m_next");
            var fieldState = typeTimerQueueTimer.GetFieldByName("m_state");

            if (fieldSQueue.IsInitialized(domain)) {
                var timeQueue = (ulong)fieldSQueue.GetValue(domain);
                for (ulong timer = (ulong)fieldTimers.GetValue(timeQueue); timer != 0; timer = (ulong)fieldNext.GetValue(timer)) {
                    var state = (ulong)fieldState.GetValue(timer);
                    yield return state;
                }
            }
        }

        public NetFrameworkClrDriver(ClrRuntime runtime) : base(runtime) {}
    }
}
