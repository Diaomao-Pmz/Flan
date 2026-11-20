using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>蓄力模块的一次结算结果</summary>
    public enum ChargeReleaseResult
    {
        /// <summary>没在蓄力，什么都没发生</summary>
        NotCharging,

        /// <summary>还没过起步时间就松手 —— 什么都不放</summary>
        Aborted,

        /// <summary>已过起步时间，应当打出当前所在的等级</summary>
        Fired,
    }

    /// <summary>
    /// 【蓄力模块】—— 一只手的充能进度，纯逻辑，不是状态。
    /// </summary>
    public class ChargeModule
    {
        /// <summary>这个模块属于哪只手</summary>
        public WeaponSlot Slot { get; private set; }

        /// <summary>对应的攻击指令</summary>
        public InputCmd Command { get; private set; }

        public bool IsCharging { get; private set; }

        /// <summary>
        /// 当前对外生效的等级（0 = 还没过起步时间）。
        /// 本值在 Stop() 里【刻意不清】：它是"刚结束那次蓄力蓄到了几级"，调用方要在 Release() 之后读。
        /// </summary>
        public int CurrentLevel { get; private set; }

        /// <summary>已过起步时间（松手能打出东西）</summary>
        public bool IsCharged => CurrentLevel >= 1;

        /// <summary>当前正处在最高级（等级会循环，所以这只是"此刻是不是 3 级"）</summary>
        public bool IsMaxed => CurrentLevel >= WeaponMoveSet.MaxChargeLevel;

        /// <summary>在当前这一级里已经攒了多久（已套用加速倍率）</summary>
        public float ChargedTime { get; private set; }

        /// <summary>距离跳到下一级的进度 0~1</summary>
        public float Progress
        {
            get
            {
                if (!IsValid) return 0f;
                float need = Weapon.GetTimeToNextLevel(rawLevel);
                return need <= 0f ? 1f : Mathf.Clamp01(ChargedTime / need);
            }
        }

        /// <summary>正在蓄的武器</summary>
        public WeaponMoveSet Weapon { get; private set; }

        /// <summary>
        /// 本次蓄力是否有效。唯一的无效情况：这只手没装武器。
        /// 这种情况下仍然进入蓄力姿态（否则按住毫无反馈），但永远没有等级。
        /// </summary>
        public bool IsValid { get; private set; }

        /// <summary>
        /// 蓄力上限（1~3）。等级沿 1→2→3→1 循环升到这一级就停住。
        /// 平a后接蓄力一开始 = 当前连段深度；原地起手一开始 = 1。侧键每按一下 +1（3 之后回到 1）。
        /// </summary>
        public int CapLevel { get; private set; }

        // 普攻后蓄力的加速倍率（武器上的 comboChargeSpeedMultiplier），原地起手为 1
        private float speedMultiplier = 1f;

        private float heldTime;       // 本次蓄力实际按住了多久（不吃加速倍率）
        private float minHoldTime;    // 起步时间：按住满这么久等级才生效
        private int rawLevel;         // 内部等级（不管起步时间有没有过）

        /// <summary>本次蓄力的起步等级（1~3）</summary>
        public int StartLevel { get; private set; }

        /// <summary>还没过起步时间</summary>
        public bool IsLocked => heldTime < minHoldTime;

        public ChargeModule(WeaponSlot slot)
        {
            Slot = slot;
            Command = WeaponMoveSet.SlotToCommand(slot);
        }

        /// <summary>开始蓄力</summary>
        /// <param name="weapon">这只手的武器，可为 null</param>
        /// <param name="startLevel">起步等级：连段为 0 时 1，第 N 段普攻后为 N</param>
        /// <param name="minHold">起步时间（秒）</param>
        /// <param name="isAfterCombo">是否接在普攻后面（决定要不要吃加速倍率）</param>
        public void Begin(WeaponMoveSet weapon, int startLevel, float minHold, bool isAfterCombo)
        {
            Weapon = weapon;
            IsValid = weapon != null;

            speedMultiplier = (IsValid && isAfterCombo)
                ? Mathf.Max(0.01f, weapon.comboChargeSpeedMultiplier)
                : 1f;

            StartLevel = Mathf.Clamp(startLevel, 1, WeaponMoveSet.MaxChargeLevel);
            rawLevel = StartLevel;

            ChargedTime = 0f;
            heldTime = 0f;
            minHoldTime = Mathf.Max(0f, minHold);

            // 上限一律 = 起步等级：平a后接蓄力是当前深度，原地起手固定 1 级。按侧键才往上升
            CapLevel = StartLevel;
            IsCharging = true;
            CurrentLevel = 0;
        }

        /// <summary>把蓄力上限提高一级（3 级之后回到 1 级）。只在蓄力中有效，可以连按</summary>
        /// <returns>提高后的上限</returns>
        public int RaiseCap()
        {
            if (!IsCharging || !IsValid) return CapLevel;

            bool wasStopped = rawLevel == CapLevel;

            CapLevel = CapLevel >= WeaponMoveSet.MaxChargeLevel ? 1 : CapLevel + 1;

            // 原本停在上限、现在又要往上升 → 当前这一级的时间从头算；
            // 上限被按回当前等级 → 就地停住，清掉攒到一半的时间。
            // 正在升的途中继续加上限（连按）→ 已经攒的时间保留
            if (wasStopped || rawLevel == CapLevel) ChargedTime = 0f;

            return CapLevel;
        }

        /// <summary>每帧推进。返回本帧对外等级是否【变了】（用于广播事件、换光效）</summary>
        public bool Tick(float deltaTime)
        {
            if (!IsCharging || !IsValid) return false;

            heldTime += deltaTime;

            // 起步时间内：只等起步等级生效，不攒升级进度。
            // 已到上限：等级停住，进度不涨
            if (rawLevel != CapLevel && !IsLocked)
            {
                ChargedTime += deltaTime * speedMultiplier;

                // 沿 1→2→3→1 往上限升，到了就停
                int guard = 0;
                float need = Weapon.GetTimeToNextLevel(rawLevel);
                while (rawLevel != CapLevel && ChargedTime >= need && guard++ < 8)
                {
                    ChargedTime -= need;
                    rawLevel = rawLevel >= WeaponMoveSet.MaxChargeLevel ? 1 : rawLevel + 1;
                    need = Weapon.GetTimeToNextLevel(rawLevel);
                }

                if (rawLevel == CapLevel) ChargedTime = 0f;
            }

            // 起步时间没过：等级不对外生效
            int visible = IsLocked ? 0 : rawLevel;
            if (visible == CurrentLevel) return false;

            CurrentLevel = visible;
            return true;
        }

        /// <summary>松手结算</summary>
        public ChargeReleaseResult Release()
        {
            if (!IsCharging) return ChargeReleaseResult.NotCharging;

            bool fired = IsValid && IsCharged;
            Stop();

            // 还没过起步时间就松手 → 什么都不放
            return fired ? ChargeReleaseResult.Fired : ChargeReleaseResult.Aborted;
        }

        /// <summary>被打断（受击、起飞、换武器等）。什么都不放</summary>
        public void Stop()
        {
            IsCharging = false;
            ChargedTime = 0f;
            // CurrentLevel 刻意不清，见其注释
        }
    }
}
