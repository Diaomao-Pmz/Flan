using System.Collections;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

[CreateAssetMenu(fileName = "NewFullScreenLinePattern", menuName = "ScriptableObjects/BulletPattern/FullScreenLine")]
public class FullScreenLinePattern : BulletPatternBase
{
    public float bulletSpawnInterval;
    public int bulletCount;
    public float freezeTime;
    public Vector2 startPosFromCam;
    public Vector2 endPosFromCam;
    public float minSpeed;
    public float maxSpeed;

    public override void Spawn(BulletSpawnContext ctx)
    {
        ctx.host.StartCoroutine(DoSpawnLine(ctx));
    }

    private IEnumerator DoSpawnLine(BulletSpawnContext ctx)
    {
        //store bullet instances so can unfreeze them at the end
        Enemy_Projectile[] projectiles = new Enemy_Projectile[bulletCount];
        int index = 0;

        Vector2 camPos = Camera.main.transform.position;
        Vector2 start = camPos + startPosFromCam;
        Vector2 end = camPos + endPosFromCam;
        Vector2 diff = end - start;
        Vector2 dir = diff.normalized;
        float distance = diff.magnitude;

        Debug.Log($"start: {start}, end: {end}, dir: {dir}, distance: {distance}");

        //how many distance increment we have for each bullet
        float step = distance / (bulletCount-1);

        for(int i = 0; i < bulletCount; i++)
        {
            Debug.Log($"index {i} bullet position: {start}");
            //spawn the bullet, store it in array
            float speed = Random.Range(minSpeed, maxSpeed);
            Vector2 bulletDir = new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f)).normalized;
            GameObject obj = ctx.host.SpawnProjectile(bulletDir, start, ctx.projectileKey, speed);
            start += dir * step;
            projectiles[index] = obj.GetComponent<Enemy_Projectile>();
            projectiles[index].isControlledByFormation = true;
            index++;

            yield return new WaitForSeconds(bulletSpawnInterval);
        }

        yield return new WaitForSeconds(freezeTime);

        foreach(Enemy_Projectile b in projectiles)
        {
            b.isControlledByFormation = false;
        }
    }
}
