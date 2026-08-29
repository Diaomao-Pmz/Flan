using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【动画调度器】—— 全项目【唯一】允许调用 Animator.Play 的地方。
    ///
    /// ==========================================================
    /// 【为什么必须唯一】
    ///
    /// 改造前，往 Animator 写东西的有两方：
    ///   ① 9 张状态卡带各自调 anim.Play
    ///   ② 本调度器事后覆盖蓄力动画
    /// 两方都写、没有仲裁、谁最后写谁赢 —— 于是：
    ///   JumpState 每帧切换上升/顶点/下落动画 → 每帧把蓄力姿势顶掉 → 抽搐
    ///   蓄力取消时没人把移动动画播回来        → 角色卡在蓄力姿势里
    ///
    /// 这和早期「无敌开关有三个主人」是完全同一类 bug。
    /// 当时的解法是引用计数（让状态有唯一裁决方），这次的解法是唯一写入者。
    ///
    /// 比喻：以前是每个部门自己往公告栏贴纸，谁后贴谁盖住。
    ///       现在所有人把稿子交给文管，由他决定贴什么。
    /// ==========================================================
    ///
    /// 【两条轨道】
    ///   Base    —— 移动与动作动画，由状态卡带声明（"我要播跑动"）
    ///   Overlay —— 蓄力姿势，由本组件根据蓄力系统自行判断
    ///
    /// 每帧结算：有覆盖就播覆盖，没有就播 Base。
    /// 因为 Base 一直被记着，覆盖一撤销就能立刻播回去 —— 不会再卡姿势。
    ///
    /// 【为以后的 Animator 分层预留】
    /// 现在覆盖是"盖住"Base（同一层）。等美术做了上下半身分离之后，
    /// 把 overlayLayer 改成 1、Animator 里建好第 1 层，
    /// 覆盖就变成"叠加"而不是"盖住"，Base 得以保留。
    /// 【状态卡带一行都不用改】—— 这正是把决策集中到一处的价值。
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
        ///
        /// 【只是声明，不会立刻播】—— 本帧最终播什么由 LateUpdate 结算。
        /// 所以每帧重复调用同一个 hash 是安全的（JumpState 就是这么用的），
        /// 不会重置动画进度。
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

            if (desiredHash == 0) return;

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