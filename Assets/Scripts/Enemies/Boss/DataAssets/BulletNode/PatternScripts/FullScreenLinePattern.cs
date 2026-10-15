using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 全屏直线：在屏幕上拉一条线（起点 → 终点，相对摄像机中心），沿线一颗颗生成子弹；
/// 整条线放完后子弹先原地定住 freezeTime 秒，然后同时朝各自的随机方向飞出。
///
/// 比喻：先沿着一条线把"地雷"一颗颗埋好，埋完等几秒，所有地雷一起炸开四散。
///
/// 通用设置里：
///   · 发射间隔 = 隔多久再拉一整条线（一般设得比本段持续时间长，这一段就只拉一次）
///   · 子弹速度 不使用 —— 飞出速度在下面的 最小 ~ 最大 之间随机
///   · 大小 / 存活时间 / 外观 / 加速 照常生效
///
/// （来源：main 分支「新增全屏弹幕符卡」，改写为内嵌图案）
/// </summary>
[Serializable, BulletPatternInfo("全屏直线 FullScreenLine")]
public class FullScreenLinePattern : BulletPatternBase
{
    [Header("--- 全屏直线专属 ---")]
    [Tooltip("一条线上放多少颗")]
    [Min(1)]
    public int bulletCount = 20;

    [Tooltip("相邻两颗子弹出现的间隔（秒）—— 子弹是沿线依次冒出来的")]
    [Min(0f)]
    public float bulletSpawnInterval = 0.1f;

    [Tooltip("整条线放完后，再定住多久才一起飞出（秒）")]
    [Min(0f)]
    public float freezeTime = 3f;

    [Tooltip("线的起点：相对摄像机中心的偏移")]
    public Vector2 startPosFromCam = new Vector2(-4.67f, 4.59f);

    [Tooltip("线的终点：相对摄像机中心的偏移")]
    public Vector2 endPosFromCam = new Vector2(6.86f, 0f);

    [Tooltip("飞出速度随机范围：最小")]
    public float minSpeed = 3f;

    [Tooltip("飞出速度随机范围：最大")]
    public float maxSpeed = 3f;

    public override void Spawn(BulletSpawnContext ctx)
    {
        if (ctx.host == null) return;
        ctx.host.StartCoroutine(SpawnLine(ctx.host));
    }

    private IEnumerator SpawnLine(BAE_BulletEmitter host)
    {
        Camera cam = Camera.main;
        Vector2 camPos = cam != null ? (Vector2)cam.transform.position : Vector2.zero;
        Vector2 start = camPos + startPosFromCam;
        Vector2 end = camPos + endPosFromCam;

        // 记下定住的子弹，等会儿一起放开
        List<Enemy_Projectile> frozen = new List<Enemy_Projectile>(bulletCount);

        for (int i = 0; i < bulletCount; i++)
        {
            // 沿线均匀分布：第一颗在起点，最后一颗在终点
            float t = bulletCount > 1 ? (float)i / (bulletCount - 1) : 0f;
            Vector2 pos = Vector2.Lerp(start, end, t);

            Vector2 dir = new Vector2(UnityEngine.Random.Range(-1f, 1f), UnityEngine.Random.Range(-1f, 1f));
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right;
            float speed = UnityEngine.Random.Range(minSpeed, maxSpeed);

            GameObject obj = host.SpawnBullet(this, dir, pos, speed);
            Enemy_Projectile proj = obj != null ? obj.GetComponent<Enemy_Projectile>() : null;
            if (proj != null)
            {
                proj.isControlledByFormation = true;   // 定住：不自己飞
                frozen.Add(proj);
            }

            if (bulletSpawnInterval > 0f)
                yield return new WaitForSeconds(bulletSpawnInterval);
        }

        if (freezeTime > 0f)
            yield return new WaitForSeconds(freezeTime);

        // 一起放开（期间已被回收的子弹跳过）
        for (int i = 0; i < frozen.Count; i++)
        {
            if (frozen[i] != null && frozen[i].gameObject.activeInHierarchy)
                frozen[i].isControlledByFormation = false;
        }
    }
}
