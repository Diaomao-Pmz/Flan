using System.Collections.Generic;
using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【通用预输入缓存】
    ///
    /// ==========================================================
    /// 这个类解决的是「吞键」——ACT 手感最容易翻车的地方。
    ///
    /// 场景：玩家在落地前 0.1 秒按下滑铲。
    ///   没有缓存 → 那一刻还在空中，指令被丢弃，落地后什么都没发生，
    ///              玩家会觉得"我明明按了"。
    ///   有缓存   → 指令进队列等着，落地那一帧闸门一开，立刻消费。
    ///
    /// 原先只有攻击键享受这个待遇（ComboInputBuffer 里的 hasBufferedInput），
    /// 跳/冲/铲全都没有。所以滑铲的手感明显比跳跃差一档。
    /// 现在所有指令一视同仁。
    ///
    /// 比喻：从"客人来的时候前台不在就让他走"，
    ///       改成"留个便条，前台回来立刻叫号"。
    /// ==========================================================
    ///
    /// 零分配：内部是固定容量的 List，只在初始化时分配一次。
    /// </summary>
    public class InputBufferQueue
    {
        private struct Entry
        {
            public InputCmd cmd;
            public float expireTime;
        }

        private readonly List<Entry> entries;
        private readonly int capacity;

        public InputBufferQueue(int capacity = 4)
        {
            this.capacity = Mathf.Max(1, capacity);
            entries = new List<Entry>(this.capacity);
        }

        public int Count => entries.Count;

        /// <summary>压入一条指令，lifespan 秒内有效</summary>
        public void Push(InputCmd cmd, float lifespan)
        {
            // 同一指令已在队列里就只刷新时限，不重复堆积
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].cmd == cmd)
                {
                    entries[i] = new Entry { cmd = cmd, expireTime = Time.time + lifespan };
                    return;
                }
            }

            // 满了就挤掉最老的一条
            if (entries.Count >= capacity) entries.RemoveAt(0);

            entries.Add(new Entry { cmd = cmd, expireTime = Time.time + lifespan });
        }

        /// <summary>清理过期项。每帧调用一次</summary>
        public void Tick()
        {
            float now = Time.time;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (entries[i].expireTime <= now) entries.RemoveAt(i);
            }
        }

        /// <summary>队列里有没有这条指令（不消费）</summary>
        public bool Contains(InputCmd cmd)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].cmd == cmd) return true;
            }
            return false;
        }

        /// <summary>取出并移除一条指令。闸门打开时调用</summary>
        public bool TryConsume(InputCmd cmd)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].cmd == cmd)
                {
                    entries.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        /// <summary>取出最早压入的那条（不限指令类型）</summary>
        public bool TryConsumeOldest(out InputCmd cmd)
        {
            if (entries.Count == 0)
            {
                cmd = default;
                return false;
            }

            cmd = entries[0].cmd;
            entries.RemoveAt(0);
            return true;
        }

        /// <summary>清空。受击、死亡、切场景时调用，防止脏指令残留</summary>
        public void Clear() => entries.Clear();
    }
}
