using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【动画名常量表】
    ///
    /// ==========================================================
    /// 解决两个问题：
    ///
    /// 1. 打错字静默失败。
    ///    anim.Play("Flandre_Jumpp_Start") 不会报任何错 ——
    ///    Unity 找不到这个 State 就【什么都不做】。
    ///    角色卡在上一个动画上，你会以为是状态机逻辑写错了，
    ///    然后花半小时排查一个根本不在那里的 bug。
    ///    改成常量后，打错字编译期就红。
    ///
    /// 2. 每帧重复算哈希。
    ///    Animator 内部用 int 哈希索引 State，Play(string) 每次调用都要现算。
    ///    JumpState 那三行动画切换是【每帧都在跑】的，
    ///    这是全项目唯一一处有实际性能意义的调用点。
    ///
    /// 比喻：从"每次寄快递都手写一遍完整地址"，
    ///       改成"存好的地址簿，选一下就行" —— 既不会写错，也不用重写。
    /// ==========================================================
    ///
    /// 【连招动画不在这里】
    /// ComboNode 的动画名来自拖进去的 AnimationClip，是数据驱动的，
    /// 没法做成常量。不过它一招只 Play 一次，不是每帧，开销可忽略。
    /// </summary>
    public static class PlayerAnimHash
    {
        // ---- 原始字符串。仅用于调试输出与 Animator 配置对照 ----
        public const string IdleName = "Flandre_Idle";
        public const string RunName = "Flandre_Run";
        public const string JumpStartName = "Flandre_Jump_Start";
        public const string JumpApexName = "Flandre_Jump_Apex";
        public const string JumpFallName = "Flandre_Jump_Fall";
        public const string CrouchName = "Flandre_Crouch";
        public const string SlideName = "Flandre_Slide";
        public const string FlyName = "Flandre_Fly";
        public const string ChargeName = "Flandre_Charge";
        public const string HitName = "Flandre_Hit";

        // TODO: 接入 DeadState 时补上死亡动画
        public const string DeathName = "Flandre_Hit";

        // ---- 预算好的哈希。静态构造只跑一次 ----
        public static readonly int Idle = Animator.StringToHash(IdleName);
        public static readonly int Run = Animator.StringToHash(RunName);
        public static readonly int JumpStart = Animator.StringToHash(JumpStartName);
        public static readonly int JumpApex = Animator.StringToHash(JumpApexName);
        public static readonly int JumpFall = Animator.StringToHash(JumpFallName);
        public static readonly int Crouch = Animator.StringToHash(CrouchName);
        public static readonly int Slide = Animator.StringToHash(SlideName);
        public static readonly int Fly = Animator.StringToHash(FlyName);
        public static readonly int Charge = Animator.StringToHash(ChargeName);
        public static readonly int Hit = Animator.StringToHash(HitName);
        public static readonly int Death = Animator.StringToHash(DeathName);
    }
}