using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【携带动量】—— 一坨还没耗尽的横向冲劲，可以跨状态传递。
    ///
    /// ==========================================================
    /// 【为什么要抽出来】
    ///
    /// 需求是"滑铲途中顺手打出普攻，滑行继续；滑到终点后可继续蓄力"。
    ///
    /// 诱人但错误的做法：在 ComboState 里写"如果上一个状态是滑铲，
    /// 就复制它的减速逻辑"。那等于把滑铲的物理抄了第二份 ——
    /// 以后调滑铲减速率要记得同时改两处，而蓄力状态还要抄第三份。
    ///
    /// 比喻：滑铲不是"我在滑铲房间里"，而是"我身上还带着一股冲劲"。
    ///       换房间不代表冲劲消失 —— 冲劲是随身携带的，
    ///       谁接手谁负责让它继续衰减。
    ///
    /// 于是链路变成：滑铲 → 攻击 → 蓄力，动量一路传下去，自己衰减到底。
    /// 减速率仍然只有 PlayerMovementConfig 里那一份。
    /// ==========================================================
    ///
    /// 【清除时机集中在 PlayerStateMachine.ChangeState】
    /// 只有滑铲/连招/蓄力三个状态会保留动量，
    /// 进入其他任何状态（跳跃、冲刺、受击、待机…）都自动清空 ——
    /// 不需要在每张卡带里各写一遍，也就不可能漏。
    /// </summary>
    public class SlideMomentum
    {
        /// <summary>当前是否还有动量</summary>
        public bool IsActive { get; private set; }

        /// <summary>当前速度大小（始终为正）</summary>
        public float Speed { get; private set; }

        /// <summary>方向：1 = 右，-1 = 左</summary>
        public float Direction { get; private set; }

        /// <summary>由 SlideState 每帧写入，保持同步</summary>
        public void Set(float speed, float direction)
        {
            Speed = Mathf.Max(0f, speed);
            Direction = Mathf.Sign(direction == 0f ? 1f : direction);
            IsActive = Speed > 0f;
        }

        public void Clear()
        {
            IsActive = false;
            Speed = 0f;
        }

        /// <summary>
        /// 在物理帧里推进一步：减速 → 写入速度。
        /// 速度衰减到蹲行阈值以下时自动清空。
        /// </summary>
        /// <returns>动量是否仍然有效（false 表示这一帧没写速度，调用方该走自己的逻辑）</returns>
        public bool Tick(PlayerStateMachine sm, float deltaTime)
        {
            if (!IsActive || sm == null) return false;

            Speed -= sm.slideDeceleration * deltaTime;

            // 和 SlideState 转蹲下用的是同一个阈值，保证手感一致
            float floor = sm.moveSpeed * sm.crouchSpeedMultiplier;

            if (Speed <= floor)
            {
                Clear();
                return false;
            }

            sm.rb.linearVelocity = new Vector2(Direction * Speed, sm.rb.linearVelocity.y);
            return true;
        }
    }
}
