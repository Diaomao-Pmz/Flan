using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 玩家的所有抽象输入指令。
    ///
    /// 【批次B 改动】新增 Crouch。
    ///
    /// 原因：Slide 这个值从定义至今，全项目【没有任何一处发送过它】——
    /// 蹲/铲一直是「Controller 置一个 bool → IdleState/RunState 自己轮询」，
    /// 从来没走过指令通道。
    ///
    /// 按你的设计，C 键是【一个键】，跑动中按是滑铲、静止按是蹲下 ——
    /// "变成铲还是变成蹲"是决策层的判断，不该在输入层就分成两个指令。
    /// 所以正确的做法是只保留一个 Crouch 指令。
    ///
    /// Slide 暂时保留以免破坏现有引用，阶段3 建立 PlayerCommandRouter 后删除。
    /// </summary>
    public enum InputCmd
    {
        Up,
        Down,
        Left,
        Right,
        MainAttack,
        SubAttack,
        Jump,
        Dash,

        /// <summary>【已废弃】阶段3 删除，请改用 Crouch</summary>
        Slide,

        Shoot,

        /// <summary>C 键。落到滑铲还是蹲下由决策层判断</summary>
        Crouch,

        /// <summary>
        /// Q 键【点按】。
        ///
        /// 注意 fly 键有两路语义，各走各的通道：
        ///   长按 → isFlyHeld 布尔，供 Jump/Fall 的悬停蓄力读取
        ///   点按 → 本指令，供「打出 AAn 后跃起进飞行」的衍生使用
        /// 两者不冲突：点按不够长，不会触发悬停蓄力。
        /// </summary>
        Fly
    }

    /// <summary>可以装备宝石的三个基础动作</summary>
    public enum ActionType
    {
        Jump,
        Dash,
        Slide
    }

    public enum GemType
    {
        None,
        Echo,
        Relay,
        Shield,
        Pulse
    }

    public enum DamageType
    {
        Melee,  // 近战
        Ranged  // 远程
    }
}
