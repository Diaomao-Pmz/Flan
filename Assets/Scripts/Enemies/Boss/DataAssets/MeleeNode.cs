using System;
using System.Collections.Generic;
using UnityEngine;
using Flandre.CombatSystem;

/// <summary>
/// 一段挥击。多段串起来就是一套近战连招。
/// 时序：windupTime（前摇）→ hitboxDuration（判定窗口）→ recoverTime（后摇）
/// </summary>
[Serializable]
public class MeleeSwing
{
    [Header("--- 时序 ---")]
    [Tooltip("前摇时长（秒）。这段时间内不产生判定，用于播放起手动作。")]
    public float windupTime = 0.3f;

    [Tooltip("判定窗口时长（秒）。填 0 表示只在单帧做一次瞬间判定。")]
    public float hitboxDuration = 0.1f;

    [Tooltip("后摇时长（秒）。这段时间 Boss 处于硬直，是玩家的输出窗口。")]
    public float recoverTime = 0.2f;

    [Header("--- 判定框 ---")]
    [Tooltip("相对 Boss 的偏移。X 会自动乘以朝向，填正数即可（表示身前）。")]
    public Vector2 hitboxOffset = new Vector2(1.5f, 0f);

    [Tooltip("判定框尺寸（宽, 高）。")]
    public Vector2 hitboxSize = new Vector2(2f, 2f);

    [Header("--- 伤害 ---")]
    public int damage = 15;

    [Header("--- 动画（可选）---")]
    [Tooltip("本段挥击的动画名。留空则使用 Node 上的 activeAnimName。")]
    public string swingAnimName;
}

[CreateAssetMenu(fileName = "NewMeleeNode", menuName = "ScriptableObjects/MeleeNode")]
public class MeleeNode : ActionNode
{
    public override ActionCategory DefaultCategory => ActionCategory.Melee;

    [Header("--- 近战专属配置 ---")]
    [Tooltip("挥击序列。一段就是单次攻击，多段就是连招。")]
    public List<MeleeSwing> swings = new List<MeleeSwing>();

    [Tooltip("勾选后每一段挥击开始前都重新面向玩家（追踪型连招）；" +
             "不勾则整套连招沿用第一段的朝向（可被玩家绕背躲开）。")]
    public bool faceTargetEachSwing = true;
}
