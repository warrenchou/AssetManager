using UnityEngine;

namespace FunPlus.AssetManagement
{
    public abstract class MonoBehaviourSingle<T> : MonoBehaviour where T : MonoBehaviour
    {
        static T _instance;

        public static bool HasInstance
        { get { return _instance != null; } }

        public static bool IsReleased { get; private set; } = false;
        public static bool IsAlive { get { return !IsReleased && HasInstance; } }

        static T Instance
        {
            get
            {
                if (IsReleased)
                {
                    Debug.LogError($"SingletonMonoBehaviour<{typeof(T).Name}> is Released.");
                    return null;
                }

                if (_instance == null)
                {
                    // 清理已有对象
                    {
                        T[] objs = FindObjectsOfType<T>();
                        if (objs != null && objs.Length > 0)
                        {
                            for (int i = 0; i < objs.Length; ++i)
                            {
                                MonoBehaviourSingle<T> t = objs[i] as MonoBehaviourSingle<T>;
                                if (t != null)
                                {
                                    if (Application.isEditor || Debug.isDebugBuild)
                                    {
                                        Debug.LogError(string.Format("MonoBehaviourSingle destroy exist {0}  {1}", t.name, t.GetType().Name));
                                    }
                                    t.Release();
                                    GameObject.DestroyImmediate(t.gameObject);
                                }
                            }
                            IsReleased = false;
                        }
                    }

                    GameObject gameObject = new GameObject();
                   
                    _instance = gameObject.AddComponent<T>();
                    (_instance as MonoBehaviourSingle<T>).OnInit();

                    string objName = typeof(T).Name;
                    gameObject.name = string.Format("!_{0}", objName);

                    if (Application.isEditor)
                    {
                        Debug.LogError(string.Format("<color=green> Create Instance {0} </color>", gameObject.name), _instance);
                    }

                    if (Application.isPlaying)
                    {
                        DontDestroyOnLoad(_instance.gameObject);
                    }
                }

                return _instance;
            }
        }

        public static void ReleaseSingle()
        {
            if ( _instance != null )
            {
                (_instance as MonoBehaviourSingle<T>).Release();
            }
        }
        public static T GetInstance()
        {
            return Instance;
        }

        public static T Create()
        {
            if (IsReleased)
            {
                IsReleased = false;
            }
            
            if ( HasInstance )
            {
                Debug.LogError(string.Format("{0} is Init before Create", typeof(T)));
            }

            return Instance;
        }


        protected abstract void OnInit();
        protected abstract void OnRelease();


//         private void Awake()
//         {
//             if (_instance == null)
//             {
//                 _instance = this as T;
//             }
// 
//             OnInit();
//         }


        public void Release()
        {
            OnRelease();
            IsReleased = true;

            if (_instance != null)
            {
                GameObject.DestroyImmediate(_instance);
                _instance = null;
            }
        }

        private void OnDestroy()
        {
            if ( !IsReleased )
            {
                Debug.LogError(string.Format("{0} is Destory but not call Release()", this.GetType()));

                OnRelease();
                IsReleased = true;
            }
        }
    }
}