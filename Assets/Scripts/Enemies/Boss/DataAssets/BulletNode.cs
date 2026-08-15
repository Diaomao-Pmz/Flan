using System;
using System.Collections.Generic;
using UnityEngine;
using Flandre.CombatSystem;

public enum BossAttackType
{
    Line,
    Random,
    Circle,
    Square,
    Rotation,
    Triangle,
    Star
}

/// <summary>
/// 组合弹幕中的一段。多段叠加就是「同时发射数种弹幕」。
///
/// startDelay 决定组合方式：
///   全填 0        → 并发（直线 + 环形同时喷）
///   递增          → 序列（直线 2 秒 → 环形 3 秒 → 五角星 2 秒）
///   混合          → 交错叠加
/// </summary>
[Serializable]
public class BulletPhase
{
    [Tooltip("这一段用哪种弹幕形态")]
    public BossAttackType type = BossAttackType.Random;

    [Tooltip("相对组合开始的延迟（秒）。0 表示与组合同时启动。")]
    public float startDelay = 0f;

    [Tooltip("这一段持续多久（秒）")]
    public float duration = 3f;

    [Tooltip("发射间隔覆盖值（秒）。填 0 则沿用 Emitter 上该形态的默认间隔。")]
    public float intervalOverride = 0f;

    [Tooltip("阵型托管时长 0~1s。仅对 Square / Triangle / Star 生效。")]
    public float formationDuration = 0f;
}

[CreateAssetMenu(fileName = "NewBulletNode", menuName = "ScriptableObjects/BulletNode")]
public class BulletNode : ActionNode
{
    public override ActionCategory DefaultCategory => ActionCategory.Bullet;

    [Header("--- 组合弹幕配置 ---")]
    [Tooltip("弹幕段列表。留空则回退到下方的旧版单形态字段。")]
    public List<BulletPhase> phases = new List<BulletPhase>();

    [Header("--- 旧版单形态字段（phases 为空时生效）---")]
    [Tooltip("弹幕形态名，需与 BossAttackType 枚举名一致")]
    public string AttackName;

    [Tooltip("0~1s")]
    public float formationDuration = 0f;

    [Tooltip("这张卡的弹幕总时长（秒）")]
    public float attackDuration = 3f;

    /// <summary>是否使用新的组合模式。</summary>
    public bool HasPhases => phases != null && phases.Count > 0;

    /// <summary>
    /// 整套组合的总时长，供执行器等待。
    /// 组合模式下自动取所有段中「延迟 + 时长」的最大值，不需要手填。
    /// </summary>
    public float TotalDuration
    {
        get
        {
            if (!HasPhases) return attackDuration;

            float max = 0f;
            for (int i = 0; i < phases.Count; i++)
            {
                if (phases[i] == null) continue;
                float end = phases[i].startDelay + phases[i].duration;
                if (end > max) max = end;
            }
            return max > 0f ? max : attackDuration;
        }
    }
}
