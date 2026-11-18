using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【动画调度器】—— 全项目【唯一】允许调用 Animator.Play 的地方。
    /// </summary>
    [DefaultExecutionOrder(200)]   // 排在所有玩法逻辑之后，确保拿到本帧最终意图
    public class PlayerAnimationDriver : MonoBehaviour
    {
        [Header("覆盖策略")]
        [Tooltip(
            "勾选后蓄力姿势【只在静止时】覆盖，跑跳时保持移动动画。\n\n" +
            "默认关闭 —— 跑动 / 跳跃 / 下落时也保持蓄力姿势，\n" +
            "让「正在蓄力」这件事在任何动作下都看得出来。\n\n" +
            "当初开启是担心 JumpState 每帧切换动画会和蓄力姿势抢 Animator，\n" +
            "但调度器已经用「相同动画不重播」解决了那个问题，限制可以放开。")]
        public bool overlayOnlyWhenIdle = false;

        [Tooltip("关掉则蓄力完全不占动画，纯靠光效表现")]
        public bool overlayWhileCharging = true;

        [Header("分层 (预留)")]
        [Tooltip(
            "覆盖动画播在第几层。\n\n" +
            "0 = 与 Base 同层，覆盖会盖住移动动画（当前方案，无需额外美术）\n" +
            "1 = 播在上半身层，移动动画得以保留（需要 Animator 分层 + 上下半身分离的美术）\n\n" +
            "以后美术到位时改成 1 即可，状态卡带完全不用动。")]
        public int overlayLayer = 0;

        [Header("Debug")]
        public bool verboseLog = false;

        private Animator anim;
        private PlayerStateMachine sm;
        private PlayerChargeSystem chargeSystem;

        // ---- 两条轨道 ----
        private int baseHash;
        private bool basePendingRestart;

        // ---- 当前实际播放的 ----
        private int playingHash;
        private int playingLayer;

        // ---- 攻击动画倍速 ----
        private AttackInstance attackOwner;
        private int attackHash;
        private int attackSegment = -1;

        private void Awake()
        {
            anim = GetComponent<Animator>();
            sm = GetComponent<PlayerStateMachine>();
            chargeSystem = GetComponent<PlayerChargeSystem>();
        }

        // ==========================================================
        // 对外：状态卡带声明意图
        // ==========================================================

        /// <summary>
        /// 声明底层动画（移动 / 攻击 / 受击等）。
        /// </summary>
        /// <param name="restart">
        /// 即使与当前动画相同也从第 0 帧重播。
        /// 攻击、待机这类需要"每次都从头演"的动画传 true。
        /// </param>
        public void SetBase(int hash, bool restart = false)
        {
            if (hash == 0) return;

            if (restart || hash != baseHash) basePendingRestart |= restart;
            baseHash = hash;
        }

        /// <summary>按动画名声明。给 ComboNode 这类数据驱动的动画用</summary>
        public void SetBase(string stateName, bool restart = false)
        {
            if (string.IsNullOrEmpty(stateName)) return;
            SetBase(Animator.StringToHash(stateName), restart);
        }

        /// <summary>
        /// 登记"现在播的攻击动画属于哪一招"。之后每帧按这一招所处的阶段（前摇 / 攻击 / 后摇）调 Animator 倍速，
        /// 让三段动画分别在 ComboNode 填的时长里播完。
        /// 动画被别的招顶掉、或切到别的动画后，自动恢复 1 倍速。
        /// 出招时在 SetBase 之后调用（ComboState / FlyCombatState）。
        /// </summary>
        public void SetAttackOwner(AttackInstance inst)
        {
            attackOwner = inst;
            attackHash = (inst != null && inst.node != null && !string.IsNullOrEmpty(inst.node.animName))
                ? Animator.StringToHash(inst.node.animName)
                : 0;
            attackSegment = -1;
        }

        // ==========================================================
        // 每帧结算
        // ==========================================================

        private void LateUpdate()
        {
            if (anim == null) return;

            int desiredHash;
            int desiredLayer;

            if (ShouldOverlay())
            {
                desiredHash = PlayerAnimHash.Charge;
                desiredLayer = overlayLayer;
            }
            else
            {
                desiredHash = baseHash;
                desiredLayer = 0;
            }

            if (desiredHash == 0)
            {
                ApplyAttackSpeed(0, 0);
                return;
            }

            bool changed = desiredHash != playingHash || desiredLayer != playingLayer;

            // 【关键】相同动画不重播 —— 这就是抽搐消失的原因。
            // JumpState 每帧都会声明一次动画，若无条件 Play 就会每帧重置到第 0 帧。
            if (changed || basePendingRestart)
            {
                anim.Play(desiredHash, desiredLayer, 0f);

                playingHash = desiredHash;
                playingLayer = desiredLayer;

                if (verboseLog && changed)
                    Debug.Log($"[动画] 切到 {desiredHash}（层 {desiredLayer}）");
            }

            basePendingRestart = false;

            ApplyAttackSpeed(desiredHash, desiredLayer);
        }

        /// <summary>
        /// 攻击动画三段倍速。只有"登记过的那一招还拥有动画、而且当前播的正是它的动画"时才调速，
        /// 其他任何时候 Animator 都是 1 倍速。
        /// </summary>
        private void ApplyAttackSpeed(int desiredHash, int desiredLayer)
        {
            AttackInstance inst = attackOwner;
            bool driving = inst != null && attackHash != 0 && inst.ownsAnimation && inst.timing.hasClip
                           && desiredHash == attackHash && desiredLayer == 0;

            if (!driving)
            {
                attackOwner = null;   // 动画已经被顶掉 / 切走，这一招不会再回来
                if (anim.speed != 1f) anim.speed = 1f;
                return;
            }

            AttackTiming t = inst.timing;
            int seg = t.SegmentAt(inst.elapsed);

            if (seg != attackSegment)
            {
                // 进入新的一段：把动画对齐到这一刻该在的位置。
                // 消掉段与段交界处一帧的误差；实际时长为 0 的段（比如前摇填 0）也靠这一下直接跳过
                float animTime = t.ToAnim(inst.elapsed);
                anim.Play(attackHash, 0, t.clipLength > 0f ? animTime / t.clipLength : 0f);
                attackSegment = seg;

                if (verboseLog)
                    Debug.Log($"[动画] {inst.node.nodeName} 进入第 {seg} 段（0 前摇 / 1 攻击 / 2 后摇），倍速 ×{t.SpeedOf(seg):F2}");
            }

            anim.speed = t.SpeedOf(seg);
        }

        /// <summary>本帧要不要用蓄力姿势覆盖</summary>
        private bool ShouldOverlay()
        {
            if (!overlayWhileCharging) return false;
            if (chargeSystem == null || !chargeSystem.IsAnyCharging) return false;
            if (sm == null) return false;

            // 攻击与受击优先级最高，不被蓄力姿势盖掉
            if (sm.currentState == sm.comboState) return false;
            if (sm.currentState == sm.hitState) return false;

            // 默认在所有移动状态下都保持蓄力姿势。
            // 勾上 overlayOnlyWhenIdle 才退回"只在静止时覆盖"。
            if (overlayOnlyWhenIdle && sm.currentState != sm.idleState) return false;

            return true;
        }
    }
}