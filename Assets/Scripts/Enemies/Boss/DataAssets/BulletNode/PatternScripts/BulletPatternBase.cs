using System;
using UnityEngine;

[Serializable]
public struct FloatRange
{
    public float min, max;
    public float Random() => UnityEngine.Random.Range(min, max);
}
/// <summary>
/// 【弹幕图案】的基类 —— 现在直接内嵌在 BulletNode 的每一段（Phase）里，不再是独立的资产。
///
/// ==========================================================
/// 比喻：以前卡片上贴的是"去仓库取图案"的取件单（PatternSO 资产），
///       改完后图案直接装在卡片里：选好类型，参数就在眼前；每张卡各存各的，互不影响。
///
/// 【Inspector 怎么显示】
/// BulletPhase.pattern 用 [SerializeReference] 存放"某一种具体图案"，
/// 配合 [SubclassSelector] 在 Inspector 里显示一个下拉框选类型（编辑器代码见 Boss/Editor/SubclassSelectorDrawer.cs）。
/// 选了哪种类型，就显示：本基类的【通用子弹设置】 + 该类型的【专属参数】。
///
/// 【加一种新图案】写一个 [Serializable] 子类、实现 Spawn，下拉框里自动出现，不用改别处。
/// ==========================================================
/// </summary>
[Serializable]
public abstract class BulletPatternBase
{
    [Header("--- 通用子弹设置 ---")]
    [Tooltip("发射间隔（秒）：每隔多久执行一次本图案（一次可能发出很多颗）")]
    [Min(0.01f)]
    public float interval = 0.5f;

    [Tooltip("子弹速度。阵型类（方阵 / 三角 / 星形）是整个阵型的移动速度")]
    public float bulletSpeed = 8f;

    [Tooltip("子弹缩放。0 = 保持预制体原大小；大于 0 = 直接设为这个缩放值")]
    [Min(0f)]
    public float bulletScale = 0f;

    [Tooltip("子弹存活时间（秒）。0 = 不限时（只在飞出屏幕时回收）")]
    [Min(0f)]
    public float lifetime = 0f;

    [Tooltip("子弹外观：对象池里的子弹 key。留空 = 用 BAE_BulletEmitter 上的默认子弹。\n" +
             "填写的 key 必须已在 ObjectPoolManager 里注册")]
    public string projectileKey = "";

    [Tooltip("是否加速")]
    public bool enableAcceleration = false;

    [Tooltip("加速度（越大加速越猛）")]
    public float accelerationRate = 1f;

    [Tooltip("初速系数：加速开始时速度 = 子弹速度 × 本系数，之后按加速度递增。\n" +
             "只对单颗子弹有效，阵型类不使用")]
    public float accelerationCoef = 1f;

    [Tooltip("是否正弦运动")]
    public bool SineWave;
    public FloatRange SinAmp;
    public FloatRange SinFreq;
    public FloatRange SinPhase;

    /// <summary>执行一次本图案：生成子弹 / 阵型</summary>
    public abstract void Spawn(BulletSpawnContext ctx);

    public virtual void AddComponents(GameObject bullet)
    {
        if (SineWave)
        {
            BulletSineWave bsw = AddBulletComponent<BulletSineWave>(bullet);
            bsw.amplitude = SinAmp.Random();
            bsw.frequency = SinFreq.Random();
            bsw.phase = SinPhase.Random();
        }
    }

    private T AddBulletComponent<T>(GameObject bullet) where T : UnityEngine.Behaviour
    {
        T comp = bullet.GetComponent<T>();
        if (comp != null)
        {
            comp.enabled = true;
            return comp;
        }
        return bullet.AddComponent<T>();
    }
}

/// <summary>
/// 给 [SerializeReference] 字段加上"选子类"的下拉框。绘制代码见 Boss/Editor/SubclassSelectorDrawer.cs
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public class SubclassSelectorAttribute : PropertyAttribute { }

/// <summary>图案在下拉框里显示的名字</summary>
[AttributeUsage(AttributeTargets.Class)]
public class BulletPatternInfoAttribute : Attribute
{
    public readonly string displayName;
    public BulletPatternInfoAttribute(string displayName) { this.displayName = displayName; }
}
