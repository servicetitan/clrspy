using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Diagnostics.Runtime;

namespace ClrSpy
{
    public class ThreadPoolItem
    {
        public ulong Address;
        public string MethodName;
    }

    public interface IClrDriver
    {
        IEnumerable<ulong> EnumerateManagedWorkItems();
        IEnumerable<ulong> EnumerateTimerTasks();
        IEnumerable<ulong> EnumerateStackTasks();
        ThreadPoolItem GetThreadPoolItem(ulong item);
    }

    public abstract class ClrDriver : IClrDriver
    {
        protected readonly ClrRuntime runtime;
        protected readonly ClrHeap heap;
        protected readonly ClrAppDomain domain;

        protected readonly ClrType typeTask, typeDelegate, typeDelayPromise;
        protected readonly ClrInstanceField fieldDelegateTarget, fieldTaskAction, fieldTaskScheduler;

        protected abstract IEnumerable<ulong> EnumerateThreadPoolWorkQueue(ulong workQueueRef);

        public IEnumerable<ulong> EnumerateStackTasks()
        {
            var set = new HashSet<ulong>();

            foreach (var thread in runtime.Threads) {
                foreach (var root in thread.EnumerateStackObjects().Where(o => o.Kind == GCRootKind.LocalVar)) {
                    if (set.Add(root.Object)) {
                        var type = heap.GetObjectType(root.Object);
                        if (type.Name.Contains("System.Threading.Tasks.Task")) {
                            yield return root.Object;
                        }
                    }
                }
            }
        }

        public IEnumerable<ulong> EnumerateManagedWorkItems()
        {
            ClrStaticField workQueueField = ClrMdUtils.GetCorlib(runtime).GetTypeByName("System.Threading.ThreadPoolGlobals")?.GetStaticFieldByName("workQueue");
            if (workQueueField != null) {
                object workQueueValue = workQueueField.GetValue(domain);
                ulong workQueueRef = (workQueueValue == null) ? 0L : (ulong)workQueueValue;
                if (workQueueRef != 0) {
                    ClrType workQueueType = heap.GetObjectType(workQueueRef);                   // should be System.Threading.ThreadPoolWorkQueue
                    if (workQueueType?.Name == "System.Threading.ThreadPoolWorkQueue") {
                        foreach (var item in EnumerateThreadPoolWorkQueue(workQueueRef)) {
                            yield return item;
                        }
                    }
                }
            }
        }

        public virtual IEnumerable<ulong> EnumerateTimerTasks() => throw new NotImplementedException();

        protected string BuildDelegateMethodName(ClrType targetType, ulong action)
        {
            var typeAction = heap.GetObjectType(action);
            var fieldMethodPtr = typeAction.GetFieldByName("_methodPtr");
            var fieldMethodPtrAux = typeAction.GetFieldByName("_methodPtrAux");
            var methodPtr = (ulong)(long)fieldMethodPtr.GetValue(action);
            if (methodPtr == 0)
                return null;

            ClrMethod method = runtime.GetMethodByAddress(methodPtr)
                ?? runtime.GetMethodByAddress((ulong)(long)fieldMethodPtrAux.GetValue(action));     // could happen in case of static method

            // anonymous method
            var methodTypeName = method.Type.Name;
            var targetTypeName = targetType.Name;
            return (methodTypeName != targetTypeName
                    && targetTypeName != "System.Threading.WaitCallback"
                    && !targetTypeName.StartsWith("System.Action<")  // method is implemented by an class inherited from targetType ... or a simple delegate indirection to a static/instance method
                        ? $"({targetTypeName})" : "")
                + $"{methodTypeName}.{method.Name}";
        }

        protected virtual ulong GetTaskAction(ulong task) =>
            (ulong)fieldTaskAction.GetValue(task);

        protected string BuildMethodNameFromDelegate(ulong action, ulong task = 0)
        {
            var r = " [no action]";
            if (action != 0) {
                var target = (ulong)fieldDelegateTarget.GetValue(action);
                if (target == 0) {
                    r = " [no target]";
                }
                else {
                    r = BuildDelegateMethodName(heap.GetObjectType(target), action);
                    if (r == "System.Runtime.CompilerServices.AsyncMethodBuilderCore+MoveNextRunner.Run") {
                        var fieldStateMachine = heap.GetObjectType(target).GetFieldByName("m_stateMachine");
                        var stateMachine = (ulong)fieldStateMachine.GetValue(target);
                        var typeStateMachine = heap.GetObjectType(stateMachine);
                        r = typeStateMachine.Name;
                    }
                    else if (task != 0) {
                        // get the task scheduler if any
                        var scheduler = (ulong)fieldTaskScheduler.GetValue(task);
                        if (scheduler != 0) {
                            var schedulerTypeName = heap.GetObjectType(scheduler).ToString();
                            if (schedulerTypeName != "System.Threading.Tasks.ThreadPoolTaskScheduler")
                                r = $"{r} [{schedulerTypeName}]";
                        }
                    }
                }
            }
            return r;
        }

        protected ThreadPoolItem GetTask(ulong task)
        {
            ThreadPoolItem tpi = new ThreadPoolItem() {
                Address = task,
            };

            // look for the context in m_action._target
            var action = GetTaskAction(task);
            tpi.MethodName = BuildMethodNameFromDelegate(action, task);
            return tpi;
        }

        private ThreadPoolItem GetQueueUserWorkItemCallback(ulong wi)
        {
            var typeQueueUserWorkItemCallback = heap.GetTypeByName("System.Threading.QueueUserWorkItemCallback");
            var fieldCallback = typeQueueUserWorkItemCallback.GetFieldByName("callback");
            var typeWaitCallback = heap.GetTypeByName("System.Threading.WaitCallback");


            ThreadPoolItem tpi = new ThreadPoolItem() {
                Address = wi,
            };

            // look for the callback given to ThreadPool.QueueUserWorkItem()
            var callback = (ulong)fieldCallback.GetValue(wi);
            tpi.MethodName = BuildMethodNameFromDelegate(callback);
            return tpi;
        }

        public ThreadPoolItem GetThreadPoolItem(ulong item)
        {
            var itemType = heap.GetObjectType(item);

            switch (itemType.Name) {
                case "System.Threading.Tasks.Task":
                case "System.Threading.Tasks.Task+DelayPromise":
                    return GetTask(item);
                case "System.Threading.QueueUserWorkItemCallback":
                    return GetQueueUserWorkItemCallback(item);
                default:
                    if (itemType.Name.StartsWith("System.Threading.Tasks.Task<")) {
                        return GetTask(item);
                    }
                    else {
                        return new ThreadPoolItem() { Address = item, MethodName = itemType.Name };
                    }
            }
        }

        public ClrDriver(ClrRuntime runtime)
        {
            (this.runtime, heap, domain) = (runtime, runtime.Heap, runtime.AppDomains[0]);

            typeDelegate = heap.GetTypeByName("System.Delegate");
            fieldDelegateTarget = typeDelegate.GetFieldByName("_target");
            typeTask = heap.GetTypeByName("System.Threading.Tasks.Task");
            fieldTaskAction = typeTask.GetFieldByName("m_action");
            fieldTaskScheduler = typeTask.GetFieldByName("m_taskScheduler");
            typeDelayPromise = heap.GetTypeByName("System.Threading.Tasks.Task+DelayPromise");
        }
    }
}
