using System.Collections.Generic;
using UnityEngine;

namespace  FunPlus.AssetManagement
{
    public class AssetLoadRequest : IGCPool
    {
        // 加载路径
        public string path { get; set; }
        //
        public AssetHandle _assetHandle { get; set; } = AssetHandle.invalid;
        // 
        public Object asset
        {
            get
            {
                if (!_assetHandle.isValid)
                {
                    return null;
                }

                return _assetHandle.asset;
            }
        }
        
        public System.Type _type { get; set; }
        public object Current { get; private set; }
        // 是否完成
        public bool IsDone { get; private set; } = false;
        
        // 加载优先级,越小越优先
        public int priority = (int)AssetLoadPriority.Priority_Common;

        // 关联的gameobject
        private GameObject _refGameObject;
        private bool hasAutoRefGameObject;

        public GameObject RefGameObject
        {
            get
            {
                return _refGameObject;
            }
            set
            {
                _refGameObject = value;
                hasAutoRefGameObject = value != null;
            }
        }

        private bool _isValid = true;
        public bool isValid
        {
            get
            {
                if (!_isValid)
                {
                    return false;
                }

                if (hasAutoRefGameObject)
                {
                    return RefGameObject != null;
                }

                return true;
            }
        }
        
        
        public bool MoveNext()
        {
            return !IsDone && isValid;
        }
        
        public void Reset()
        {
            path = null;
            IsDone = false;
            _onCompleted?.Clear();
            priority = (int)AssetLoadPriority.Priority_Common;;
            RefGameObject = null;
            hasAutoRefGameObject = false;
           // attachDatas = null;
           _assetHandle = AssetHandle.invalid;
           _type = null;
           _isValid = false;
        }
        
        // 完成回调
        private DelegateList<AssetLoadRequest> _onCompleted;
        public event System.Action<AssetLoadRequest> onCompleted
        {
            add
            {
                if (IsDone)
                {
                    value(this);
                    return;
                }

                if (_onCompleted == null)
                {
                    _onCompleted = new DelegateList<AssetLoadRequest>();
                }
                _onCompleted.Add(value);
            }

            remove => _onCompleted?.Remove(value);
        }

        public void Release()
        {
            if (!_isValid)
            {
                return;
            }

            _isValid = false;
            _onCompleted?.Clear();
            Free(this);
        }
        
        //回收任务，减少gc
        public static void Free(AssetLoadRequest req)
        {
            if (AssetManagerEx.GetInstance() != null)
            {
               // AssetManagerEx.GetInstance().PushAutoFreeRequest(req);
            }
        }
        
        private Dictionary<string, System.Object> attachDatas;
        public void SetData(string key, System.Object data)
        {
            if (attachDatas == null)
            {
                attachDatas = new Dictionary<string, object>();
            }

            attachDatas[key] = data;
        }
        public T GetData<T>(string key) where T : class
        {
            if (attachDatas == null)
            {
                return null;
            }

            if (!attachDatas.TryGetValue(key, out var val))
            {
                return null;
            }

            return val as T;
        }
        
        public T GetDataValue<T>(string key, T defaultValue) where T : struct
        {
            if (attachDatas == null)
            {
                return defaultValue;
            }

            if (!attachDatas.TryGetValue(key, out var val))
            {
                return defaultValue;
            }

            return (T) val;
        }

        public void InternalLoaded()
        {
            if (IsDone)
            {
                return;
            }

            IsDone = true;

            Complete();
        }
        
        private void Complete()
        {
            if (!isValid)
            {
                return;
            }

            if (_assetHandle.isValid)
            {
                if (hasAutoRefGameObject)
                {
                    if (RefGameObject != null)
                    {
                        //AssetManagerEx.GetInstance().AddAssetRefToObject(RefGameObject, _assetHandle.assetRef);
                    }
                }
            }

            if (_onCompleted != null)
            {
                try
                {
                    _onCompleted?.Invoke(this);
                }
                catch (System.Exception e)
                {
                    Debug.LogError(e.ToString());
                }

                _onCompleted?.Clear();
            }
        }
        
        private static GCPool<AssetLoadRequest> gcPool = new GCPool<AssetLoadRequest>();


        //获取一个加载任务
        //默认是自动回收的（加载成功后执行回收，如果有回调会在回调结束后执行）
        //如果有手动管理的需求，需要new一个出来
        public static AssetLoadRequest Get()
        {
            var req = gcPool.Get();
            req._isValid = true;
            return req;
        }
        
        public static void DoFree(AssetLoadRequest req)
        {
            gcPool.Free(req);
        }
    }

    // asset load batch，用于管理多个request一起加载完成后生成回调
    public class AssetsLoadBatch
    {
        public delegate void OnBatchCompleted(AssetsLoadBatch batch);
        private OnBatchCompleted onBatchCompleted;
        
        protected List<AssetLoadRequest> reqs = new List<AssetLoadRequest>();
        
        private int loadingCount;
        
        public object Current { get; private set; }
        
        public void Start(OnBatchCompleted onCompleted)
        {
            onBatchCompleted = onCompleted;
            var assetMgr = AssetManagerEx.GetInstance();
            loadingCount = reqs.Count;
            foreach (var req in reqs)
            {
               //assetMgr.StartLoad(req);
            }
        }

        public bool isDone
        {
            get { return loadingCount <= 0; }
            
        }
        public void AddRequest(AssetLoadRequest req)
        {
            reqs.Add(req);
            req.onCompleted += OnRequestCompleted;
        }
        
        private void OnRequestCompleted(AssetLoadRequest req)
        {
            --loadingCount;
            //防止batch中被释放
            if (req._assetHandle.isValid)
            {
                req._assetHandle.RefAsset();
            }

            if (!isDone)
            {
                return;
            }

            OnCompleted();
        }
        
        protected virtual void OnCompleted()
        {
            //全部结束，释放之前持有的引用，执行回调
            foreach (var req in reqs)
            {
                if (req._assetHandle.isValid)
                {
                    req._assetHandle.UnRefAsset();
                }
            }

            //全部加载结束
            if (onBatchCompleted != null)
            {
                onBatchCompleted(this);
            }
        }
        
        public bool MoveNext()
        {
            return !isDone;
        }
        
        public void Reset()
        {
            reqs.Clear();
            onBatchCompleted = null;
            loadingCount = 0;
        }
    }
}