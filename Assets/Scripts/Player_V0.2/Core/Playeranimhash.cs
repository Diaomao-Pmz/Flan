using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【动画名常量表】
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