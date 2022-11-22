using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Object = System.Object;

namespace FunPlus.AssetManagement
{
    public class AssetManagerEx : MonoBehaviourSingle<AssetManagerEx>
    {
        public enum ForceMode
        {
            None,
            ForceSync,
            ForceAsync,
        }

        #region Handle

        private interface LoadHandle
        {
            bool TryGetLoaded(out AssetHandle assetHandle);
            bool Load(AssetLoadRequest req);
            bool LoadAsync(AssetLoadRequest req);
        }
        
        private class  InvalidLoadHandle : LoadHandle
        {
            public bool TryGetLoaded(out AssetHandle assetHandle)
            {
                assetHandle = AssetHandle.invalid;
                return false;
            }

            public bool Load(AssetLoadRequest req)
            {
                req.InternalLoaded();
                return false;
            }

            public bool LoadAsync(AssetLoadRequest req)
            {
                req.InternalLoaded();
                return false;
            }
        }
        
        private class ABHandle : LoadHandle
        {
            public ABLoader abLoader;
            public string abPath;
            public string assetName;
            public bool TryGetLoaded(out AssetHandle assetHandle)
            {
                return abLoader.TryGetloaded(abPath, assetName, out assetHandle);
            }

            public bool Load(AssetLoadRequest req)
            {
                req.priority = (int)AssetLoadPriority.Priority_Sync;
                GetInstance().OnAssetLoadRequest?.Invoke(assetName);
                return abLoader.LoadFromABAsync(abPath,assetName,req);
            }

            public bool LoadAsync(AssetLoadRequest req)
            {
                GetInstance().OnAssetLoadRequest?.Invoke(assetName);
                return abLoader.LoadFromABAsync(abPath,assetName,req);
            }
        }

        #endregion
        


        public Action<string> OnAssetLoadRequest;
        public static ForceMode forceMode { get; set; } = ForceMode.None;

        private List<ABLoader> abLoaders = new List<ABLoader>();
        private Dictionary<string, LoadHandle> loadHandleCache = new Dictionary<string, LoadHandle>();
        private AssetRefManager refManager = new AssetRefManager("InstantiateRefManager");
        private PriorityQueue<AssetLoadRequest> requestQueue = new PriorityQueue<AssetLoadRequest>();
        private UnityEngine.Coroutine loadProcessCoroutine;
        private List<AssetLoadRequest> loadingReqs = new List<AssetLoadRequest>();
        private Dictionary<Object, AssetRefList> objRefListMap = new Dictionary<Object, AssetRefList>();
        //资源关联的引用，key是asset的instanceId,value是关联的引用
        //如果是ab加载的资源，则引用关联ab包，而不是asset本身
        //如果是Resouce、Web加载的资源，则引用关联asset本身
        private Dictionary<int, AssetRef> assetRefDict = new Dictionary<int, AssetRef>();
        protected override void OnInit()
        {

        }

        protected override void  OnRelease()
        {
            loadHandleCache.Clear();
            foreach (var abLoader in abLoaders)
            {
                abLoader.Release();
            }
            abLoaders.Clear();
            refManager.Clear();
        }

        public AssetLoadRequest LoadAssetAsync<T>(string path
            , Action<AssetLoadRequest> cb = null
            , int priority = (int)AssetLoadPriority.Priority_Common) where T : UnityEngine.Object
        {
            return LoadAsync(path,null, cb, typeof(T), priority);
        }
        
        public AssetLoadRequest LoadAssetAsync<T>(string path
            , UnityEngine.GameObject autoRefGameObject
            , Action<AssetLoadRequest> cb = null
            , int priority = (int)AssetLoadPriority.Priority_Common
        ) where T : UnityEngine.Object
        {
            return LoadAsync(path, autoRefGameObject, cb, typeof(T), priority);
        }
        
        public AssetLoadRequest LoadAsync(string path
            , UnityEngine.GameObject autoRefGameObject
            , Action<AssetLoadRequest> cb = null
            , Type type = null
            , int priority = (int)AssetLoadPriority.Priority_Common
        )
        {
            if (string.IsNullOrEmpty(path))
            {
                UnityEngine.Debug.LogError("empty path.");
                return null;
            }
            var req = AssetLoadRequest.Get();
            req.path = path;
            req._type = type;
            req.onCompleted += cb;
            req.RefGameObject = autoRefGameObject;
            req.priority = priority;
            StartLoad(req);
            return req;
        }
        
        public void StartLoad(AssetLoadRequest req)
        {
            if (req == null)
            {
                return;
            }
            
            if (forceMode == ForceMode.ForceSync || req.priority <= (int)AssetLoadPriority.Priority_Sync)
            {
                DoLoadSync(req);
                return;
            }
            if (loadProcessCoroutine == null)
            {
                loadProcessCoroutine = StartCoroutine(AssetLoadProcess());
                StartCoroutine(AutoClearInvalidAssetRef());
            }
            requestQueue.Enqueue(req.priority, req);
        }
        
        bool DoLoadSync(AssetLoadRequest req)
        {
            var handle = GetLoadHandle(req.path, GetAssetType(req));
            if (handle.TryGetLoaded(out var assetHandle))
            {
                req._assetHandle = assetHandle;
                req.InternalLoaded();
                return false;
            }
            frameLoadCount++;
            return handle.Load(req);
        }
        
        bool DoLoadAsync(AssetLoadRequest req)
        {
            loadingReqs.Add(req);

                var handle = GetLoadHandle(req.path, GetAssetType(req));
                if (handle.TryGetLoaded(out var assetHandle))
                {
                    req._assetHandle = assetHandle;
                    req.InternalLoaded();
                    return false;
                }
                frameLoadCount++;
                return handle.LoadAsync(req);
        }
        
        private AssetTypeEx GetAssetType(AssetLoadRequest req)
        {
            if (req._type == null)
            {
                //默认
                return AssetTypeEx.Asset;
            }

            if (req._type == typeof(UnityEngine.AssetBundle))
            {
                return AssetTypeEx.AssetBundle;
            }

            return AssetTypeEx.Asset;
        }
        
        public int frameLoadCount { get; private set; }
        public int maxLoadCountPerFrame {get; set;} = 8;
        public int maxFastLoadCountPerFrame { get; set; } = 1;
          IEnumerator AssetLoadProcess()
        {
            while (true)
            {
                while (!requestQueue.IsEmpty())
                {
                    if (frameLoadCount >= maxLoadCountPerFrame)
                    {
                        yield return null;
                        frameLoadCount = 0;
                    }
                    else
                    {
                        var req = requestQueue.Dequeue();
                        if (!req.isValid)
                        {
                            continue;
                        }
                        if ( (req.priority <= (int)AssetLoadPriority.Priority_Fast && frameLoadCount < maxFastLoadCountPerFrame) 
                             && forceMode != ForceMode.ForceAsync)
                        {
                            DoLoadSync(req);
                        }
                        else
                        {

                            DoLoadAsync(req);
                        }
                    }
                }
                yield return null;
                frameLoadCount = 0;
            }
        }
          
        private LoadHandle GetLoadHandle(string assetPath, AssetTypeEx assetType = AssetTypeEx.Asset)
        {
            LoadHandle handle;
            //有缓存直接返回
            if (loadHandleCache.TryGetValue(assetPath, out handle))
            {
                return handle;
            }
            
            {
                string abPath = null;
                string assetName = null;
                ABLoader abLoader = null;
                if (assetType == AssetTypeEx.AssetBundle)
                {
                    abLoader = abLoaders.Find((loader) => assetPath.StartsWith(loader.rootPath));
                    if (abLoader == null)
                    {
                        UnityEngine.Debug.LogError("abLoader not find for asset bundle:" + assetPath);
                        handle = new InvalidLoadHandle();
                        loadHandleCache[assetPath] = handle;
                        return handle;
                    }
                    int index = assetPath.IndexOf(abLoader.rootPath, StringComparison.Ordinal);
                    abPath = assetPath.Remove(index, abLoader.rootPath.Length).TrimStart('/', '\\');
                    assetName = "";
                }
                else
                {
                    abLoader = abLoaders.Find((loader) => loader.GetABPath(assetPath, out abPath, out assetName));
                }

                if (abLoader == null)
                {
                    UnityEngine.Debug.LogError("path not valid:" + assetPath);
                    handle = new InvalidLoadHandle();
                    loadHandleCache[assetPath] = handle;
                    return handle;
                }

                handle = new ABHandle()
                {
                    abLoader = abLoader,
                    assetName = assetName,
                    abPath = abPath,
                };
            }
            
            loadHandleCache[assetPath] = handle;
            return handle;
        }
        
        public float clearInvalidObjInterval = 1f;
        IEnumerator AutoClearInvalidAssetRef()
        {
            while (true)
            {
                ClearInvalidAssetRef();
                yield return new WaitForSeconds(clearInvalidObjInterval);
            }
        }
        
        List<int> tempRefs = new List<int>();
        List<Object> tmpObjs = new List<Object>();
        private void ClearInvalidAssetRef()
        {
            foreach(var pair in objRefListMap)
            {
                if (pair.Key == null)
                {
                    pair.Value.ClearRef();
                    tmpObjs.Add(pair.Key);
                }
            }

            foreach(var key in tmpObjs)
            {
                objRefListMap.Remove(key);
            }
            tmpObjs.Clear();

            //清除失效的资源到ab引用映射
            tempRefs.Clear();
            foreach (var pair in assetRefDict)
            {
                if (!pair.Value.isValid) //引用已经失效了，不再被管理了
                {
                    tempRefs.Add(pair.Key);
                }
            }
            foreach (var key in tempRefs)
            {
                assetRefDict.Remove(key);
            }
            tempRefs.Clear();
        }
    }
}