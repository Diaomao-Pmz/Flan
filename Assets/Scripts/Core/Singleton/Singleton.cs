using UnityEngine;

public class Singleton<T> : MonoBehaviour where T : Singleton<T>
{
    //µ¥ÀýÊµÀý
    public static T Instance;

    private void Awake()
    {
        if(Instance != null)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = (T)this;
            Debug.Log("Instance created");
        }
    }
}
