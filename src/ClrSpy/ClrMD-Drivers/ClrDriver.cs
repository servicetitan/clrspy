using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Diagnostics.Runtime;

#nullable enable
namespace ClrSpy
{
    public struct ObjectInfo
    {
        public ulong Address;
        public ClrType Type;
    }

    public class TaskInfo
    {
        public ulong Address;
        public string? MethodName;
    }

    public interface IClrDriver
    {
        IEnumerable<ObjectInfo> EnumerateManagedWorkItems();
        IEnumerable<ObjectInfo> EnumerateTimerTasks();
        IEnumerable<ulong> EnumerateStackTasks();
        TaskInfo? GetTaskInfo(ObjectInfo oi);
        bool IsTaskDescendant(ClrType type);
        bool IsDelegateDescendant(ClrType type);
    }

    public abstract class ClrDriver : IClrDriver
    {
        protected readonly ClrRuntime runtime;
        protected readonly ClrHeap heap;
        protected readonly ClrAppDomain domain;

        protected readonly ClrType typeObject, typeTask, typeDelegate, typeDelayPromise, typeQueueUserWorkItemCallback, typeWaitCallback;
        protected readonly ClrInstanceField fieldDelegateTarget, fieldTaskAction, fieldTaskScheduler, fieldTaskContinuationObject, fieldCallback;

        protected abstract IEnumerable<ulong> EnumerateThreadPoolWorkQueue(ulong workQueueRef);

        private readonly Dictionary<ClrType, bool> isTaskByType = new Dictionary<ClrType, bool>();

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

        public IEnumerable<ObjectInfo> EnumerateManagedWorkItems()
        {
            ClrStaticField workQueueField = ClrMdUtils.GetCorlib(runtime).GetTypeByName("System.Threading.ThreadPoolGlobals")?.GetStaticFieldByName("workQueue");
            if (workQueueField != null) {
                object workQueueValue = workQueueField.GetValue(domain);
                ulong workQueueRef = (workQueueValue == null) ? 0L : (ulong)workQueueValue;
                if (workQueueRef != 0) {
                    ClrType workQueueType = heap.GetObjectType(workQueueRef);                   // should be System.Threading.ThreadPoolWorkQueue
                    if (workQueueType?.Name == "System.Threading.ThreadPoolWorkQueue") {
                        foreach (var item in EnumerateThreadPoolWorkQueue(workQueueRef)) {
                            yield return new ObjectInfo { Address = item, Type = heap.GetObjectType(item) };
                        }
                    }
                }
            }
        }

        public virtual IEnumerable<ObjectInfo> EnumerateTimerTasks() => throw new NotImplementedException();

        protected string? BuildDelegateMethodName(ClrType targetType, ulong action)
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

        public bool IsTaskDescendant(ClrType type)
        {
            if (!isTaskByType.TryGetValue(type, out var r)) {
                r = type != typeObject && (type == typeTask || IsTaskDescendant(type.BaseType));
                isTaskByType.Add(type, r);
            }
            return r;
        }

        public  bool IsDelegateDescendant(ClrType type)
            => type != typeObject && (type == typeDelegate || IsDelegateDescendant(type.BaseType));

        protected virtual ulong GetTaskAction(ulong task)
        {
            var r = (ulong)fieldTaskAction.GetValue(task);
            if (r == 0) {
                var cont = (ulong)fieldTaskContinuationObject.GetValue(task);
                if (cont != 0) {
                    var typeCont = heap.GetObjectType(cont);
                    if (IsDelegateDescendant(typeCont)) {
                        r = cont;
                    }
                }
            }
            return r;
        }

        protected string? BuildMethodNameFromDelegate(ulong action, ulong task = 0)
        {
            string? r = "[no action]";
            if (action != 0) {
                var target = (ulong)fieldDelegateTarget.GetValue(action);
                if (target == 0) {
                    r = "[no target]";
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

        protected TaskInfo? GetTask(ObjectInfo oi)
        {
            TaskInfo tpi = new TaskInfo() {
                Address = oi.Address,
            };

            // look for the context in m_action._target
            var action = GetTaskAction(oi.Address);
            if (action == 0)
                return null;

            tpi.MethodName = BuildMethodNameFromDelegate(action, oi.Address);
            return tpi;
        }

        private TaskInfo GetQueueUserWorkItemCallback(ObjectInfo oi)
        {
            TaskInfo tpi = new TaskInfo() {
                Address = oi.Address,
            };

            // look for the callback given to ThreadPool.QueueUserWorkItem()
            var callback = (ulong)fieldCallback.GetValue(oi.Address);
            tpi.MethodName = BuildMethodNameFromDelegate(callback);
            return tpi;
        }

        public TaskInfo? GetTaskInfo(ObjectInfo oi)
        {
            var typeName = oi.Type.Name;
            switch (typeName) {
                case "System.Threading.Tasks.Task":
                case "System.Threading.Tasks.Task+DelayPromise":
                    return GetTask(oi);
                case "System.Threading.QueueUserWorkItemCallback":
                    return GetQueueUserWorkItemCallback(oi);
                default:
                    if (typeName.StartsWith("System.Threading.Tasks.Task<")) {
                        return GetTask(oi);
                    }
                    else {
                        return new TaskInfo() { Address = oi.Address, MethodName = typeName };
                    }
            }
        }

        public ClrDriver(ClrRuntime runtime)
        {
            (this.runtime, heap, domain) = (runtime, runtime.Heap, runtime.AppDomains[0]);

            typeObject = heap.GetTypeByName("System.Object");
            typeDelegate = heap.GetTypeByName("System.Delegate");
            fieldDelegateTarget = typeDelegate.GetFieldByName("_target");

            typeTask = heap.GetTypeByName("System.Threading.Tasks.Task");
            if (typeTask.BaseType.Name == "System.Threading.Tasks.Task")
                typeTask = typeTask.BaseType;
            fieldTaskAction = typeTask.GetFieldByName("m_action");
            fieldTaskScheduler = typeTask.GetFieldByName("m_taskScheduler");
            fieldTaskContinuationObject = typeTask.GetFieldByName("m_continuationObject");

            typeDelayPromise = heap.GetTypeByName("System.Threading.Tasks.Task+DelayPromise");
            typeQueueUserWorkItemCallback = heap.GetTypeByName("System.Threading.QueueUserWorkItemCallback");
            fieldCallback = typeQueueUserWorkItemCallback.GetFieldByName("callback");
            typeWaitCallback = heap.GetTypeByName("System.Threading.WaitCallback");
        }
    }
}
