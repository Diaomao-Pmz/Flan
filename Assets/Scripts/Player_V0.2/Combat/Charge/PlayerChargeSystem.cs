using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【蓄力系统】—— 持有两只手的蓄力模块，挂在玩家身上。
    ///
    /// ==========================================================
    /// 它取代了原来的 ChargeState。
    ///
    /// 蓄力不再是"玩家待在某个状态里"，而是"某只手正攥着一个充能物"。
    /// 于是蓄力期间玩家仍然处于 Idle / Run / Jump / Fall / Combo ——
    /// 跳跃和跑动自然可用，另一只手也能正常打连招。
    ///
    /// 【移动折损用属性修饰器，不用硬编码】
    /// 蓄力开始时往 moveSpeed / jumpForce 上贴一张便利贴，
    /// 结束时按签名撕掉。这样：
    ///   ① 不需要在每个状态里写 if(蓄力中) 速度×0.2
    ///   ② 和宝石、buff 的加成自动叠加，不会互相覆盖
    ///   ③ 撕的时候按签名撕，不可能漏减
    /// ==========================================================
    /// </summary>
    public class PlayerChargeSystem : MonoBehaviour
    {
        [Header("移动折损 (蓄力期间)")]
        [Tooltip("移速倍率。0.2 = 只有平时两成。留空则读 PlayerMovementConfig")]
        public bool overrideMoveMultiplier = false;
        [Range(0f, 1f)] public float moveMultiplierOverride = 0.2f;

        [Tooltip("跳跃力度倍率。0.7 = 跳得比平时低三成")]
        [Range(0.1f, 1f)]
        public float jumpMultiplier = 0.7f;

        [Header("空中蓄力")]
        [Tooltip(
            "空中蓄力时的最大下落速度。\n\n" +
            "0   = 完全悬停（解耦前 ChargeState 的旧行为）\n" +
            "2   = 缓降，还能蓄一会儿\n" +
            "999 = 关闭，正常下落\n\n" +
            "【为什么需要它】蓄力一段通常要 1 秒左右，\n" +
            "而从跳跃最高点落地往往不到 1 秒 ——\n" +
            "不限制下落速度的话，空中蓄力几乎不可能在落地前完成。")]
        public float airChargeMaxFallSpeed = 2f;

        [Header("Debug")]
        public bool verboseLog = false;

        // ---- 事件（UI / 特效订阅）----
        /// <summary>某只手的蓄力等级变化 (槽位, 等级)。0 = 未蓄满，>0 = 已蓄满的等级</summary>
        public event System.Action<WeaponSlot, int> OnChargeLevelChanged;

        /// <summary>某只手的蓄力进度变化 (槽位, 0~1)</summary>
        public event System.Action<WeaponSlot, float> OnChargeProgressChanged;

        /// <summary>某只手开始蓄力</summary>
        public event System.Action<WeaponSlot> OnChargeStarted;

        /// <summary>某只手结束蓄力（无论成败）</summary>
        public event System.Action<WeaponSlot> OnChargeEnded;

        private readonly ChargeModule[] modules =
        {
            new ChargeModule(WeaponSlot.Main),
            new ChargeModule(WeaponSlot.Sub),
        };

        private PlayerState state;
        private PlayerController controller;
        private ComboInputBuffer buffer;
        private WeaponLoadout weapons;

        private bool penaltyApplied;

        private void Awake()
        {
            state = GetComponent<PlayerState>();
            controller = GetComponent<PlayerController>();
            buffer = GetComponent<ComboInputBuffer>();
            weapons = GetComponent<WeaponLoadout>();

            if (state != null) state.EnsureInitialized();
        }

        // ==========================================================
        // 查询
        // ==========================================================

        public ChargeModule GetModule(WeaponSlot slot) => modules[(int)slot];

        public ChargeModule GetModule(InputCmd cmd)
            => WeaponMoveSet.TryCommandToSlot(cmd, out WeaponSlot s) ? modules[(int)s] : null;

        /// <summary>有任意一只手在蓄力</summary>
        public bool IsAnyCharging => modules[0].IsCharging || modules[1].IsCharging;

        public bool IsCharging(InputCmd cmd)
        {
            var m = GetModule(cmd);
            return m != null && m.IsCharging;
        }

        /// <summary>最先开始蓄力的那只手，供动画与光效使用。都没蓄时返回 null</summary>
        public ChargeModule PrimaryChargingModule
        {
            get
            {
                if (modules[0].IsCharging) return modules[0];
                if (modules[1].IsCharging) return modules[1];
                return null;
            }
        }

        // ==========================================================
        // 开始 / 结束
        // ==========================================================

        /// <summary>
        /// 开始蓄力。由 ComboInputBuffer 在「长按确认」时调用。
        /// </summary>
        public void BeginCharge(InputCmd cmd, int targetLevel, bool isAfterCombo)
        {
            ChargeModule m = GetModule(cmd);
            if (m == null || m.IsCharging) return;

            WeaponMoveSet weapon = weapons != null ? weapons.GetWeapon(m.Slot) : null;
            bool ready = weapons == null || weapons.IsChargeReady(m.Slot);

            m.Begin(weapon, targetLevel, isAfterCombo, ready);

            ApplyPenaltyIfNeeded();

            OnChargeStarted?.Invoke(m.Slot);
            OnChargeLevelChanged?.Invoke(m.Slot, 0);
            OnChargeProgressChanged?.Invoke(m.Slot, 0f);

            if (verboseLog)
            {
                Debug.Log(m.IsValid
                    ? $"[蓄力] {m.Slot} 开始，目标 AA{m.TargetLevel}，需要 {m.RequiredTime:F2}s"
                    : $"[蓄力] {m.Slot} 开始，但无武器或 CD 中，蓄不出东西");
            }
        }

        /// <summary>
        /// 松手结算。由 ComboInputBuffer 在攻击键松开时调用。
        /// </summary>
        public ChargeReleaseResult ReleaseCharge(InputCmd cmd)
        {
            ChargeModule m = GetModule(cmd);
            if (m == null || !m.IsCharging) return ChargeReleaseResult.NotCharging;

            int level = m.TargetLevel;
            ChargeReleaseResult result = m.Release();

            FinishModule(m);

            if (verboseLog)
            {
                Debug.Log(result == ChargeReleaseResult.Fired
                    ? $"[蓄力] {m.Slot} 蓄满 AA{level}，释放"
                    : $"[蓄力] {m.Slot} 未蓄满就松手，连段清零");
            }

            return result;
        }

        /// <summary>
        /// 强制中断某只手的蓄力（受击、死亡、换武器）。
        /// 后果与未蓄满松手相同：什么都不放。
        /// </summary>
        public void CancelCharge(InputCmd cmd)
        {
            ChargeModule m = GetModule(cmd);
            if (m == null || !m.IsCharging) return;

            m.Stop();
            FinishModule(m);

            if (verboseLog) Debug.Log($"[蓄力] {m.Slot} 被打断");
        }

        /// <summary>中断两只手的蓄力</summary>
        public void CancelAll()
        {
            CancelCharge(InputCmd.MainAttack);
            CancelCharge(InputCmd.SubAttack);
        }

        private void FinishModule(ChargeModule m)
        {
            OnChargeLevelChanged?.Invoke(m.Slot, 0);
            OnChargeProgressChanged?.Invoke(m.Slot, 0f);
            OnChargeEnded?.Invoke(m.Slot);

            ReleasePenaltyIfNeeded();
        }

        // ==========================================================
        // 每帧推进
        // ==========================================================

        private void Update()
        {
            float dt = Time.deltaTime;

            for (int i = 0; i < modules.Length; i++)
            {
                ChargeModule m = modules[i];
                if (!m.IsCharging) continue;

                bool justCharged = m.Tick(dt);

                OnChargeProgressChanged?.Invoke(m.Slot, m.Progress);

                if (justCharged)
                {
                    OnChargeLevelChanged?.Invoke(m.Slot, m.TargetLevel);
                    if (verboseLog) Debug.Log($"[蓄力] {m.Slot} 蓄满 AA{m.TargetLevel}，松手即发");
                }
            }
        }

        // ==========================================================
        // 移动折损
        // ==========================================================

        private float MoveMultiplier
        {
            get
            {
                if (overrideMoveMultiplier) return moveMultiplierOverride;

                var cfg = state != null ? state.movementConfig : null;
                return cfg != null ? cfg.chargeMoveSpeedMultiplier : 0.2f;
            }
        }

        /// <summary>
        /// 贴便利贴：蓄力期间移速与跳跃力打折。
        ///
        /// 用属性修饰器而不是在每个状态里写 if(蓄力中)，好处是
        /// 和宝石、buff 的加成自动叠加，而且撕的时候按签名撕，不可能漏减。
        /// </summary>
        private void ApplyPenaltyIfNeeded()
        {
            if (penaltyApplied || state == null) return;

            // 倍率转成"减少百分之多少"的加法修饰
            state.stats.moveSpeed.AddModifier(
                StatModifier.Percent(MoveMultiplier - 1f, this));

            state.stats.jumpForce.AddModifier(
                StatModifier.Percent(jumpMultiplier - 1f, this));

            penaltyApplied = true;
        }

        private void ReleasePenaltyIfNeeded()
        {
            if (!penaltyApplied) return;
            if (IsAnyCharging) return;   // 另一只手还在蓄，折损保持

            state?.stats.RemoveAllModifiersFrom(this);
            penaltyApplied = false;
        }

        private void OnDisable()
        {
            // 组件被关掉时别把折损留在属性面板上
            CancelAll();
            ReleasePenaltyIfNeeded();
        }
    }
}