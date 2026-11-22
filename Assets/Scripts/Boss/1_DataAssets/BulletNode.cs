using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>旧版弹幕形态枚举。新版用 BulletPhase.pattern 的下拉框选类型，这里只为兼容可能残留的引用而保留</summary>
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
///
/// 【图案直接内嵌】pattern 用下拉框选类型，选完显示该类型的通用子弹设置 + 专属参数。
/// 每张卡的参数各存各的，改这张卡不会影响别的卡。
/// </summary>
[Serializable]
public class BulletPhase
{
    [Tooltip("相对组合开始的延迟（秒）。0 表示与组合同时启动。")]
    public float startDelay = 0f;

    [Tooltip("这一段持续多久（秒）")]
    public float duration = 3f;

    [Tooltip("这一段用哪种弹幕：点右侧下拉框选类型")]
    [SerializeReference, SubclassSelector]
    public BulletPatternBase pattern;
}

/// <summary>
/// 弹幕招式卡。只有一种弹幕的卡，Phases 里放 1 个元素即可；组合弹幕放多个。
/// </summary>
[CreateAssetMenu(fileName = "NewBulletNode", menuName = "ScriptableObjects/BulletNode")]
public class BulletNode : ActionNode
{
    [Header("--- 弹幕配置 ---")]
    [Tooltip("弹幕段列表。单一弹幕放 1 段，组合弹幕放多段")]
    public List<BulletPhase> phases = new List<BulletPhase>();

    /// <summary>
    /// 整套弹幕的总时长，供执行器等待：取所有段中「延迟 + 时长」的最大值。
    /// </summary>
    public float TotalDuration
    {
        get
        {
            float max = 0f;
            if (phases == null) return max;
            for (int i = 0; i < phases.Count; i++)
            {
                BulletPhase p = phases[i];
                if (p == null || p.pattern == null) continue;
                max = Mathf.Max(max, p.startDelay + p.duration);
            }
            return max;
        }
    }
}
