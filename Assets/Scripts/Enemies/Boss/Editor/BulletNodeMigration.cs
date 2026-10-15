using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 【一次性迁移工具】把旧版 PatternSO 资产里的弹幕参数，写进新版 BulletNode（图案内嵌）。
///
/// 用法：Unity 菜单 Tools → Boss → 迁移旧弹幕卡参数。跑完检查一遍卡片，确认无误后：
///   1. 删除 DataAssets/BulletNode/PatternSO 文件夹（旧图案资产已无用，还会在控制台报"脚本类型不符"）
///   2. 删除本文件
///
/// 参数是从旧资产里抄下来的固定值（迁移时旧图案类已改为内嵌类，旧资产无法再被直接读取）。
/// 旧 Rotation1 图案资产在迁移前已经不存在，Rotation / RR 两张卡用旋转图案的默认值代替，需要手动调。
/// </summary>
public static class BulletNodeMigration
{
    private const string Root = "Assets/Scripts/Enemies/Boss/DataAssets/BulletNode/";

    [MenuItem("Tools/Boss/迁移旧弹幕卡参数")]
    public static void Migrate()
    {
        if (!EditorUtility.DisplayDialog("迁移旧弹幕卡参数",
                "会覆盖下列弹幕卡的 Phases：\n" +
                "Circle、Clock Corpse、Eternal Meek、Line、Random、Rotation、Square、Star、Triangle、\n" +
                "Combo/Flower、Combo/Misdirection、Combo/RR\n\n继续吗？", "迁移", "取消"))
            return;

        int ok = 0;

        ok += Write("Circle", Phase(0f, 4f, Circle1()));
        ok += Write("Clock Corpse", Phase(0f, 1f, Chaotic(1f, 1f, 3f, 40)));
        ok += Write("Eternal Meek", Phase(0f, 10f, Chaotic(0.5f, 3f, 6f, 20)));
        ok += Write("Line", Phase(0f, 3f, new LinePattern { interval = 2f, bulletSpeed = 15f, bulletScale = 1.5f, aimOffsetY = 0f }));
        ok += Write("Random", Phase(0f, 4f, new RandomPattern { interval = 0.5f, bulletSpeed = 10f }));
        ok += Write("Rotation", Phase(0f, 6f, new RotationPattern()));   // 原 Rotation1 资产已丢失，用默认值
        ok += Write("Square", Phase(0f, 4f, new SquarePattern
        {
            interval = 2f, bulletSpeed = 5f, bulletsPerSide = 3, size = 3f, offsetY = 0f,
            enableAcceleration = true, accelerationRate = 1f, formationDuration = 0.5f
        }));
        ok += Write("Star", Phase(0f, 6f, new StarPattern
        {
            interval = 2f, bulletSpeed = 5f, bulletCount = 10, rMin = 2f, rMax = 4f, offsetY = 0f,
            enableAcceleration = true, accelerationRate = 1f, formationDuration = 0.5f
        }));
        ok += Write("Triangle", Phase(0f, 5f, new TrianglePattern
        {
            interval = 3f, bulletSpeed = 5f, bulletCount = 6, rMin = 2f, rMax = 4f, offsetY = 0f,
            enableAcceleration = true, accelerationRate = 1f, formationDuration = 0.5f
        }));

        // ---- 组合弹幕 ----
        ok += Write("Combo/Flower",
            Phase(0f, 12f, FlowerRot(90f, 450f)),     // Rot_TopLeft
            Phase(0f, 12f, FlowerRot(90f, -270f)),    // Rot_TopRight
            Phase(0f, 12f, FlowerRot(-90f, -450f)),   // Rot_DownLeft
            Phase(0f, 12f, FlowerRot(-90f, 270f)),    // Rot_DownRight
            Phase(0f, 12f, FlowerRot(180f, 540f)),    // Rot_LeftDown
            Phase(0f, 12f, FlowerRot(180f, -180f)),   // Rot_LeftUp
            Phase(0f, 12f, FlowerRot(0f, -360f)),     // Rot_RightDown
            Phase(0f, 12f, FlowerRot(0f, 360f)),      // Rot_RightUp
            Phase(2f, 10f, Circle1()));

        ok += Write("Combo/Misdirection",
            Phase(0f, 1.5f, Sector(0.5f, new[] { 2.5f, 2.25f, 2f }, 11, 180f, 0f)),   // CircularSector1
            Phase(1f, 1.5f, Sector(1f, new[] { 2.5f, 2f }, 14, 180f, 3f)),            // CircularSector2
            Phase(2f, 1.5f, Sector(1f, new[] { 4f }, 7, 90f, -2f)),                   // CircularSector3
            Phase(3f, 1.5f, Sector(3f, new[] { 2f }, 4, 45f, -3f)));                  // CircularSector4

        ok += Write("Combo/RR",                                                       // 原 Rotation1 资产已丢失，用默认值
            Phase(0f, 5f, new RotationPattern()),
            Phase(0.8f, 4.3f, new RotationPattern()),
            Phase(1.5f, 3.5f, new RotationPattern()));

        AssetDatabase.SaveAssets();
        Debug.Log($"[BulletNodeMigration] 迁移完成：{ok} 张卡。Rotation 与 RR 用的是旋转图案默认值，请手动检查。");
    }

    // ---------------- 旧资产参数 ----------------

    private static CirclePattern Circle1()
        => new CirclePattern { interval = 2f, bulletSpeed = 8f, bulletCount = 24, offsetY = -0.1f };

    private static ChaoticPattern Chaotic(float interval, float minSpeed, float maxSpeed, int count)
        => new ChaoticPattern { interval = interval, minSpeed = minSpeed, maxSpeed = maxSpeed, bulletCountPerBurst = count };

    private static RotationPattern FlowerRot(float from, float to)
        => new RotationPattern
        {
            interval = 0.15f, bulletSpeed = 4f, initialAngle = from, endAngle = to, rotationDuration = 12f,
            enableAcceleration = false, accelerationRate = 3f, accelerationCoef = 0.3f
        };

    private static CircularSectorPattern Sector(float interval, float[] speeds, int sections, float angle, float radius)
        => new CircularSectorPattern
        {
            interval = interval, layerSpeeds = speeds, sectionCount = sections,
            angleRange = angle, radius = radius, offsetY = 0f
        };

    // ---------------- 工具 ----------------

    private static BulletPhase Phase(float delay, float duration, BulletPatternBase pattern)
        => new BulletPhase { startDelay = delay, duration = duration, pattern = pattern };

    private static int Write(string name, params BulletPhase[] phases)
    {
        string path = Root + name + ".asset";
        BulletNode node = AssetDatabase.LoadAssetAtPath<BulletNode>(path);
        if (node == null)
        {
            Debug.LogWarning($"[BulletNodeMigration] 找不到 {path}，跳过。");
            return 0;
        }

        Undo.RecordObject(node, "迁移弹幕卡");
        node.phases = new List<BulletPhase>(phases);
        EditorUtility.SetDirty(node);
        return 1;
    }
}
