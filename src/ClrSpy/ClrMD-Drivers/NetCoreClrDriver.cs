using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Diagnostics.Runtime;

namespace ClrSpy
{
    public class NetCoreClrDriver : ClrDriver
    {
        private IEnumerable<ulong> EnumerateConcurrentQueue(ulong queueAddr)
        {
            var types = heap.EnumerateTypes();
            var typeQueue = heap.GetObjectType(queueAddr);
            var fieldHead = typeQueue.GetFieldByName("_head");
            var fieldTail = typeQueue.GetFieldByName("_tail");
            var head = (ulong)fieldHead.GetValue(queueAddr);
            var tail = (ulong)fieldTail.GetValue(queueAddr);

            var typeSegment = heap.GetObjectType(head);
            var fieldNextSegment = typeSegment.GetFieldByName("_nextSegment");
            var fieldHeadAndTail = typeSegment.GetFieldByName("_headAndTail");
            var fieldSlots = typeSegment.GetFieldByName("_slots");
            var fieldSlotsMask = typeSegment.GetFieldByName("_slotsMask");

            var typeHeadAndTail = heap.GetTypeByName("System.Collections.Concurrent.PaddedHeadAndTail");
            var fieldSegmentHead = typeHeadAndTail.GetFieldByName("Head");
            var fieldSegmentTail = typeHeadAndTail.GetFieldByName("Tail");

            var typeSlot = heap.GetTypeByName("System.Collections.Concurrent.ConcurrentQueueSegment+Slot");
            var fieldItem = typeSlot.GetFieldByName("Item");

            for (; ; head = (ulong)fieldNextSegment.GetValue(head)) {
                var slots = (ulong)fieldSlots.GetValue(head);
                var slotsType = heap.GetObjectType(slots);
                var slotsMask = (int)fieldSlotsMask.GetValue(head);
                var len = slotsType.GetArrayLength(slots);

                var headAndTail = (ulong)fieldHeadAndTail.GetValue(head);

                for (int h = (int)fieldSegmentHead.GetValue(headAndTail, true), t = (int)fieldSegmentTail.GetValue(headAndTail, true); h != t; h = (h + 1) % len) {
                    var slot = slotsType.GetArrayElementAddress(slots, h);
                    var item = (ulong)fieldItem.GetValue(slot, true);
                    yield return item;
                }

                if (head == tail)
                    break;
            }
        }

        protected override IEnumerable<ulong> EnumerateThreadPoolWorkQueue(ulong workQueueRef)
        {
            var typeQueue = heap.GetObjectType(workQueueRef);
            var fieldWorkItems = typeQueue.GetFieldByName("workItems");
            return EnumerateConcurrentQueue((ulong)fieldWorkItems.GetValue(workQueueRef));
        }

        public NetCoreClrDriver(ClrRuntime runtime) : base(runtime) {}
    }
}
