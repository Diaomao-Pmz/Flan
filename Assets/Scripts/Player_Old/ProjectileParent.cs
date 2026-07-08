using UnityEngine;

public class ProjectileParent : MonoBehaviour
{
    private void Update()
    {
        if(transform.childCount == 0)
        {
            Destroy(gameObject);
        }
    }
}
