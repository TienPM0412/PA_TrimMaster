using UnityEngine;
public class Singleton<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T instance;
    public static T Ins => instance != null ? instance : (instance = FindObjectOfType<T>());
    public static bool HasInstance => Ins != null;
}
